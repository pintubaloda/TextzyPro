using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/purchases")]
public class PlatformPurchasesController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    AuditLogService audit,
    IEmailService emailService,
    InvoiceAttachmentService invoiceAttachmentService,
    SecretCryptoService crypto,
    IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Report(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] string service = "",
        [FromQuery] string q = "",
        [FromQuery] string status = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var normalizedService = (service ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedQuery = (q ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant();
        var requestedPage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 10, 200);

        var baseQuery =
            from invoice in db.BillingInvoices.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on invoice.TenantId equals tenant.Id
            from profile in db.TenantCompanyProfiles.AsNoTracking().Where(x => x.TenantId == invoice.TenantId).DefaultIfEmpty()
            select new PurchaseInvoiceBaseRow
            {
                InvoiceId = invoice.Id,
                TenantId = invoice.TenantId,
                InvoiceNo = invoice.InvoiceNo,
                InvoiceKind = invoice.InvoiceKind,
                InvoiceStatus = invoice.Status,
                UserName = profile != null && profile.BillingEmail != string.Empty
                    ? profile.BillingEmail
                    : "-",
                UserEmail = profile != null
                    ? profile.BillingEmail
                    : string.Empty,
                CompanyName = profile != null && profile.CompanyName != string.Empty
                    ? profile.CompanyName
                    : tenant.Name,
                TenantName = tenant.Name,
                GstNo = profile != null ? profile.Gstin : string.Empty,
                PurchaseDateUtc = invoice.PaidAtUtc ?? invoice.IssuedAtUtc,
                InvoiceDateUtc = invoice.IssuedAtUtc,
                ServiceName = invoice.Description != string.Empty
                    ? invoice.Description
                    : invoice.BillingCycle == "yearly"
                        ? "Yearly Subscription"
                        : invoice.BillingCycle == "monthly"
                            ? "Monthly Subscription"
                            : invoice.BillingCycle == "lifetime"
                                ? "Lifetime Subscription"
                                : invoice.BillingCycle == "usage_based"
                                    ? "Usage Pack"
                                    : "Platform Service",
                BillingCycle = invoice.BillingCycle,
                Amount = invoice.Subtotal,
                GstAmount = invoice.TaxAmount,
                TotalAmount = invoice.Total,
                ReferenceNo = invoice.ReferenceNo,
                BillingEmail = profile != null ? profile.BillingEmail : string.Empty,
                PaidAtUtc = invoice.PaidAtUtc,
                CreatedAtUtc = invoice.CreatedAtUtc
            };

        if (fromUtc.HasValue)
        {
            var start = DateTime.SpecifyKind(fromUtc.Value, DateTimeKind.Utc);
            baseQuery = baseQuery.Where(x => x.PurchaseDateUtc >= start);
        }

        if (toUtc.HasValue)
        {
            var end = DateTime.SpecifyKind(toUtc.Value, DateTimeKind.Utc);
            baseQuery = baseQuery.Where(x => x.PurchaseDateUtc <= end);
        }

        if (!string.IsNullOrWhiteSpace(normalizedStatus))
        {
            baseQuery = baseQuery.Where(x => (x.InvoiceStatus ?? string.Empty).ToLower() == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var ownerTenantIds = await (
                from membership in db.TenantUsers.AsNoTracking()
                join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
                where (user.FullName ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                      (user.Email ?? string.Empty).ToLower().Contains(normalizedQuery)
                select membership.TenantId)
                .Distinct()
                .ToListAsync(ct);

            baseQuery = baseQuery.Where(x =>
                (x.InvoiceNo ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.UserName ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.UserEmail ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.CompanyName ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.TenantName ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.GstNo ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.ServiceName ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                (x.ReferenceNo ?? string.Empty).ToLower().Contains(normalizedQuery) ||
                ownerTenantIds.Contains(x.TenantId));
        }

        if (!string.IsNullOrWhiteSpace(normalizedService))
        {
            baseQuery = baseQuery.Where(x => (x.ServiceName ?? string.Empty).ToLower() == normalizedService);
        }

        var serviceOptions = await baseQuery
            .Where(x => x.ServiceName != string.Empty)
            .Select(x => new { code = x.ServiceName, name = x.ServiceName })
            .Distinct()
            .OrderBy(x => x.name)
            .ToListAsync(ct);

        var totalPurchases = await baseQuery.CountAsync(ct);
        var totalAmount = await baseQuery.Select(x => (decimal?)x.Amount).SumAsync(ct) ?? 0m;
        var totalGst = await baseQuery.Select(x => (decimal?)x.GstAmount).SumAsync(ct) ?? 0m;
        var totalInvoiceValue = await baseQuery.Select(x => (decimal?)x.TotalAmount).SumAsync(ct) ?? 0m;
        var uniqueCustomers = await baseQuery.Select(x => x.TenantId).Distinct().CountAsync(ct);
        var services = await baseQuery.Select(x => x.ServiceName).Distinct().CountAsync(ct);

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalPurchases / (double)safePageSize));
        var safePage = Math.Min(requestedPage, totalPages);
        var skip = (safePage - 1) * safePageSize;

        var pageRows = await baseQuery
            .OrderByDescending(x => x.PurchaseDateUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(safePageSize)
            .ToListAsync(ct);

        var tenantIds = pageRows.Select(x => x.TenantId).Distinct().ToList();
        var ownerMembershipRows = tenantIds.Count == 0
            ? new List<OwnerSql>()
            : await db.TenantUsers.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .Select(x => new
                {
                    x.TenantId,
                    x.UserId,
                    x.CreatedAtUtc,
                    RoleRank =
                        (x.Role ?? string.Empty).ToLower() == "owner" ? 0 :
                        (x.Role ?? string.Empty).ToLower() == "admin" ? 1 :
                        (x.Role ?? string.Empty).ToLower() == "manager" ? 2 :
                        (x.Role ?? string.Empty).ToLower() == "support" ? 3 :
                        (x.Role ?? string.Empty).ToLower() == "marketing" ? 4 :
                        (x.Role ?? string.Empty).ToLower() == "finance" ? 5 : 99
                })
                .OrderBy(x => x.RoleRank)
                .ThenBy(x => x.CreatedAtUtc)
                .GroupBy(x => x.TenantId)
                .Select(g => new OwnerSql
                {
                    TenantId = g.Key,
                    UserId = g.Select(x => x.UserId).First()
                })
                .ToListAsync(ct);
        var ownerUserIds = ownerMembershipRows.Select(x => x.UserId).Distinct().ToList();
        var ownerUsers = ownerUserIds.Count == 0
            ? new Dictionary<Guid, Models.User>()
            : await db.Users.AsNoTracking()
                .Where(x => ownerUserIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
        var ownerByTenant = ownerMembershipRows.ToDictionary(
            x => x.TenantId,
            x => ownerUsers.TryGetValue(x.UserId, out var user) ? user : null);
        var references = pageRows
            .Select(x => x.ReferenceNo)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var attemptRows = tenantIds.Count == 0 || references.Count == 0
            ? new List<Models.BillingPaymentAttempt>()
            : await db.BillingPaymentAttempts.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId) && references.Contains(x.OrderId))
                .OrderByDescending(x => x.PaidAtUtc ?? x.UpdatedAtUtc)
                .ToListAsync(ct);
        var latestAttempts = attemptRows
            .GroupBy(x => new { x.TenantId, Key = x.OrderId ?? string.Empty })
            .ToDictionary(
                g => $"{g.Key.TenantId:D}|{g.Key.Key}",
                g => g.First(),
                StringComparer.OrdinalIgnoreCase);

        var manualSubscriptionIds = pageRows
            .Select(x => TryParseManualSubscriptionId(x.ReferenceNo))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var subscriptionRows = tenantIds.Count == 0
            ? new List<Models.TenantSubscription>()
            : await db.TenantSubscriptions.AsNoTracking()
                .Where(x => tenantIds.Contains(x.TenantId))
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync(ct);
        var subscriptionsByTenant = subscriptionRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var subscriptionsById = manualSubscriptionIds.Count == 0
            ? new Dictionary<Guid, Models.TenantSubscription>()
            : subscriptionRows
                .Where(x => manualSubscriptionIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x);

        var planIds = attemptRows
            .Where(x => x.PlanId != Guid.Empty)
            .Select(x => x.PlanId)
            .Concat(subscriptionRows.Where(x => x.PlanId != Guid.Empty).Select(x => x.PlanId))
            .Distinct()
            .ToList();
        var plans = planIds.Count == 0
            ? new Dictionary<Guid, Models.BillingPlan>()
            : await db.BillingPlans.AsNoTracking()
                .Where(x => planIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var rows = pageRows.Select((row, idx) =>
        {
            latestAttempts.TryGetValue($"{row.TenantId:D}|{row.ReferenceNo}", out var attempt);

            Models.TenantSubscription? subscription = null;
            var manualSubscriptionId = TryParseManualSubscriptionId(row.ReferenceNo);
            if (manualSubscriptionId.HasValue)
            {
                subscriptionsById.TryGetValue(manualSubscriptionId.Value, out subscription);
            }
            else if (subscriptionsByTenant.TryGetValue(row.TenantId, out var tenantSubscriptions))
            {
                subscription = tenantSubscriptions.FirstOrDefault(x => x.CreatedAtUtc <= row.PurchaseDateUtc);
            }

            Models.BillingPlan? plan = null;
            if (attempt is not null && attempt.PlanId != Guid.Empty)
                plans.TryGetValue(attempt.PlanId, out plan);
            else if (subscription is not null && subscription.PlanId != Guid.Empty)
                plans.TryGetValue(subscription.PlanId, out plan);

            var serviceName = string.IsNullOrWhiteSpace(row.ServiceName)
                ? ResolveServiceName(plan?.Name, row.BillingCycle)
                : row.ServiceName;
            ownerByTenant.TryGetValue(row.TenantId, out var owner);
            var userName = owner is not null && !string.IsNullOrWhiteSpace(owner.FullName)
                ? owner.FullName
                : owner is not null && !string.IsNullOrWhiteSpace(owner.Email)
                    ? owner.Email
                    : string.IsNullOrWhiteSpace(row.UserName)
                        ? "-"
                        : row.UserName;
            var userEmail = owner?.Email ?? row.UserEmail;

            return new
            {
                sNo = skip + idx + 1,
                row.InvoiceId,
                row.TenantId,
                row.InvoiceNo,
                row.InvoiceKind,
                invoiceStatus = row.InvoiceStatus,
                UserName = userName,
                UserEmail = userEmail,
                row.CompanyName,
                gstNo = row.GstNo,
                row.PurchaseDateUtc,
                row.InvoiceDateUtc,
                ServiceName = serviceName,
                ServiceCode = plan?.Code ?? string.Empty,
                row.BillingCycle,
                row.Amount,
                gstAmount = row.GstAmount,
                row.TotalAmount,
                Currency = string.IsNullOrWhiteSpace(attempt?.Currency) ? "INR" : attempt!.Currency.Trim().ToUpperInvariant(),
                row.ReferenceNo,
                row.BillingEmail,
                row.PaidAtUtc,
                row.CreatedAtUtc
            };
        }).ToList();

        return Ok(new
        {
            summary = new
            {
                totalPurchases,
                totalAmount,
                totalGst,
                totalInvoiceValue,
                uniqueCustomers,
                services
            },
            page = safePage,
            pageSize = safePageSize,
            totalCount = totalPurchases,
            totalPages,
            hasPreviousPage = safePage > 1,
            hasNextPage = safePage < totalPages,
            serviceOptions,
            items = rows
        });
    }

    [HttpGet("{invoiceId:guid}/view")]
    public async Task<IActionResult> ViewInvoice(Guid invoiceId, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (invoice is null) return NotFound("Invoice not found.");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoice.TenantId, ct);
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == invoice.TenantId, ct);
        var companyName = string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Textzy Workspace") : profile!.CompanyName;
        var legalName = string.IsNullOrWhiteSpace(profile?.LegalName) ? companyName : profile!.LegalName;
        var branding = await GetPlatformBrandingAsync(ct);
        var integrityHash = await EnsureInvoiceIntegrityHashAsync(invoice, ct);
        var verificationUrl = BuildPublicInvoiceVerificationUrl(invoice.Id, integrityHash, branding.Website);
        var qrCodeUrl = BuildQrCodeUrl(verificationUrl);
        var html = InvoiceDocumentRenderer.BuildInvoiceHtml(
            invoice,
            new InvoiceSellerProfile
            {
                PlatformName = branding.PlatformName,
                LogoUrl = branding.LogoUrl,
                LegalName = branding.LegalName,
                Address = branding.Address,
                Gstin = branding.Gstin,
                Pan = branding.Pan,
                Cin = branding.Cin,
                BillingEmail = branding.BillingEmail,
                BillingPhone = branding.BillingPhone,
                Website = branding.Website,
                InvoiceFooter = branding.InvoiceFooter
            },
            new InvoiceBuyerProfile
            {
                CompanyName = companyName,
                LegalName = legalName,
                BillingEmail = profile?.BillingEmail ?? string.Empty,
                Address = profile?.Address ?? string.Empty,
                Gstin = profile?.Gstin ?? string.Empty,
                Pan = profile?.Pan ?? string.Empty
            },
            profile?.TaxRatePercent ?? 18m,
            profile?.IsTaxExempt ?? false,
            profile?.IsReverseCharge ?? false,
            verificationUrl,
            qrCodeUrl);

        return File(
            Encoding.UTF8.GetBytes(html),
            "text/html; charset=utf-8",
            $"{(string.IsNullOrWhiteSpace(invoice.InvoiceNo) ? invoice.Id.ToString("N") : invoice.InvoiceNo)}.html");
    }

    [HttpPost("{invoiceId:guid}/send")]
    public async Task<IActionResult> SendInvoice(Guid invoiceId, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (invoice is null) return NotFound("Invoice not found.");

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoice.TenantId, ct);
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == invoice.TenantId, ct);
        var recipient = await ResolveBillingRecipientAsync(invoice.TenantId, ct);
        if (string.IsNullOrWhiteSpace(recipient.email))
            return BadRequest("Billing recipient email is not configured.");

        var attempt = await db.BillingPaymentAttempts.AsNoTracking()
            .Where(x => x.TenantId == invoice.TenantId && x.OrderId == invoice.ReferenceNo)
            .OrderByDescending(x => x.PaidAtUtc ?? x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        var plan = attempt is null
            ? null
            : await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attempt.PlanId, ct);
        var serviceName = string.IsNullOrWhiteSpace(invoice.Description)
            ? ResolveServiceName(plan?.Name, invoice.BillingCycle)
            : invoice.Description.Trim();
        var currency = string.IsNullOrWhiteSpace(attempt?.Currency) ? "INR" : attempt!.Currency.Trim().ToUpperInvariant();
        var companyName = string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Textzy Workspace") : profile!.CompanyName;
        var attachments = new[] { await invoiceAttachmentService.BuildPdfAttachmentAsync(invoice, Request, ct) };

        await emailService.SendBillingEventAsync(
            recipient.email,
            recipient.name,
            companyName,
            $"Invoice {invoice.InvoiceNo}",
            "Your billing invoice has been resent for review.",
            new Dictionary<string, string>
            {
                ["Invoice No"] = invoice.InvoiceNo,
                ["Service"] = serviceName,
                ["Invoice Date"] = invoice.IssuedAtUtc.ToString("yyyy-MM-dd"),
                ["Amount"] = FormatCurrency(invoice.Subtotal, currency),
                ["GST"] = FormatCurrency(invoice.TaxAmount, currency),
                ["Total"] = FormatCurrency(invoice.Total, currency),
                ["Status"] = invoice.Status
            },
            ct,
            attachments);

        await audit.WriteAsync("platform.purchase.invoice.resent", $"invoice={invoiceId}; tenant={invoice.TenantId}; email={recipient.email}", ct);
        return Ok(new { sent = true, invoiceId });
    }

    private async Task<(string email, string name)> ResolveBillingRecipientAsync(Guid tenantId, CancellationToken ct)
    {
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (!string.IsNullOrWhiteSpace(profile?.BillingEmail))
            return (profile.BillingEmail.Trim(), string.IsNullOrWhiteSpace(profile.CompanyName) ? profile.BillingEmail.Trim() : profile.CompanyName);

        var ownerMembership = await db.TenantUsers.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x =>
                (x.Role ?? string.Empty).ToLower() == "owner" ? 0 :
                (x.Role ?? string.Empty).ToLower() == "admin" ? 1 :
                (x.Role ?? string.Empty).ToLower() == "manager" ? 2 :
                (x.Role ?? string.Empty).ToLower() == "support" ? 3 :
                (x.Role ?? string.Empty).ToLower() == "marketing" ? 4 :
                (x.Role ?? string.Empty).ToLower() == "finance" ? 5 : 99)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerMembership.HasValue)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ownerMembership.Value, ct);
            if (!string.IsNullOrWhiteSpace(owner?.Email))
                return (owner.Email.Trim(), string.IsNullOrWhiteSpace(owner.FullName) ? owner.Email.Trim() : owner.FullName);
        }

        if (!string.IsNullOrWhiteSpace(auth.Email))
            return (auth.Email.Trim(), string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email.Trim() : auth.FullName);

        return (string.Empty, string.Empty);
    }

    private async Task<string> EnsureInvoiceIntegrityHashAsync(Models.BillingInvoice invoice, CancellationToken ct)
    {
        var expected = InvoiceIntegrityHasher.Compute(invoice);
        var legacyExpected = InvoiceIntegrityHasher.ComputeLegacy(invoice);
        if (string.Equals(invoice.IntegrityHash, expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invoice.IntegrityHash, legacyExpected, StringComparison.OrdinalIgnoreCase))
        {
            return invoice.IntegrityHash;
        }

        invoice.IntegrityAlgo = "SHA256";
        invoice.IntegrityHash = expected;
        await db.SaveChangesAsync(ct);
        return expected;
    }

    private static string ResolveServiceName(string? planName, string? billingCycle)
    {
        if (!string.IsNullOrWhiteSpace(planName)) return planName.Trim();
        var cycle = (billingCycle ?? string.Empty).Trim().ToLowerInvariant();
        return cycle switch
        {
            "yearly" => "Yearly Subscription",
            "monthly" => "Monthly Subscription",
            "lifetime" => "Lifetime Subscription",
            "usage_based" => "Usage Pack",
            _ => "Platform Service"
        };
    }

    private static Guid? TryParseManualSubscriptionId(string? referenceNo)
    {
        var value = (referenceNo ?? string.Empty).Trim();
        const string prefix = "manual-plan:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return Guid.TryParse(value[prefix.Length..], out var id) ? id : null;
    }

    private static string FormatCurrency(decimal amount, string currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();
        return $"{(code == "INR" ? "INR " : code + " ")}{amount:0.00}";
    }

    private async Task<PlatformBrandingSnapshot> GetPlatformBrandingAsync(CancellationToken ct)
    {
        var rows = await db.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "platform-branding")
            .ToListAsync(ct);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
            values[row.Key] = crypto.Decrypt(row.ValueEncrypted);

        var platformName = (values.TryGetValue("platformName", out var pn) ? pn : "Textzy").Trim();
        var legalName = (values.TryGetValue("legalName", out var ln) ? ln : platformName).Trim();
        return new PlatformBrandingSnapshot
        {
            PlatformName = string.IsNullOrWhiteSpace(platformName) ? "Textzy" : platformName,
            LogoUrl = ResolveLogoUrl(values.TryGetValue("logoUrl", out var logoUrl) ? logoUrl : string.Empty),
            LegalName = string.IsNullOrWhiteSpace(legalName) ? (string.IsNullOrWhiteSpace(platformName) ? "Textzy" : platformName) : legalName,
            Gstin = (values.TryGetValue("gstin", out var gst) ? gst : string.Empty).Trim(),
            Pan = (values.TryGetValue("pan", out var sellerPan) ? sellerPan : string.Empty).Trim(),
            Cin = (values.TryGetValue("cin", out var cin) ? cin : string.Empty).Trim(),
            BillingEmail = (values.TryGetValue("billingEmail", out var email) ? email : string.Empty).Trim(),
            BillingPhone = (values.TryGetValue("billingPhone", out var phone) ? phone : string.Empty).Trim(),
            Website = (values.TryGetValue("website", out var website) ? website : string.Empty).Trim(),
            Address = (values.TryGetValue("address", out var address) ? address : string.Empty).Trim(),
            InvoiceFooter = (values.TryGetValue("invoiceFooter", out var footer) ? footer : string.Empty).Trim()
        };
    }

    private string BuildPublicInvoiceVerificationUrl(Guid invoiceId, string integrityHash, string? brandingWebsite)
    {
        var baseUrl = NormalizeBaseUrl(config["PUBLIC_API_BASE_URL"])
            ?? NormalizeBaseUrl(config["API_BASE_URL"])
            ?? $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl}/api/public/invoices/verify?invoiceId={Uri.EscapeDataString(invoiceId.ToString())}&hash={Uri.EscapeDataString(integrityHash ?? string.Empty)}";
    }

    private static string BuildQrCodeUrl(string verificationUrl)
        => $"https://api.qrserver.com/v1/create-qr-code/?size=180x180&ecc=M&data={Uri.EscapeDataString(verificationUrl ?? string.Empty)}";

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string ResolveLogoUrl(string? configuredUrl)
    {
        var explicitUrl = (configuredUrl ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(explicitUrl))
            return explicitUrl;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "textzy-landing-logo.svg"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "textzy-landing-logo.svg"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "textzy-logo-full.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "textzy-logo-full.png")
        };

        foreach (var path in candidates)
        {
            if (!System.IO.File.Exists(path)) continue;
            var mime = path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png";
            return $"data:{mime};base64,{Convert.ToBase64String(System.IO.File.ReadAllBytes(path))}";
        }

        return string.Empty;
    }

    private static string BuildInvoiceHtml(
        Models.BillingInvoice invoice,
        string companyName,
        string legalName,
        string billingEmail,
        string billingAddress,
        string gstin,
        string pan,
        PlatformBrandingSnapshot branding,
        decimal taxRatePercent,
        bool isTaxExempt,
        bool isReverseCharge)
    {
        var safeInvoiceNo = WebUtility.HtmlEncode(invoice.InvoiceNo);
        var safePlatformName = WebUtility.HtmlEncode(branding.PlatformName);
        var safeSellerLegalName = WebUtility.HtmlEncode(branding.LegalName);
        var safeSellerGstin = WebUtility.HtmlEncode(branding.Gstin);
        var safeSellerPan = WebUtility.HtmlEncode(branding.Pan);
        var safeSellerEmail = WebUtility.HtmlEncode(branding.BillingEmail);
        var safeSellerPhone = WebUtility.HtmlEncode(branding.BillingPhone);
        var safeSellerWebsite = WebUtility.HtmlEncode(branding.Website);
        var safeSellerAddress = WebUtility.HtmlEncode(branding.Address);
        var safeFooter = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(branding.InvoiceFooter) ? "This is a system-generated GST invoice." : branding.InvoiceFooter);
        var safeCompany = WebUtility.HtmlEncode(companyName);
        var safeLegalName = WebUtility.HtmlEncode(legalName);
        var safeEmail = WebUtility.HtmlEncode(billingEmail);
        var safeAddress = WebUtility.HtmlEncode(billingAddress);
        var safeGstin = WebUtility.HtmlEncode(gstin);
        var safePan = WebUtility.HtmlEncode(pan);
        var safeReference = WebUtility.HtmlEncode(invoice.ReferenceNo);
        var safeDescription = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(invoice.Description) ? "Platform service purchase" : invoice.Description);
        var invoiceLabel = string.Equals(invoice.InvoiceKind, "proforma_invoice", StringComparison.OrdinalIgnoreCase) ? "Proforma Invoice" : "Tax Invoice";
        var safeInvoiceLabel = WebUtility.HtmlEncode(invoiceLabel);
        var supplyLabel = isReverseCharge
            ? "Reverse charge"
            : isTaxExempt
                ? "GST exempt"
                : $"{Math.Clamp(taxRatePercent, 0m, 100m):0.##}% GST";
        var taxLineLabel = isReverseCharge
            ? "GST payable under reverse charge"
            : isTaxExempt
                ? "GST"
                : $"GST @ {Math.Clamp(taxRatePercent, 0m, 100m):0.##}%";
        var safeSupplyLabel = WebUtility.HtmlEncode(supplyLabel);
        var safeTaxLineLabel = WebUtility.HtmlEncode(taxLineLabel);
        var safeBillingCycle = WebUtility.HtmlEncode((invoice.BillingCycle ?? string.Empty).Replace("_", " "));
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>{{safeInvoiceNo}}</title>
              <style>
                body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; background: #f8fafc; color: #0f172a; }
                .page { max-width: 1040px; margin: 24px auto; background: #ffffff; border: 1px solid #e2e8f0; box-shadow: 0 10px 30px rgba(15, 23, 42, 0.06); }
                .band { height: 10px; background: linear-gradient(90deg, #ea580c 0%, #fb923c 100%); }
                .wrap { padding: 28px 32px; }
                .header { display:flex; justify-content:space-between; gap:24px; align-items:flex-start; }
                .brand { font-size:28px; font-weight:800; color:#c2410c; letter-spacing:0.02em; }
                .label { display:inline-flex; padding:8px 14px; border-radius:999px; background:#fff7ed; color:#c2410c; font-size:12px; font-weight:700; text-transform:uppercase; letter-spacing:0.08em; }
                .muted { color:#64748b; font-size:13px; line-height:1.55; }
                .title { margin-top:10px; font-size:14px; font-weight:700; color:#0f172a; text-transform:uppercase; letter-spacing:0.08em; }
                .meta { margin-top:14px; display:grid; grid-template-columns:repeat(2, minmax(0, 1fr)); gap:10px 24px; font-size:14px; }
                .meta .key { color:#64748b; }
                .section { margin-top:24px; }
                .grid { display:grid; grid-template-columns:1fr 1fr; gap:18px; }
                .panel { border:1px solid #e2e8f0; background:#f8fafc; border-radius:18px; padding:18px; min-height:170px; }
                .panel h3 { margin:0 0 10px; font-size:13px; text-transform:uppercase; letter-spacing:0.08em; color:#475569; }
                .panel strong { display:block; font-size:18px; margin-bottom:4px; color:#0f172a; }
                table { width:100%; border-collapse:collapse; margin-top:18px; }
                th, td { border:1px solid #cbd5e1; padding:12px 14px; text-align:left; vertical-align:top; }
                th { background:#eff6ff; font-size:12px; text-transform:uppercase; letter-spacing:0.08em; color:#334155; }
                .right { text-align:right; }
                .summary td { background:#fff7ed; font-weight:600; }
                .total td { background:#0f172a; color:#ffffff; font-size:16px; font-weight:700; }
                .foot { margin-top:18px; display:flex; justify-content:space-between; gap:24px; font-size:12px; color:#64748b; }
              </style>
            </head>
            <body>
              <div class="page">
                <div class="band"></div>
                <div class="wrap">
                  <div class="header">
                    <div>
                      <div class="brand">{{safePlatformName}}</div>
                      <div class="title">{{safeInvoiceLabel}}</div>
                      <div class="muted">{{safeSellerLegalName}}</div>
                    </div>
                    <div class="right">
                      <div class="label">{{safeInvoiceLabel}}</div>
                      <div class="meta" style="margin-top:16px;">
                        <div><span class="key">Invoice No</span><br /><strong style="display:inline; font-size:16px;">{{safeInvoiceNo}}</strong></div>
                        <div><span class="key">Invoice Date</span><br />{{invoice.IssuedAtUtc:yyyy-MM-dd}}</div>
                        <div><span class="key">Status</span><br />{{invoice.Status}}</div>
                        <div><span class="key">Paid Date</span><br />{{(invoice.PaidAtUtc ?? invoice.IssuedAtUtc):yyyy-MM-dd}}</div>
                        <div><span class="key">Reference</span><br />{{(string.IsNullOrWhiteSpace(invoice.ReferenceNo) ? "-" : safeReference)}}</div>
                        <div><span class="key">Supply Type</span><br />{{safeSupplyLabel}}</div>
                      </div>
                    </div>
                  </div>
                  <div class="section grid">
                    <div class="panel">
                      <h3>Supplier</h3>
                      <strong>{{safeSellerLegalName}}</strong>
                      <div class="muted">{{safeSellerAddress}}</div>
                      <div class="muted">GSTIN: {{(string.IsNullOrWhiteSpace(branding.Gstin) ? "-" : safeSellerGstin)}}</div>
                      <div class="muted">PAN: {{(string.IsNullOrWhiteSpace(branding.Pan) ? "-" : safeSellerPan)}}</div>
                      <div class="muted">Email: {{(string.IsNullOrWhiteSpace(branding.BillingEmail) ? "-" : safeSellerEmail)}}</div>
                      <div class="muted">Phone: {{(string.IsNullOrWhiteSpace(branding.BillingPhone) ? "-" : safeSellerPhone)}}</div>
                      <div class="muted">Website: {{(string.IsNullOrWhiteSpace(branding.Website) ? "-" : safeSellerWebsite)}}</div>
                    </div>
                    <div class="panel">
                      <h3>Bill To</h3>
                      <strong>{{safeCompany}}</strong>
                      <div class="muted">{{safeLegalName}}</div>
                      <div class="muted">{{safeAddress}}</div>
                      <div class="muted">GSTIN: {{(string.IsNullOrWhiteSpace(gstin) ? "-" : safeGstin)}}</div>
                      <div class="muted">PAN: {{(string.IsNullOrWhiteSpace(pan) ? "-" : safePan)}}</div>
                      <div class="muted">Billing Email: {{(string.IsNullOrWhiteSpace(billingEmail) ? "-" : safeEmail)}}</div>
                      <div class="muted">Service Period: {{invoice.PeriodStartUtc:yyyy-MM-dd}} to {{invoice.PeriodEndUtc:yyyy-MM-dd}}</div>
                      <div class="muted">Billing Cycle: {{safeBillingCycle}}</div>
                    </div>
                  </div>
                  <div class="section">
                    <table>
                      <thead>
                        <tr>
                          <th style="width:72px;">S. No.</th>
                          <th>Description of Service</th>
                          <th style="width:140px;">HSN/SAC</th>
                          <th style="width:160px;" class="right">Taxable Value</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr>
                          <td>1</td>
                          <td>
                            <strong style="display:block; font-size:15px; margin:0; color:#0f172a;">{{safeDescription}}</strong>
                            <span class="muted">Reference: {{(string.IsNullOrWhiteSpace(invoice.ReferenceNo) ? "-" : safeReference)}} | Cycle: {{safeBillingCycle}}</span>
                          </td>
                          <td>998314</td>
                          <td class="right">{{invoice.Subtotal:0.00}}</td>
                        </tr>
                        <tr class="summary">
                          <td colspan="3">{{safeTaxLineLabel}}</td>
                          <td class="right">{{invoice.TaxAmount:0.00}}</td>
                        </tr>
                        <tr class="total">
                          <td colspan="3">Invoice Total</td>
                          <td class="right">{{invoice.Total:0.00}}</td>
                        </tr>
                      </tbody>
                    </table>
                  </div>
                  <div class="foot">
                    <div>
                      <div><strong>Notes</strong></div>
                      <div>{{safeFooter}}</div>
                    </div>
                    <div class="right">
                      <div><strong>System generated document</strong></div>
                      <div>Integrity: {{invoice.IntegrityAlgo}} / {{invoice.IntegrityHash}}</div>
                    </div>
                  </div>
                </div>
              </div>
            </body>
            </html>
            """;
    }

    private sealed class PlatformBrandingSnapshot
    {
        public string PlatformName { get; init; } = "Textzy";
        public string LogoUrl { get; init; } = string.Empty;
        public string LegalName { get; init; } = "Textzy";
        public string Gstin { get; init; } = string.Empty;
        public string Pan { get; init; } = string.Empty;
        public string Cin { get; init; } = string.Empty;
        public string BillingEmail { get; init; } = string.Empty;
        public string BillingPhone { get; init; } = string.Empty;
        public string Website { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string InvoiceFooter { get; init; } = string.Empty;
    }

    private sealed class OwnerSql
    {
        public Guid TenantId { get; init; }
        public Guid UserId { get; init; }
    }

    private sealed class PurchaseInvoiceBaseRow
    {
        public Guid InvoiceId { get; init; }
        public Guid TenantId { get; init; }
        public string InvoiceNo { get; init; } = string.Empty;
        public string InvoiceKind { get; init; } = string.Empty;
        public string InvoiceStatus { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string UserEmail { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public string TenantName { get; init; } = string.Empty;
        public string GstNo { get; init; } = string.Empty;
        public DateTime PurchaseDateUtc { get; init; }
        public DateTime InvoiceDateUtc { get; init; }
        public string ServiceName { get; init; } = string.Empty;
        public string BillingCycle { get; init; } = string.Empty;
        public decimal Amount { get; init; }
        public decimal GstAmount { get; init; }
        public decimal TotalAmount { get; init; }
        public string ReferenceNo { get; init; } = string.Empty;
        public string BillingEmail { get; init; } = string.Empty;
        public DateTime? PaidAtUtc { get; init; }
        public DateTime CreatedAtUtc { get; init; }
    }
}

