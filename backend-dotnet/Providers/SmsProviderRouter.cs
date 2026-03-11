using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Providers;

public class SmsProviderRouter(
    ControlDbContext controlDb,
    IConfiguration config,
    SecretCryptoService crypto,
    TataSmsMessageProvider tata,
    EquenceSmsMessageProvider equence) : IMessageProvider
{
    public async Task<string> SendAsync(ChannelType channel, string recipient, string body, SmsSendContext? context = null, CancellationToken ct = default)
    {
        if (channel != ChannelType.Sms)
            throw new InvalidOperationException("SMS provider router only supports SMS sends.");

        var provider = await ResolveProviderAsync(context?.TenantId ?? Guid.Empty, ct);
        return provider switch
        {
            "equence" => await equence.SendAsync(channel, recipient, body, context, ct),
            _ => await tata.SendAsync(channel, recipient, body, context, ct),
        };
    }

    private async Task<string> ResolveProviderAsync(Guid tenantId, CancellationToken ct)
    {
        var defaultProvider = (config["Sms:Provider"] ?? "tata").Trim().ToLowerInvariant();

        var scopeSettings = await controlDb.PlatformSettings.AsNoTracking()
            .Where(x => x.Scope == "sms-gateway" && x.Key == "provider")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(scopeSettings?.ValueEncrypted))
        {
            try
            {
                var maybe = crypto.Decrypt(scopeSettings.ValueEncrypted).Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(maybe)) defaultProvider = maybe;
            }
            catch
            {
            }
        }

        if (tenantId == Guid.Empty) return NormalizeProvider(defaultProvider);

        var route = await controlDb.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => x.OwnerGroupId)
            .FirstOrDefaultAsync(ct);
        if (!route.HasValue || route.Value == Guid.Empty) return NormalizeProvider(defaultProvider);

        var ownerGroupProvider = await controlDb.TenantOwnerGroups.AsNoTracking()
            .Where(x => x.Id == route.Value && x.IsActive)
            .Select(x => x.SmsProviderRoute)
            .FirstOrDefaultAsync(ct);

        return NormalizeProvider(string.IsNullOrWhiteSpace(ownerGroupProvider) ? defaultProvider : ownerGroupProvider);
    }

    private static string NormalizeProvider(string? value)
    {
        var provider = (value ?? "tata").Trim().ToLowerInvariant();
        return provider == "equence" ? "equence" : "tata";
    }
}
