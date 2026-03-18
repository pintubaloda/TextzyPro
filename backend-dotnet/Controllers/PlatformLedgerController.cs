using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/ledger")]
public class PlatformLedgerController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Report(
        [FromQuery] string service = "",
        [FromQuery] string status = "",
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string q = "",
        [FromQuery] int take = 300,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        take = Math.Clamp(take, 25, 1000);
        var sourceTake = Math.Min(take * 3, 3000);
        var normalizedService = (service ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedQuery = (q ?? string.Empty).Trim().ToLowerInvariant();

        var tenants = await db.Tenants.AsNoTracking().ToListAsync(ct);
        var tenantMap = tenants.ToDictionary(x => x.Id, x => x);
        var tenantIds = tenantId.HasValue ? new[] { tenantId.Value } : tenantMap.Keys.ToArray();

        var profiles = tenantIds.Length == 0
            ? new Dictionary<Guid, TenantCompanyProfile>()
            : await db.TenantCompanyProfiles.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .ToDictionaryAsync(x => x.TenantId, ct);

        var invoiceRows = tenantIds.Length == 0
            ? new List<BillingInvoice>()
            : await db.BillingInvoices.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .OrderByDescending(x => x.PaidAtUtc ?? x.IssuedAtUtc)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Take(sourceTake)
                .ToListAsync(ct);

        var kycRows = tenantIds.Length == 0
            ? new List<KycSession>()
            : await db.KycSessions.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .OrderByDescending(x => x.CompletedAtUtc ?? x.UpdatedAtUtc)
                .Take(sourceTake)
                .ToListAsync(ct);

        var balanceRows = tenantIds.Length == 0
            ? new List<TenantUsageCreditBalance>()
            : await db.TenantUsageCreditBalances.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .ToListAsync(ct);

        var items = new List<PlatformLedgerItem>();

        items.AddRange(invoiceRows.Select(x =>
        {
            tenantMap.TryGetValue(x.TenantId, out var tenantRow);
            profiles.TryGetValue(x.TenantId, out var profileRow);
            return new PlatformLedgerItem
            {
                Id = x.Id,
                TenantId = x.TenantId,
                TenantName = tenantRow?.Name ?? "-",
                TenantSlug = tenantRow?.Slug ?? string.Empty,
                CompanyName = profileRow?.CompanyName ?? tenantRow?.Name ?? "-",
                OccurredAtUtc = x.PaidAtUtc ?? x.IssuedAtUtc,
                Service = "billing",
                ApiName = "Billing",
                EntryType = "purchase",
                Direction = "credit",
                Status = x.Status,
                ReferenceId = x.InvoiceNo,
                ExternalReference = x.ReferenceNo,
                CustomerRef = string.Empty,
                CreditsUsed = 0,
                Amount = x.Total,
                Currency = "INR",
                Description = string.IsNullOrWhiteSpace(x.Description) ? "Invoice purchase" : x.Description
            };
        }));

        items.AddRange(kycRows.Select(x =>
        {
            tenantMap.TryGetValue(x.TenantId, out var tenantRow);
            profiles.TryGetValue(x.TenantId, out var profileRow);
            return new PlatformLedgerItem
            {
                Id = x.Id,
                TenantId = x.TenantId,
                TenantName = tenantRow?.Name ?? "-",
                TenantSlug = tenantRow?.Slug ?? string.Empty,
                CompanyName = profileRow?.CompanyName ?? tenantRow?.Name ?? "-",
                OccurredAtUtc = x.CompletedAtUtc ?? x.UpdatedAtUtc,
                Service = "kyc",
                ApiName = string.Equals(x.ProviderCode, "gst", StringComparison.OrdinalIgnoreCase) ? "AppyFlow GST" : "DigiLocker KYC",
                EntryType = "verification",
                Direction = "debit",
                Status = x.Status,
                ReferenceId = x.Id.ToString(),
                ExternalReference = x.GstNumber,
                CustomerRef = x.CustomerRef,
                CreditsUsed = x.CreditsUsed,
                Amount = null,
                Currency = "INR",
                Description = string.Equals(x.ProviderCode, "gst", StringComparison.OrdinalIgnoreCase)
                    ? (string.IsNullOrWhiteSpace(x.GstNumber) ? "GST verification session" : $"GST verification for {x.GstNumber}")
                    : (string.IsNullOrWhiteSpace(x.CustomerRef) ? "DigiLocker verification session" : $"DigiLocker verification for {x.CustomerRef}")
            };
        }));

        var filtered = items
            .Where(x => string.IsNullOrWhiteSpace(normalizedService) || x.Service == normalizedService)
            .Where(x => string.IsNullOrWhiteSpace(normalizedStatus) || (x.Status ?? string.Empty).Contains(normalizedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedQuery) ||
                string.Join(" ", new[]
                {
                    x.TenantName,
                    x.TenantSlug,
                    x.CompanyName,
                    x.ApiName,
                    x.EntryType,
                    x.Status,
                    x.ReferenceId,
                    x.ExternalReference,
                    x.CustomerRef,
                    x.Description
                }).ToLowerInvariant().Contains(normalizedQuery))
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToList();

        return Ok(new
        {
            summary = new
            {
                totalEntries = filtered.Count,
                totalInvoiceValue = filtered.Where(x => x.Amount.HasValue).Sum(x => x.Amount ?? 0m),
                totalCreditsUsed = filtered.Sum(x => x.CreditsUsed),
                uniqueTenants = filtered.Select(x => x.TenantId).Distinct().Count(),
                balanceSnapshot = balanceRows
                    .GroupBy(x => x.MetricKey)
                    .Select(g => new
                    {
                        metricKey = g.Key,
                        unitsRemaining = g.Sum(x => x.UnitsRemaining),
                        tenants = g.Select(x => x.TenantId).Distinct().Count()
                    })
                    .OrderByDescending(x => x.unitsRemaining)
                    .ToList()
            },
            items = filtered
        });
    }

    [HttpGet("credit-ledger")]
    public async Task<IActionResult> CreditLedger(
        [FromQuery] string service = "",
        [FromQuery] string status = "",
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string q = "",
        [FromQuery] int take = 300,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        take = Math.Clamp(take, 25, 1000);
        var normalizedService = (service ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedQuery = (q ?? string.Empty).Trim().ToLowerInvariant();

        var tenants = await db.Tenants.AsNoTracking().ToListAsync(ct);
        var tenantMap = tenants.ToDictionary(x => x.Id, x => x);
        var tenantIds = tenantId.HasValue ? new[] { tenantId.Value } : tenantMap.Keys.ToArray();

        var profiles = tenantIds.Length == 0
            ? new Dictionary<Guid, TenantCompanyProfile>()
            : await db.TenantCompanyProfiles.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .ToDictionaryAsync(x => x.TenantId, ct);

        var rows = tenantIds.Length == 0
            ? new List<TenantCreditTransaction>()
            : await db.TenantCreditTransactions.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(Math.Min(take * 3, 3000))
                .ToListAsync(ct);

        var balanceRows = tenantIds.Length == 0
            ? new List<TenantUsageCreditBalance>()
            : await db.TenantUsageCreditBalances.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .ToListAsync(ct);

        var filtered = rows
            .Where(x => string.IsNullOrWhiteSpace(normalizedService) || (x.Service ?? string.Empty).Contains(normalizedService, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(normalizedStatus) || string.Equals((x.Status ?? string.Empty).Trim(), normalizedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedQuery) ||
                string.Join(" ", new[]
                {
                    tenantMap.TryGetValue(x.TenantId, out var tenantRow) ? tenantRow.Name : string.Empty,
                    tenantMap.TryGetValue(x.TenantId, out tenantRow) ? tenantRow.Slug : string.Empty,
                    profiles.TryGetValue(x.TenantId, out var profileRow) ? profileRow.CompanyName : string.Empty,
                    x.MetricKey,
                    x.TransactionType,
                    x.Source,
                    x.Service,
                    x.ReferenceId,
                    x.Status
                }).ToLowerInvariant().Contains(normalizedQuery))
            .Take(take)
            .ToList();

        return Ok(new
        {
            summary = new
            {
                totalEntries = filtered.Count,
                uniqueTenants = filtered.Select(x => x.TenantId).Distinct().Count(),
                debitUnits = filtered.Where(x => string.Equals(x.TransactionType, "debit", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Units),
                refundUnits = filtered.Where(x => string.Equals(x.TransactionType, "refund", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Units),
                creditUnits = filtered.Where(x => string.Equals(x.TransactionType, "credit", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Units),
                balanceSnapshot = balanceRows
                    .GroupBy(x => x.MetricKey)
                    .Select(g => new
                    {
                        metricKey = g.Key,
                        unitsRemaining = g.Sum(x => x.UnitsRemaining),
                        tenants = g.Select(x => x.TenantId).Distinct().Count()
                    })
                    .OrderByDescending(x => x.unitsRemaining)
                    .ToList()
            },
            items = filtered.Select(x =>
            {
                tenantMap.TryGetValue(x.TenantId, out var tenantRow);
                profiles.TryGetValue(x.TenantId, out var profileRow);
                return new
                {
                    id = x.Id,
                    tenantId = x.TenantId,
                    tenantName = tenantRow?.Name ?? "-",
                    tenantSlug = tenantRow?.Slug ?? string.Empty,
                    companyName = profileRow?.CompanyName ?? tenantRow?.Name ?? "-",
                    occurredAtUtc = x.CreatedAtUtc,
                    metricKey = x.MetricKey,
                    transactionType = x.TransactionType,
                    units = x.Units,
                    source = x.Source,
                    service = x.Service,
                    referenceId = x.ReferenceId,
                    status = x.Status
                };
            })
        });
    }

    private sealed class PlatformLedgerItem
    {
        public Guid Id { get; init; }
        public Guid TenantId { get; init; }
        public string TenantName { get; init; } = string.Empty;
        public string TenantSlug { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public DateTime OccurredAtUtc { get; init; }
        public string Service { get; init; } = string.Empty;
        public string ApiName { get; init; } = string.Empty;
        public string EntryType { get; init; } = string.Empty;
        public string Direction { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string ReferenceId { get; init; } = string.Empty;
        public string ExternalReference { get; init; } = string.Empty;
        public string CustomerRef { get; init; } = string.Empty;
        public int CreditsUsed { get; init; }
        public decimal? Amount { get; init; }
        public string Currency { get; init; } = "INR";
        public string Description { get; init; } = string.Empty;
    }
}
