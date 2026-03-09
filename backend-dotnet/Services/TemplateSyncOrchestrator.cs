using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public class TemplateSyncOrchestrator(
    TenantDbContext tenantDb,
    TenancyContext tenancy,
    WhatsAppCloudService whatsapp,
    SensitiveDataRedactor redactor,
    ILogger<TemplateSyncOrchestrator> logger)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TenantLocks = new();

    public async Task EnsureInitialOrDailySyncAsync(bool force, CancellationToken ct = default)
    {
        if (!tenancy.IsSet) return;
        var gate = TenantLocks.GetOrAdd(tenancy.TenantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var cfg = await tenantDb.TenantWabaConfigs
                .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
                .OrderByDescending(x => x.ConnectedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (cfg is null || string.IsNullOrWhiteSpace(cfg.WabaId) || string.IsNullOrWhiteSpace(cfg.AccessToken))
                return;

            var now = DateTime.UtcNow;
            var due = force || !cfg.TemplatesSyncedAtUtc.HasValue || cfg.TemplatesSyncedAtUtc.Value <= now.AddDays(-1);
            if (!due) return;

            try
            {
                await whatsapp.SyncMessageTemplatesAsync(force || !cfg.TemplatesSyncedAtUtc.HasValue, ct);
                cfg.TemplatesSyncedAtUtc = DateTime.UtcNow;
                cfg.TemplatesSyncStatus = "ok";
                cfg.TemplatesSyncFailCount = 0;
                await tenantDb.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                cfg.TemplatesSyncedAtUtc = DateTime.UtcNow;
                cfg.TemplatesSyncStatus = $"failed:{redactor.RedactText(ex.GetType().Name)}";
                cfg.TemplatesSyncFailCount = Math.Max(0, cfg.TemplatesSyncFailCount) + 1;
                await tenantDb.SaveChangesAsync(ct);
                logger.LogWarning("Template auto-sync failed for tenant={TenantId}: {Error}", tenancy.TenantId, redactor.RedactText(ex.Message));
            }
        }
        finally
        {
            gate.Release();
        }
    }
}

