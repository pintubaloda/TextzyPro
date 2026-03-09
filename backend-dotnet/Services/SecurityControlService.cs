using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class SecurityControlService(ControlDbContext db)
{
    public async Task<TenantSecurityControl?> GetTenantControlAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.TenantSecurityControls.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
    }

    public async Task<bool> IsCircuitBreakerOpenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await GetTenantControlAsync(tenantId, ct);
        return row?.CircuitBreakerEnabled == true;
    }

    public async Task<int?> GetRatePerMinuteOverrideAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await GetTenantControlAsync(tenantId, ct);
        return row is null || row.RatePerMinuteOverride <= 0 ? null : row.RatePerMinuteOverride;
    }

    public async Task UpsertControlAsync(Guid tenantId, bool? circuitBreaker, int? ratePerMinuteOverride, Guid actorUserId, string reason, CancellationToken ct = default)
    {
        var row = await db.TenantSecurityControls.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (row is null)
        {
            row = new TenantSecurityControl
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId
            };
            db.TenantSecurityControls.Add(row);
        }

        if (circuitBreaker.HasValue) row.CircuitBreakerEnabled = circuitBreaker.Value;
        if (ratePerMinuteOverride.HasValue) row.RatePerMinuteOverride = Math.Max(0, ratePerMinuteOverride.Value);
        row.Reason = (reason ?? string.Empty).Trim();
        row.UpdatedByUserId = actorUserId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task AddSignalAsync(Guid? tenantId, string signalType, string severity, int countValue, string details, CancellationToken ct = default)
    {
        db.SecuritySignals.Add(new SecuritySignal
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SignalType = signalType,
            Severity = severity,
            Status = "open",
            CountValue = Math.Max(0, countValue),
            Details = details,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
