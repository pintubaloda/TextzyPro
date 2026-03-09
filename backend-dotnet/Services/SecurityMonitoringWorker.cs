using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public class SecurityMonitoringWorker(
    IServiceScopeFactory scopeFactory,
    OutboundMessageQueueService outboundQueue,
    WabaWebhookQueueService webhookQueue,
    SensitiveDataRedactor redactor,
    ILogger<SecurityMonitoringWorker> logger) : BackgroundService
{
    private sealed record MonitorThresholds(
        int AuthFailureBurst,
        int TokenFailureBurst,
        int OutboundQueueCritical,
        int WebhookQueueCritical,
        int AuthFailureIpBurst,
        bool AutoResponseEnabled,
        int AutoResponseRateLimit);

    private static MonitorThresholds ReadThresholds(IConfiguration config)
    {
        int ReadInt(string key, int fallback) =>
            int.TryParse(config[key], out var parsed) && parsed > 0 ? parsed : fallback;

        var autoEnabled = !string.Equals(config["SecurityMonitoring:AutoResponseEnabled"], "false", StringComparison.OrdinalIgnoreCase);
        return new MonitorThresholds(
            AuthFailureBurst: ReadInt("SecurityMonitoring:AuthFailureBurst", 20),
            TokenFailureBurst: ReadInt("SecurityMonitoring:TokenFailureBurst", 10),
            OutboundQueueCritical: ReadInt("SecurityMonitoring:OutboundQueueCritical", 2000),
            WebhookQueueCritical: ReadInt("SecurityMonitoring:WebhookQueueCritical", 5000),
            AuthFailureIpBurst: ReadInt("SecurityMonitoring:AuthFailureIpBurst", 12),
            AutoResponseEnabled: autoEnabled,
            AutoResponseRateLimit: ReadInt("SecurityMonitoring:AutoResponseRateLimit", 20)
        );
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
                var controls = scope.ServiceProvider.GetRequiredService<SecurityControlService>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                var thresholds = ReadThresholds(config);

                var now = DateTime.UtcNow;
                var from = now.AddMinutes(-5);
                var authFailures = await db.PlatformRequestLogs
                    .CountAsync(x => x.CreatedAtUtc >= from && x.Path.Contains("/api/auth/login") && x.StatusCode >= 400, stoppingToken);
                if (authFailures >= thresholds.AuthFailureBurst)
                {
                    await controls.AddSignalAsync(null, "auth_failure_burst", "high", authFailures, "High login failure burst in last 5 minutes.", stoppingToken);
                }

                var tokenFailures = await db.WebhookEvents
                    .CountAsync(x => x.ReceivedAtUtc >= from && ((x.LastError ?? string.Empty).ToLower().Contains("token")), stoppingToken);
                if (tokenFailures >= thresholds.TokenFailureBurst)
                {
                    await controls.AddSignalAsync(null, "token_failure_burst", "high", tokenFailures, "Token-related webhook failures crossed threshold.", stoppingToken);
                }

                var abusiveIps = await db.PlatformRequestLogs
                    .Where(x => x.CreatedAtUtc >= from && x.Path.Contains("/api/auth/login") && x.StatusCode >= 400 && x.ClientIp != "")
                    .GroupBy(x => x.ClientIp)
                    .Select(g => new { Ip = g.Key, Count = g.Count() })
                    .Where(x => x.Count >= thresholds.AuthFailureIpBurst)
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToListAsync(stoppingToken);
                foreach (var ip in abusiveIps)
                {
                    await controls.AddSignalAsync(null, "auth_failure_ip_burst", "high", ip.Count, $"Suspicious auth failures from ip={ip.Ip}", stoppingToken);
                }

                var outboundDepth = await outboundQueue.GetDepthAsync(stoppingToken);
                if (outboundDepth >= thresholds.OutboundQueueCritical)
                {
                    await controls.AddSignalAsync(null, "outbound_queue_growth", "critical", (int)Math.Min(outboundDepth, int.MaxValue), "Outbound queue depth critical; circuit breaker recommended.", stoppingToken);
                }

                var webhookDepth = await webhookQueue.GetDepthAsync(stoppingToken);
                if (webhookDepth >= thresholds.WebhookQueueCritical)
                {
                    await controls.AddSignalAsync(null, "webhook_queue_growth", "critical", (int)Math.Min(webhookDepth, int.MaxValue), "Webhook queue depth critical.", stoppingToken);
                }

                if (thresholds.AutoResponseEnabled)
                {
                    var recentTokenFailuresByTenant = await db.WebhookEvents
                        .Where(x => x.ReceivedAtUtc >= from && x.TenantId != null && ((x.LastError ?? string.Empty).ToLower().Contains("token")))
                        .GroupBy(x => x.TenantId!.Value)
                        .Select(g => new { TenantId = g.Key, Count = g.Count() })
                        .Where(x => x.Count >= thresholds.TokenFailureBurst)
                        .ToListAsync(stoppingToken);

                    foreach (var row in recentTokenFailuresByTenant)
                    {
                        await controls.UpsertControlAsync(
                            row.TenantId,
                            circuitBreaker: true,
                            ratePerMinuteOverride: thresholds.AutoResponseRateLimit,
                            actorUserId: Guid.Empty,
                            reason: $"auto_response_token_failure_{row.Count}_{now:O}",
                            ct: stoppingToken);

                        await controls.AddSignalAsync(
                            row.TenantId,
                            "auto_response_circuit_breaker",
                            "critical",
                            row.Count,
                            "Circuit breaker auto-enabled due to token failure burst.",
                            stoppingToken);
                    }

                    if (outboundDepth >= thresholds.OutboundQueueCritical)
                    {
                        var activeTenantIds = await db.Tenants
                            .Select(x => x.Id)
                            .ToListAsync(stoppingToken);

                        foreach (var tenantId in activeTenantIds)
                        {
                            await controls.UpsertControlAsync(
                                tenantId,
                                circuitBreaker: null,
                                ratePerMinuteOverride: Math.Min(thresholds.AutoResponseRateLimit, 10),
                                actorUserId: Guid.Empty,
                                reason: $"auto_response_queue_backpressure_{outboundDepth}_{now:O}",
                                ct: stoppingToken);
                        }
                    }
                }

                var oldRows = await db.SecuritySignals
                    .Where(x => x.CreatedAtUtc < now.AddDays(-14))
                    .OrderBy(x => x.CreatedAtUtc)
                    .Take(2000)
                    .ToListAsync(stoppingToken);
                if (oldRows.Count > 0)
                {
                    db.SecuritySignals.RemoveRange(oldRows);
                    await db.SaveChangesAsync(stoppingToken);
                }

                var oldReplay = await db.WebhookReplayGuards
                    .Where(x => x.ExpiresAtUtc <= now)
                    .OrderBy(x => x.ExpiresAtUtc)
                    .Take(5000)
                    .ToListAsync(stoppingToken);
                if (oldReplay.Count > 0)
                {
                    db.WebhookReplayGuards.RemoveRange(oldReplay);
                    await db.SaveChangesAsync(stoppingToken);
                }

                var staleControls = await db.TenantSecurityControls
                    .Where(x => x.CircuitBreakerEnabled && x.Reason.ToLower().StartsWith("auto_response_") && x.UpdatedAtUtc < now.AddHours(-12))
                    .ToListAsync(stoppingToken);
                if (staleControls.Count > 0)
                {
                    foreach (var row in staleControls)
                    {
                        row.CircuitBreakerEnabled = false;
                        row.Reason = "auto_response_timeout_expired";
                        row.UpdatedAtUtc = now;
                        row.UpdatedByUserId = Guid.Empty;
                    }
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Security monitor iteration failed: {Error}", redactor.RedactText(ex.Message));
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
