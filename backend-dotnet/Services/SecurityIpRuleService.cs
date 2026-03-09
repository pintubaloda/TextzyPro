using System.Net;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class SecurityIpRuleService(ControlDbContext db)
{
    public IpRuleDecision EvaluateSessionIp(string? ipAddress, Guid tenantId, Guid userId, bool enforceAllowlist = true)
    {
        var normalizedIp = NormalizeIp(ipAddress);
        var rules = db.SecurityIpRules
            .AsNoTracking()
            .Where(x => x.IsActive && x.Scope == "session" && (x.TenantId == null || x.TenantId == tenantId))
            .OrderByDescending(x => x.TenantId == tenantId)
            .ThenBy(x => x.RuleType)
            .ToList();

        var matchingBlock = rules.FirstOrDefault(x => string.Equals(x.RuleType, "block", StringComparison.OrdinalIgnoreCase) && MatchesRule(normalizedIp, x.IpRule));
        if (matchingBlock is not null)
        {
            return new IpRuleDecision(
                false,
                normalizedIp,
                matchingBlock.RuleType,
                matchingBlock.IpRule,
                matchingBlock.Id,
                matchingBlock.TenantId,
                "This IP address is blocked for platform login.");
        }

        var allowRules = rules
            .Where(x => string.Equals(x.RuleType, "allow", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!enforceAllowlist || allowRules.Count == 0)
            return new IpRuleDecision(true, normalizedIp, string.Empty, string.Empty, null, null, string.Empty);

        var matchingAllow = allowRules.FirstOrDefault(x => MatchesRule(normalizedIp, x.IpRule));
        if (matchingAllow is not null)
        {
            return new IpRuleDecision(
                true,
                normalizedIp,
                matchingAllow.RuleType,
                matchingAllow.IpRule,
                matchingAllow.Id,
                matchingAllow.TenantId,
                string.Empty);
        }

        return new IpRuleDecision(
            false,
            normalizedIp,
            "allow",
            string.Empty,
            null,
            null,
            "This IP address is not on the session allowlist.");
    }

    public async Task<List<SecurityIpRule>> ListAsync(string scope = "session", bool activeOnly = true, CancellationToken ct = default)
    {
        var query = db.SecurityIpRules.AsNoTracking().Where(x => x.Scope == scope);
        if (activeOnly)
            query = query.Where(x => x.IsActive);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public static bool MatchesRule(string? ipAddress, string rawRule)
    {
        var normalizedIp = NormalizeIp(ipAddress);
        if (string.IsNullOrWhiteSpace(normalizedIp) || string.IsNullOrWhiteSpace(rawRule))
            return false;

        if (!IPAddress.TryParse(normalizedIp, out var ip))
            return false;

        var normalizedRule = rawRule.Trim();
        if (string.Equals(normalizedRule, "*", StringComparison.Ordinal))
            return true;

        if (TryMatchCidr(ip, normalizedRule))
            return true;

        if (!IPAddress.TryParse(normalizedRule, out var allowed))
            return false;

        return NormalizeAddress(allowed).Equals(NormalizeAddress(ip));
    }

    public static string NormalizeIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (!IPAddress.TryParse(value.Trim(), out var ip))
            return value.Trim();
        return NormalizeAddress(ip).ToString();
    }

    private static IPAddress NormalizeAddress(IPAddress ip)
        => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    private static bool TryMatchCidr(IPAddress ip, string rule)
    {
        var slash = rule.IndexOf('/');
        if (slash <= 0 || slash == rule.Length - 1) return false;
        if (!IPAddress.TryParse(rule[..slash], out var network)) return false;
        if (!int.TryParse(rule[(slash + 1)..], out var prefixLength)) return false;

        var normalizedIp = NormalizeAddress(ip);
        var normalizedNetwork = NormalizeAddress(network);
        var ipBytes = normalizedIp.GetAddressBytes();
        var networkBytes = normalizedNetwork.GetAddressBytes();
        if (ipBytes.Length != networkBytes.Length) return false;

        var maxPrefix = ipBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > maxPrefix) return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != networkBytes[i]) return false;
        }

        if (remainingBits == 0) return true;

        var mask = (byte)~(255 >> remainingBits);
        return (ipBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}

public sealed record IpRuleDecision(
    bool IsAllowed,
    string IpAddress,
    string MatchedRuleType,
    string MatchedRule,
    Guid? MatchedRuleId,
    Guid? MatchedTenantId,
    string Message);
