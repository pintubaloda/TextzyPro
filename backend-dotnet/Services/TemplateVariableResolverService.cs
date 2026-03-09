using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class TemplateVariableResolverService(
    TenantDbContext tenantDb,
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    ContactPiiService pii)
{
    private static readonly Dictionary<int, string> SuggestedTokenByIndex = new()
    {
        [1] = "customer_name",
        [2] = "company_name",
        [3] = "agent_name",
        [4] = "customer_phone",
        [5] = "current_date"
    };

    public async Task<(Dictionary<string, string> TokenValues, Dictionary<int, string> SuggestedValues)> BuildAsync(string recipient, CancellationToken ct = default)
    {
        var to = (recipient ?? string.Empty).Trim();
        var now = DateTime.UtcNow;
        var tokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_phone"] = to,
            ["current_date"] = now.ToString("yyyy-MM-dd"),
            ["current_time"] = now.ToString("HH:mm"),
            ["current_datetime"] = now.ToString("yyyy-MM-dd HH:mm")
        };

        var tenant = await controlDb.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        if (tenant is not null)
        {
            tokenValues["project_name"] = tenant.Name ?? string.Empty;
            tokenValues["company_name"] = tenant.Name ?? string.Empty;
            tokenValues["tenant_slug"] = tenant.Slug ?? string.Empty;
        }

        var user = await controlDb.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == auth.UserId, ct);
        if (user is not null)
        {
            tokenValues["agent_name"] = string.IsNullOrWhiteSpace(user.FullName) ? user.Email : user.FullName;
            tokenValues["agent_email"] = user.Email ?? string.Empty;
        }
        else
        {
            tokenValues["agent_name"] = auth.Email ?? string.Empty;
            tokenValues["agent_email"] = auth.Email ?? string.Empty;
        }

        var waba = await tenantDb.TenantWabaConfigs.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
            .OrderByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (waba is not null)
        {
            if (!string.IsNullOrWhiteSpace(waba.BusinessAccountName))
                tokenValues["business_name"] = waba.BusinessAccountName;
            if (!string.IsNullOrWhiteSpace(waba.DisplayPhoneNumber))
                tokenValues["business_phone"] = waba.DisplayPhoneNumber;
            if (!string.IsNullOrWhiteSpace(waba.WabaId))
                tokenValues["waba_id"] = waba.WabaId;
        }

        Contact? contact = null;
        if (!string.IsNullOrWhiteSpace(to))
        {
            var phoneHash = pii.IsEnabled ? pii.ComputePhoneHash(to) : string.Empty;
            contact = pii.IsEnabled
                ? await tenantDb.Contacts.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.PhoneHash == phoneHash, ct)
                : await tenantDb.Contacts.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Phone == to, ct);
        }

        if (contact is not null)
        {
            var name = pii.RevealName(contact);
            var email = pii.RevealEmail(contact);
            var phone = pii.RevealPhone(contact);
            if (!string.IsNullOrWhiteSpace(name)) tokenValues["customer_name"] = name;
            if (!string.IsNullOrWhiteSpace(email)) tokenValues["customer_email"] = email;
            if (!string.IsNullOrWhiteSpace(phone)) tokenValues["customer_phone"] = phone;
        }
        else if (!string.IsNullOrWhiteSpace(to))
        {
            tokenValues["customer_name"] = to;
        }

        var conversation = !string.IsNullOrWhiteSpace(to)
            ? await tenantDb.Conversations.AsNoTracking()
                .Where(x => x.TenantId == tenancy.TenantId && x.CustomerPhone == to)
                .OrderByDescending(x => x.LastMessageAtUtc)
                .FirstOrDefaultAsync(ct)
            : null;
        if (conversation is not null)
        {
            if (!string.IsNullOrWhiteSpace(conversation.CustomerName))
                tokenValues["customer_name"] = conversation.CustomerName;
            if (!string.IsNullOrWhiteSpace(conversation.AssignedUserName))
                tokenValues["assigned_agent"] = conversation.AssignedUserName;
        }

        var suggestedValues = new Dictionary<int, string>();
        foreach (var pair in SuggestedTokenByIndex)
        {
            if (tokenValues.TryGetValue(pair.Value, out var value) && !string.IsNullOrWhiteSpace(value))
                suggestedValues[pair.Key] = value;
        }

        return (tokenValues, suggestedValues);
    }

    public static bool TryResolveSystemToken(string input, IReadOnlyDictionary<string, string> tokenValues, out string resolved)
    {
        resolved = string.Empty;
        var raw = (input ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string token;
        if (raw.StartsWith("{{", StringComparison.Ordinal) && raw.EndsWith("}}", StringComparison.Ordinal))
            token = raw[2..^2].Trim().ToLowerInvariant();
        else if (raw.StartsWith("sys:", StringComparison.OrdinalIgnoreCase))
            token = raw[4..].Trim().ToLowerInvariant();
        else if (raw.StartsWith("$", StringComparison.Ordinal))
            token = raw[1..].Trim().ToLowerInvariant();
        else
            return false;

        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!tokenValues.TryGetValue(token, out var value) || string.IsNullOrWhiteSpace(value))
            return false;

        resolved = value.Trim();
        return true;
    }

    public static string GetSuggestedTokenNameByIndex(int index) =>
        SuggestedTokenByIndex.TryGetValue(index, out var token) ? token : string.Empty;
}

