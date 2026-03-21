using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/ops/health")]
public sealed class TenantHealthController(
    ControlDbContext controlDb,
    TenantDbContext tenantDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    OpsMetricsService ops,
    BillingGuardService billingGuard) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(ApiRead)) return Forbid();

        var safeDays = Math.Clamp(days, 1, 90);
        var fromUtc = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        // WhatsApp outbound delivery snapshot (simple, message-table based)
        var outboundRows = await tenantDb.Messages
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && x.Channel == ChannelType.WhatsApp
                        && x.CreatedAtUtc >= fromUtc
                        && !string.Equals(x.Status, "Received", StringComparison.OrdinalIgnoreCase))
            .Select(x => new { x.Status, x.CreatedAtUtc })
            .Take(20000)
            .ToListAsync(ct);

        var outboundTotal = outboundRows.Count;
        var outboundQueued = outboundRows.Count(x => string.Equals(x.Status, "Queued", StringComparison.OrdinalIgnoreCase));
        var outboundFailed = outboundRows.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var outboundSuccess = outboundRows.Count(x =>
            string.Equals(x.Status, "Sent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Status, "Delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Status, "Read", StringComparison.OrdinalIgnoreCase));
        var outboundDeliveryRate = outboundTotal == 0 ? 0d : Math.Round((double)outboundSuccess * 100d / Math.Max(1, outboundTotal), 2);

        // Webhook + outbound latency (SLO)
        var webhook = await ops.GetWebhookLagAsync(tenancy.TenantId, safeDays, ct);
        var outboundLatency = await ops.GetOutboundLatencyAsync(tenantDb, tenancy.TenantId, safeDays, ct);

        // KYC success snapshot
        var kycRows = await controlDb.KycSessions
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= fromUtc)
            .Select(x => x.Status)
            .Take(20000)
            .ToListAsync(ct);
        var kycTotal = kycRows.Count;
        var kycVerified = kycRows.Count(x => string.Equals(x, "verified", StringComparison.OrdinalIgnoreCase));
        var kycFailed = kycRows.Count(x => string.Equals(x, "failed", StringComparison.OrdinalIgnoreCase));
        var kycPending = kycTotal - kycVerified - kycFailed;
        var kycSuccessRate = kycTotal == 0 ? 0d : Math.Round((double)kycVerified * 100d / Math.Max(1, kycTotal), 2);

        // Billing status + credit balances
        var sub = await controlDb.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        BillingPlan? plan = null;
        if (sub is not null)
            plan = await controlDb.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sub.PlanId, ct);

        var balances = await controlDb.TenantUsageCreditBalances
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .ToListAsync(ct);
        var creditBalances = balances.ToDictionary(x => x.MetricKey, x => x.UnitsRemaining, StringComparer.OrdinalIgnoreCase);

        var guardrails = await billingGuard.GetGuardrailsAsync(tenancy.TenantId, ct);

        var score = ComputeScore(outboundDeliveryRate, webhook, outboundLatency, kycSuccessRate, sub?.Status ?? string.Empty, creditBalances, guardrails);

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            tenantSlug = tenancy.TenantSlug,
            fromUtc,
            days = safeDays,
            whatsapp = new
            {
                outboundTotal,
                outboundSuccess,
                outboundFailed,
                outboundQueued,
                deliveryRatePct = outboundDeliveryRate
            },
            webhook,
            outbound = outboundLatency,
            kyc = new
            {
                total = kycTotal,
                verified = kycVerified,
                failed = kycFailed,
                pending = Math.Max(0, kycPending),
                successRatePct = kycSuccessRate
            },
            billing = new
            {
                subscription = sub is null ? null : new
                {
                    sub.Id,
                    sub.Status,
                    sub.BillingCycle,
                    sub.StartedAtUtc,
                    sub.RenewAtUtc,
                    sub.CancelledAtUtc
                },
                plan = plan is null ? null : new { plan.Code, plan.Name, plan.PricingModel },
                creditBalances
            },
            score
        });
    }

    private static object ComputeScore(
        double deliveryRatePct,
        OpsMetricsService.WebhookLagMetrics webhook,
        OpsMetricsService.OutboundSendLatencyMetrics outbound,
        double kycSuccessRatePct,
        string subscriptionStatus,
        Dictionary<string, int> creditBalances,
        CreditGuardrailSettings guardrails)
    {
        var reasons = new List<string>();
        var score = 100;

        if (deliveryRatePct > 0 && deliveryRatePct < 95)
        {
            score -= deliveryRatePct < 90 ? 25 : 15;
            reasons.Add($"WhatsApp delivery rate is low ({deliveryRatePct}%).");
        }

        if (webhook.P95Ms >= 15000)
        {
            score -= 20;
            reasons.Add($"Webhook lag p95 is high ({webhook.P95Ms}ms).");
        }
        else if (webhook.P95Ms >= 5000)
        {
            score -= 10;
            reasons.Add($"Webhook lag p95 is elevated ({webhook.P95Ms}ms).");
        }

        if (webhook.Pending > 0 && webhook.OldestPendingAgeSec >= 300)
        {
            score -= 10;
            reasons.Add($"Webhook queue has pending events (oldest {webhook.OldestPendingAgeSec}s).");
        }

        if (outbound.QueuedCount > 0 && outbound.OldestQueuedAgeSec >= 300)
        {
            score -= 10;
            reasons.Add($"Outbound queue has stuck messages (oldest {outbound.OldestQueuedAgeSec}s).");
        }

        if (kycSuccessRatePct > 0 && kycSuccessRatePct < 80)
        {
            score -= 10;
            reasons.Add($"KYC success rate is low ({kycSuccessRatePct}%).");
        }

        var sub = (subscriptionStatus ?? string.Empty).Trim().ToLowerInvariant();
        if (sub is not ("active" or "trial" or "trialing"))
        {
            score -= 25;
            reasons.Add($"Billing subscription status is {subscriptionStatus}.");
        }

        foreach (var (metricKey, threshold) in guardrails.LowBalanceThresholds)
        {
            if (threshold <= 0) continue;
            if (!creditBalances.TryGetValue(metricKey, out var remaining)) continue;
            if (remaining > threshold) continue;
            score -= 5;
            reasons.Add($"Low balance: {metricKey} remaining {remaining} (threshold {threshold}).");
        }

        score = Math.Clamp(score, 0, 100);
        var grade = score >= 90 ? "A" : score >= 80 ? "B" : score >= 65 ? "C" : score >= 50 ? "D" : "E";
        return new { score, grade, reasons = reasons.Take(8).ToList() };
    }
}

