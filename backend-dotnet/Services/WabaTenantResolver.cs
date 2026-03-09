using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public sealed class WabaTenantResolution
{
    public Guid TenantId { get; init; }
    public string TenantSlug { get; init; } = string.Empty;
    public string DataConnectionString { get; init; } = string.Empty;
}

public class WabaTenantResolver(
    IMemoryCache memoryCache,
    IDistributedCache distributedCache,
    ControlDbContext controlDb)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public async Task<WabaTenantResolution?> ResolveByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId)) return null;
        var memKey = $"waba:phone:{phoneNumberId}";
        if (memoryCache.TryGetValue<WabaTenantResolution>(memKey, out var cached))
            return cached;

        var dist = await distributedCache.GetStringAsync(memKey, ct);
        if (!string.IsNullOrWhiteSpace(dist))
        {
            var parts = dist.Split('|', 3);
            if (parts.Length == 3 && Guid.TryParse(parts[0], out var tid))
            {
                var row = new WabaTenantResolution
                {
                    TenantId = tid,
                    TenantSlug = parts[1],
                    DataConnectionString = parts[2]
                };
                memoryCache.Set(memKey, row, CacheTtl);
                return row;
            }
        }

        var tenants = await controlDb.Tenants.ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            try
            {
                using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var match = await tenantDb.Set<Models.TenantWabaConfig>()
                    .AnyAsync(x => x.TenantId == tenant.Id && x.IsActive && x.PhoneNumberId == phoneNumberId, ct);
                if (!match) continue;

                var resolved = new WabaTenantResolution
                {
                    TenantId = tenant.Id,
                    TenantSlug = tenant.Slug,
                    DataConnectionString = tenant.DataConnectionString
                };
                memoryCache.Set(memKey, resolved, CacheTtl);
                await distributedCache.SetStringAsync(memKey, $"{resolved.TenantId}|{resolved.TenantSlug}|{resolved.DataConnectionString}",
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
                return resolved;
            }
            catch
            {
                // Ignore single tenant DB error while resolving.
            }
        }

        return null;
    }

    public async Task InvalidateAsync(string phoneNumberId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumberId)) return;
        var memKey = $"waba:phone:{phoneNumberId}";
        memoryCache.Remove(memKey);
        await distributedCache.RemoveAsync(memKey, ct);
    }
}

