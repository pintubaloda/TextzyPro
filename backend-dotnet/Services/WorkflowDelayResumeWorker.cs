using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class WorkflowDelayResumeWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkflowRuntimeOptions> runtimeOptions,
    SensitiveDataRedactor redactor,
    ILogger<WorkflowDelayResumeWorker> logger) : BackgroundService
{
    private const int TenantBatchSize = 25;
    private static readonly TimeSpan TenantCacheTtl = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _tenantCacheLock = new(1, 1);
    private List<TenantScanTarget> _tenantCache = [];
    private DateTime _tenantCacheExpiresUtc = DateTime.MinValue;
    private int _tenantCursor;

    private sealed class DelayResumePayload
    {
        public Guid RunId { get; init; }
        public Guid VersionId { get; init; }
        public string InboundRecipient { get; init; } = string.Empty;
        public string InboundMessageText { get; init; } = string.Empty;
        public string InboundMessageId { get; init; } = string.Empty;
        public string PhoneNumberId { get; init; } = string.Empty;
        public string DefinitionJson { get; init; } = "{}";
        public string StartNodeId { get; init; } = string.Empty;
        public Dictionary<string, object?> Payload { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAndResumeAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Workflow delay-resume scan failed: {Error}", SafeError(ex.Message));
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ScanAndResumeAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var tenancy = scope.ServiceProvider.GetRequiredService<TenancyContext>();
        var now = DateTime.UtcNow;

        var tenants = await GetTenantBatchAsync(controlDb, ct);
        foreach (var tenant in tenants)
        {
            try
            {
                var tenantConn = string.IsNullOrWhiteSpace(tenant.DataConnectionString)
                    ? controlDb.Database.GetConnectionString()
                    : tenant.DataConnectionString;
                if (string.IsNullOrWhiteSpace(tenantConn)) continue;

                using var tenantDb = SeedData.CreateTenantDbContext(tenantConn);
                var jobs = await tenantDb.WorkflowScheduledMessages
                    .Where(x => x.TenantId == tenant.Id &&
                                (x.Status == "pending" || x.Status == "retry") &&
                                (x.ScheduledForUtc <= now || (x.NextRetryAtUtc != null && x.NextRetryAtUtc <= now)))
                    .OrderBy(x => x.ScheduledForUtc)
                    .Take(25)
                    .ToListAsync(ct);
                if (jobs.Count == 0) continue;

                tenancy.SetTenant(tenant.Id, tenant.Slug, tenantConn);
                var engine = new WorkflowExecutionEngine(
                    controlDb,
                    tenantDb,
                    scope.ServiceProvider.GetRequiredService<MessagingService>(),
                    scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(),
                    scope.ServiceProvider.GetRequiredService<SecretCryptoService>(),
                    redactor,
                    runtimeOptions);

                foreach (var job in jobs)
                {
                    try
                    {
                        job.Status = "processing";
                        job.UpdatedAtUtc = DateTime.UtcNow;
                        await tenantDb.SaveChangesAsync(ct);

                        var payload = JsonSerializer.Deserialize<DelayResumePayload>(
                            string.IsNullOrWhiteSpace(job.MessageContent) ? "{}" : job.MessageContent,
                            new JsonSerializerOptions(JsonSerializerDefaults.Web))
                            ?? new DelayResumePayload();

                        var flow = await tenantDb.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Id == job.FlowId, ct);
                        if (flow is null) throw new InvalidOperationException("scheduled_flow_not_found");
                        var targetVersionId = payload.VersionId != Guid.Empty
                            ? payload.VersionId
                            : flow.PublishedVersionId ?? flow.CurrentVersionId ?? Guid.Empty;
                        if (targetVersionId == Guid.Empty) throw new InvalidOperationException("scheduled_flow_version_missing");

                        var version = await tenantDb.AutomationFlowVersions
                            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.FlowId == flow.Id && x.Id == targetVersionId, ct);
                        if (version is null) throw new InvalidOperationException("scheduled_flow_version_not_found");

                        var run = await tenantDb.AutomationRuns
                            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.Id == payload.RunId, ct);
                        if (run is null)
                        {
                            run = new AutomationRun
                            {
                                Id = payload.RunId == Guid.Empty ? Guid.NewGuid() : payload.RunId,
                                TenantId = tenant.Id,
                                FlowId = flow.Id,
                                VersionId = version.Id,
                                Mode = "live",
                                TriggerType = flow.TriggerType,
                                IdempotencyKey = $"auto-delay:{flow.Id}:{job.Id}",
                                TriggerPayloadJson = JsonSerializer.Serialize(payload.Payload),
                                Status = "running",
                                StartedAtUtc = DateTime.UtcNow
                            };
                            tenantDb.AutomationRuns.Add(run);
                        }
                        else
                        {
                            run.Status = "running";
                            run.FailureReason = string.Empty;
                            run.CompletedAtUtc = null;
                        }

                        await engine.ExecuteAsync(new WorkflowExecutionEngine.ExecuteRequest
                        {
                            TenantId = tenant.Id,
                            FlowId = flow.Id,
                            PhoneNumberId = payload.PhoneNumberId,
                            InboundMessageId = string.IsNullOrWhiteSpace(payload.InboundMessageId) ? $"scheduled:{job.Id}" : payload.InboundMessageId,
                            InboundRecipient = payload.InboundRecipient,
                            InboundMessageText = payload.InboundMessageText,
                            DefinitionJson = string.IsNullOrWhiteSpace(payload.DefinitionJson) ? version.DefinitionJson : payload.DefinitionJson,
                            Run = run,
                            Payload = payload.Payload,
                            StartNodeId = payload.StartNodeId
                        }, ct);

                        job.Status = "sent";
                        job.SentAtUtc = DateTime.UtcNow;
                        job.UpdatedAtUtc = DateTime.UtcNow;
                        job.FailureReason = string.Empty;
                        await tenantDb.SaveChangesAsync(ct);

                        controlDb.AuditLogs.Add(new AuditLog
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenant.Id,
                            ActorUserId = Guid.Empty,
                            Action = "waba.workflow.delay_resumed",
                            Details = $"flowId={flow.Id}; scheduledId={job.Id}; nodeId={job.NodeId}; status=sent",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await controlDb.SaveChangesAsync(ct);
                    }
                    catch (Exception ex)
                    {
                        job.RetryCount += 1;
                        var maxRetries = Math.Max(1, job.MaxRetries <= 0 ? runtimeOptions.Value.DelayMaxRetries : job.MaxRetries);
                        job.MaxRetries = maxRetries;
                        job.FailureReason = SafeError(ex.Message);
                        job.UpdatedAtUtc = DateTime.UtcNow;
                        if (job.RetryCount >= maxRetries)
                        {
                            job.Status = "failed";
                        }
                        else
                        {
                            job.Status = "retry";
                            var delaySeconds = Math.Min(300, (int)Math.Pow(2, job.RetryCount) * 5);
                            job.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                        }
                        await tenantDb.SaveChangesAsync(ct);
                        controlDb.AuditLogs.Add(new AuditLog
                        {
                            Id = Guid.NewGuid(),
                            TenantId = tenant.Id,
                            ActorUserId = Guid.Empty,
                            Action = "waba.workflow.delay_resume_failed",
                            Details = $"scheduledId={job.Id}; retry={job.RetryCount}; status={job.Status}; error={SafeError(ex.Message)}",
                            CreatedAtUtc = DateTime.UtcNow
                        });
                        await controlDb.SaveChangesAsync(ct);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Workflow delay-resume tenant scan failed for tenant={TenantId}: {Error}", tenant.Id, SafeError(ex.Message));
            }
        }
    }

    private static string SafeError(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return input.Length <= 500 ? input : $"{input[..500]}...";
    }

    private async Task<List<TenantScanTarget>> GetTenantBatchAsync(ControlDbContext controlDb, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (_tenantCache.Count == 0 || now >= _tenantCacheExpiresUtc)
        {
            await _tenantCacheLock.WaitAsync(ct);
            try
            {
                if (_tenantCache.Count == 0 || now >= _tenantCacheExpiresUtc)
                {
                    _tenantCache = await controlDb.Tenants.AsNoTracking()
                        .OrderBy(x => x.CreatedAtUtc)
                        .Select(x => new TenantScanTarget
                        {
                            Id = x.Id,
                            Slug = x.Slug,
                            DataConnectionString = x.DataConnectionString
                        })
                        .ToListAsync(ct);
                    _tenantCacheExpiresUtc = now.Add(TenantCacheTtl);
                    _tenantCursor = 0;
                }
            }
            finally
            {
                _tenantCacheLock.Release();
            }
        }

        if (_tenantCache.Count <= TenantBatchSize)
            return _tenantCache;

        var batch = new List<TenantScanTarget>(TenantBatchSize);
        for (var i = 0; i < TenantBatchSize; i++)
        {
            batch.Add(_tenantCache[_tenantCursor]);
            _tenantCursor = (_tenantCursor + 1) % _tenantCache.Count;
        }
        return batch;
    }

    private sealed class TenantScanTarget
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string DataConnectionString { get; init; } = string.Empty;
    }
}
