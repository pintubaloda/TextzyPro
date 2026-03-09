using System.Net.Http.Headers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/billing")]
public class BillingController(
    ControlDbContext db,
    TenantDbContext tenantDb,
    AuthContext auth,
    TenancyContext tenancy,
    RbacService rbac,
    BillingGuardService billingGuard,
    SecretCryptoService crypto,
    IEmailService emailService,
    IRazorpayPaymentValidator razorpayPaymentValidator,
    InvoiceAttachmentService invoiceAttachmentService,
    IConfiguration config,
    SensitiveDataRedactor redactor,
    AuditLogService audit,
    ILogger<BillingController> logger) : ControllerBase
{
    [HttpGet("plans")]
    public async Task<IActionResult> Plans(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();
        var rows = await db.BillingPlans.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(ct);
        return Ok(rows.Select(MapPlan));
    }

    [HttpGet("current-plan")]
    public async Task<IActionResult> CurrentPlan(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();
        var sub = await db.TenantSubscriptions.Where(x => x.TenantId == tenancy.TenantId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var creditBalances = await GetCreditBalancesAsync(tenancy.TenantId, ct);
        if (sub is null)
        {
            return Ok(new
            {
                subscription = (object?)null,
                plan = (object?)null,
                creditBalances
            });
        }
        var plan = await db.BillingPlans.FirstOrDefaultAsync(x => x.Id == sub.PlanId, ct);
        if (plan is null)
        {
            return Ok(new
            {
                subscription = new { sub.Id, sub.TenantId, sub.PlanId, sub.Status, sub.BillingCycle, sub.StartedAtUtc, sub.RenewAtUtc, sub.CancelledAtUtc },
                plan = (object?)null,
                creditBalances
            });
        }
        return Ok(new
        {
            subscription = new { sub.Id, sub.TenantId, sub.PlanId, sub.Status, sub.BillingCycle, sub.StartedAtUtc, sub.RenewAtUtc, sub.CancelledAtUtc },
            plan = MapPlan(plan),
            creditBalances
        });
    }

    [HttpGet("usage")]
    public async Task<IActionResult> Usage(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();
        var monthKey = DateTime.UtcNow.ToString("yyyy-MM");
        var usage = await db.TenantUsages.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.MonthKey == monthKey, ct);
        var creditBalances = await GetCreditBalancesAsync(tenancy.TenantId, ct);
        if (usage is null) return Ok(new { monthKey, values = new Dictionary<string, int>(), creditBalances });
        return Ok(new
        {
            usage.MonthKey,
            values = new Dictionary<string, int>
            {
                ["whatsappMessages"] = usage.WhatsappMessagesUsed,
                ["smsCredits"] = usage.SmsCreditsUsed,
                ["contacts"] = usage.ContactsUsed,
                ["teamMembers"] = usage.TeamMembersUsed,
                ["chatbots"] = usage.ChatbotsUsed,
                ["flows"] = usage.FlowsUsed,
                ["apiCalls"] = usage.ApiCallsUsed
            },
            creditBalances
        });
    }

    [HttpGet("dunning-status")]
    public async Task<IActionResult> DunningStatus(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();

        var sub = await db.TenantSubscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (sub is null) return Ok(new { hasSubscription = false });

        var now = DateTime.UtcNow;
        var graceDays = ResolveGraceDaysForApi(config);
        var renewalReminderDays = ResolveDayOffsetsForApi(config["Billing:RenewalReminderDays"] ?? config["BILLING_RENEWAL_REMINDER_DAYS"], [7, 3, 1, 0]);
        var dunningReminderDays = ResolveDayOffsetsForApi(config["Billing:DunningReminderDays"] ?? config["BILLING_DUNNING_REMINDER_DAYS"], [1, 3, 5]);
        var daysToRenew = (int)Math.Floor((sub.RenewAtUtc - now).TotalDays);
        var daysPastDue = Math.Max(0, (int)Math.Floor((now - sub.RenewAtUtc).TotalDays));
        var graceDeadlineUtc = sub.RenewAtUtc.AddDays(graceDays);
        var graceDaysLeft = Math.Max(0, (int)Math.Ceiling((graceDeadlineUtc - now).TotalDays));

        var eventActions = new[] { "billing.renewal.reminder", "billing.dunning.reminder", "billing.dunning.suspended" };
        var events = await db.AuditLogs.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && eventActions.Contains(x.Action))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(x => new { x.Action, x.Details, x.CreatedAtUtc })
            .ToListAsync(ct);

        var nextAction = "none";
        if (string.Equals(sub.Status, "active", StringComparison.OrdinalIgnoreCase) && renewalReminderDays.Contains(daysToRenew))
            nextAction = $"renewal_reminder_d{daysToRenew}";
        else if (string.Equals(sub.Status, "past_due", StringComparison.OrdinalIgnoreCase) && dunningReminderDays.Contains(daysPastDue))
            nextAction = $"dunning_reminder_d{daysPastDue}";
        else if (string.Equals(sub.Status, "past_due", StringComparison.OrdinalIgnoreCase) && now >= graceDeadlineUtc)
            nextAction = "suspend";

        return Ok(new
        {
            hasSubscription = true,
            subscription = new
            {
                sub.Id,
                sub.Status,
                sub.BillingCycle,
                sub.StartedAtUtc,
                sub.RenewAtUtc,
                sub.UpdatedAtUtc
            },
            dunning = new
            {
                nowUtc = now,
                graceDays,
                renewalReminderDays = renewalReminderDays.OrderBy(x => x).ToArray(),
                dunningReminderDays = dunningReminderDays.OrderBy(x => x).ToArray(),
                daysToRenew,
                daysPastDue,
                graceDeadlineUtc,
                graceDaysLeft,
                nextAction
            },
            recentEvents = events
        });
    }

    [HttpPost("usage/resync")]
    public async Task<IActionResult> ResyncUsage(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingWrite)) return Forbid();

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = monthStart.AddMonths(1);
        var monthKey = now.ToString("yyyy-MM");

        var whatsappMessages = await tenantDb.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && x.Channel == ChannelType.WhatsApp
                        && !string.Equals(x.Status, "Received", StringComparison.OrdinalIgnoreCase)
                        && x.CreatedAtUtc >= monthStart
                        && x.CreatedAtUtc < nextMonth)
            .CountAsync(ct);

        var smsCredits = await tenantDb.SmsBillingLedgers.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId
                        && x.CreatedAtUtc >= monthStart
                        && x.CreatedAtUtc < nextMonth)
            .SumAsync(x => (int?)x.Segments, ct) ?? 0;

        var contacts = await tenantDb.Contacts.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .CountAsync(ct);

        var teamMembers = await db.TenantUsers.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .CountAsync(ct);

        var flows = await tenantDb.AutomationFlows.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .CountAsync(ct);

        var chatbots = await tenantDb.AutomationFlows.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.LifecycleStatus.ToLower() == "published")
            .CountAsync(ct);

        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "whatsappMessages", whatsappMessages, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "smsCredits", smsCredits, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "contacts", contacts, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "teamMembers", teamMembers, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "flows", flows, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "chatbots", chatbots, ct);

        var usage = await db.TenantUsages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.MonthKey == monthKey, ct);

        return Ok(new
        {
            resynced = true,
            monthKey,
            note = "apiCalls was not rebuilt from history; it continues from live runtime metering.",
            values = usage is null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>
                {
                    ["whatsappMessages"] = usage.WhatsappMessagesUsed,
                    ["smsCredits"] = usage.SmsCreditsUsed,
                    ["contacts"] = usage.ContactsUsed,
                    ["teamMembers"] = usage.TeamMembersUsed,
                    ["chatbots"] = usage.ChatbotsUsed,
                    ["flows"] = usage.FlowsUsed,
                    ["apiCalls"] = usage.ApiCallsUsed
                }
        });
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();
        var rows = await db.BillingInvoices
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.PaidAtUtc ?? x.IssuedAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);
        return Ok(rows.Select(x => new
        {
            x.Id,
            x.InvoiceNo,
            x.InvoiceKind,
            x.BillingCycle,
            x.TaxMode,
            x.ReferenceNo,
            x.Description,
            x.PeriodStartUtc,
            x.PeriodEndUtc,
            x.Subtotal,
            x.TaxAmount,
            x.Total,
            x.Status,
            x.PaidAtUtc,
            x.PdfUrl,
            x.IntegrityAlgo,
            x.IntegrityHash,
            x.IssuedAtUtc,
            x.CreatedAtUtc
        }));
    }

    [HttpGet("invoices/{invoiceId:guid}/download")]
    public async Task<IActionResult> DownloadInvoice(Guid invoiceId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();

        var inv = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId && x.TenantId == tenancy.TenantId, ct);
        if (inv is null) return NotFound("Invoice not found.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == tenancy.TenantId, ct);
        var profile = await db.TenantCompanyProfiles.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId, ct);
        var companyName = string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Textzy Workspace") : profile!.CompanyName;
        var legalName = string.IsNullOrWhiteSpace(profile?.LegalName) ? companyName : profile!.LegalName;
        var billingEmail = profile?.BillingEmail ?? string.Empty;
        var billingAddress = profile?.Address ?? string.Empty;
        var gstin = profile?.Gstin ?? string.Empty;
        var pan = profile?.Pan ?? string.Empty;
        var branding = await GetPlatformBrandingAsync(ct);
        var integrityHash = await EnsureInvoiceIntegrityHashAsync(inv, ct);
        var verificationUrl = BuildPublicInvoiceVerificationUrl(inv.Id, integrityHash, branding.Website);
        var qrCodeUrl = BuildQrCodeUrl(verificationUrl);
        var html = InvoiceDocumentRenderer.BuildInvoiceHtml(
            inv,
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
                BillingEmail = billingEmail,
                Address = billingAddress,
                Gstin = gstin,
                Pan = pan
            },
            profile?.TaxRatePercent ?? 18m,
            profile?.IsTaxExempt ?? false,
            profile?.IsReverseCharge ?? false,
            verificationUrl,
            qrCodeUrl);
        var bytes = Encoding.UTF8.GetBytes(html);
        var filename = $"{(string.IsNullOrWhiteSpace(inv.InvoiceNo) ? inv.Id.ToString("N") : inv.InvoiceNo)}.html";
        return File(bytes, "text/html; charset=utf-8", filename);
    }

    [HttpGet("invoices/{invoiceId:guid}/verify")]
    public async Task<IActionResult> VerifyInvoice(Guid invoiceId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();

        var inv = await db.BillingInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId && x.TenantId == tenancy.TenantId, ct);
        if (inv is null) return NotFound("Invoice not found.");

        var expected = ComputeInvoiceIntegrityHash(inv);
        var legacyExpected = ComputeInvoiceIntegrityHashLegacy(inv);
        var valid = string.Equals(expected, inv.IntegrityHash, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(legacyExpected, inv.IntegrityHash, StringComparison.OrdinalIgnoreCase);
        return Ok(new
        {
            invoiceId = inv.Id,
            invoiceNo = inv.InvoiceNo,
            integrityAlgo = inv.IntegrityAlgo,
            integrityHash = inv.IntegrityHash,
            expectedHash = expected,
            legacyExpectedHash = legacyExpected,
            valid
        });
    }

    private async Task<string> EnsureInvoiceIntegrityHashAsync(BillingInvoice invoice, CancellationToken ct)
    {
        var expected = ComputeInvoiceIntegrityHash(invoice);
        var legacyExpected = ComputeInvoiceIntegrityHashLegacy(invoice);
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

    [HttpGet("invoices/download-all")]
    public async Task<IActionResult> DownloadAllInvoices(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();

        var rows = await db.BillingInvoices
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNo,Description,PeriodStartUtc,PeriodEndUtc,Subtotal,TaxAmount,Total,Status,PaidAtUtc,CreatedAtUtc");
        foreach (var x in rows)
        {
            sb.AppendLine(string.Join(",",
                Csv(x.InvoiceNo),
                Csv(x.Description),
                Csv(x.PeriodStartUtc.ToString("O")),
                Csv(x.PeriodEndUtc.ToString("O")),
                Csv(x.Subtotal.ToString("0.00")),
                Csv(x.TaxAmount.ToString("0.00")),
                Csv(x.Total.ToString("0.00")),
                Csv(x.Status),
                Csv(x.PaidAtUtc?.ToString("O") ?? ""),
                Csv(x.CreatedAtUtc.ToString("O"))));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", "textzy-invoices.csv");
    }

    [HttpGet("reconciliation/export")]
    public async Task<IActionResult> ReconciliationExport([FromQuery] string month = "", CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();

        var monthKey = string.IsNullOrWhiteSpace(month) ? DateTime.UtcNow.ToString("yyyy-MM") : month.Trim();
        var monthStart = DateTime.TryParse($"{monthKey}-01T00:00:00Z", out var parsedStart)
            ? DateTime.SpecifyKind(parsedStart, DateTimeKind.Utc)
            : new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        var usage = await db.TenantUsages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.MonthKey == monthKey, ct);
        var attempts = await db.BillingPaymentAttempts.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        var invoices = await db.BillingInvoices.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        var smsTotal = await tenantDb.SmsBillingLedgers.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= monthStart && x.CreatedAtUtc < monthEnd)
            .SumAsync(x => (decimal?)x.TotalAmount, ct) ?? 0m;

        var sb = new StringBuilder();
        sb.AppendLine("section,key,value");
        sb.AppendLine($"usage,monthKey,{Csv(monthKey)}");
        if (usage is not null)
        {
            sb.AppendLine($"usage,whatsappMessages,{usage.WhatsappMessagesUsed}");
            sb.AppendLine($"usage,smsCredits,{usage.SmsCreditsUsed}");
            sb.AppendLine($"usage,contacts,{usage.ContactsUsed}");
            sb.AppendLine($"usage,teamMembers,{usage.TeamMembersUsed}");
            sb.AppendLine($"usage,chatbots,{usage.ChatbotsUsed}");
            sb.AppendLine($"usage,flows,{usage.FlowsUsed}");
            sb.AppendLine($"usage,apiCalls,{usage.ApiCallsUsed}");
        }
        sb.AppendLine($"usage,smsLedgerTotal,{smsTotal:0.00}");
        sb.AppendLine($"summary,paymentAttempts,{attempts.Count}");
        sb.AppendLine($"summary,invoices,{invoices.Count}");
        var invoicesPaid = invoices.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
        var invoicesRefunded = invoices.Count(x => string.Equals(x.Status, "refunded", StringComparison.OrdinalIgnoreCase));
        var attemptsPaid = attempts.Count(x => string.Equals(x.Status, "paid", StringComparison.OrdinalIgnoreCase));
        var attemptsFailed = attempts.Count(x => x.Status.Contains("failed", StringComparison.OrdinalIgnoreCase));
        sb.AppendLine($"summary,invoicesPaid,{invoicesPaid}");
        sb.AppendLine($"summary,invoicesRefunded,{invoicesRefunded}");
        sb.AppendLine($"summary,attemptsPaid,{attemptsPaid}");
        sb.AppendLine($"summary,attemptsFailed,{attemptsFailed}");

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=utf-8", $"billing-reconciliation-{monthKey}.csv");
    }

    [HttpPost("change-plan")]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.PlanCode)) return BadRequest("planCode is required.");
        var plan = await db.BillingPlans.FirstOrDefaultAsync(x => x.Code == request.PlanCode && x.IsActive, ct);
        if (plan is null) return NotFound("Plan not found.");

        var sub = await db.TenantSubscriptions.Where(x => x.TenantId == tenancy.TenantId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        decimal? prorationCredit = null;
        decimal? prorationDebit = null;
        decimal? prorationNet = null;
        if (sub is not null && sub.PlanId != plan.Id && sub.RenewAtUtc > DateTime.UtcNow)
        {
            var oldPlan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sub.PlanId, ct);
            if (oldPlan is not null)
            {
                var oldPrice = string.Equals(sub.BillingCycle, "yearly", StringComparison.OrdinalIgnoreCase) ? oldPlan.PriceYearly : oldPlan.PriceMonthly;
                var newPrice = string.Equals(sub.BillingCycle, "yearly", StringComparison.OrdinalIgnoreCase) ? plan.PriceYearly : plan.PriceMonthly;
                var totalWindow = Math.Max(1, (sub.RenewAtUtc - sub.StartedAtUtc).TotalSeconds);
                var remaining = Math.Max(0, (sub.RenewAtUtc - DateTime.UtcNow).TotalSeconds);
                var ratio = (decimal)Math.Clamp(remaining / totalWindow, 0, 1);
                prorationCredit = Math.Round(oldPrice * ratio, 2, MidpointRounding.AwayFromZero);
                prorationDebit = Math.Round(newPrice * ratio, 2, MidpointRounding.AwayFromZero);
                prorationNet = Math.Round((prorationDebit ?? 0) - (prorationCredit ?? 0), 2, MidpointRounding.AwayFromZero);
            }
        }

        if (sub is null)
        {
            sub = new Textzy.Api.Models.TenantSubscription { Id = Guid.NewGuid(), TenantId = tenancy.TenantId, PlanId = plan.Id };
            db.TenantSubscriptions.Add(sub);
        }
        sub.PlanId = plan.Id;
        var normalizedCycle = NormalizeBillingCycle(request.BillingCycle);
        if (string.IsNullOrWhiteSpace(normalizedCycle))
            return BadRequest("billingCycle must be monthly, yearly, lifetime, or usage_based.");
        sub.BillingCycle = normalizedCycle;
        sub.Status = "active";
        sub.UpdatedAtUtc = DateTime.UtcNow;
        sub.RenewAtUtc = ResolveRenewAtUtc(DateTime.UtcNow, sub.BillingCycle);
        await db.SaveChangesAsync(ct);
        await TrySendBillingEventAsync(
            tenancy.TenantId,
            "Plan changed",
            "Your subscription plan has been updated successfully.",
            new Dictionary<string, string>
            {
                ["Plan"] = plan.Name,
                ["Code"] = plan.Code,
                ["Billing Cycle"] = sub.BillingCycle,
                ["Status"] = sub.Status
            },
            ct);
        return Ok(new
        {
            changed = true,
            planCode = plan.Code,
            proration = prorationNet.HasValue
                ? new { credit = prorationCredit, debit = prorationDebit, net = prorationNet, currency = plan.Currency }
                : null
        });
    }

    [HttpGet("payment-config")]
    public async Task<IActionResult> PaymentConfig(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(BillingRead)) return Forbid();

        var cfg = await ReadPaymentSettingsAsync(ct);
        var provider = (cfg.TryGetValue("provider", out var p) ? p : "razorpay").Trim().ToLowerInvariant();
        var mode = NormalizeRazorpayMode(cfg.TryGetValue("mode", out var m) ? m : "test");
        var keyId = cfg.TryGetValue("keyId", out var kid) ? kid : string.Empty;
        var isPlatformOwner = string.Equals(auth.Role, RolePermissionCatalog.SuperAdmin, StringComparison.OrdinalIgnoreCase);

        return Ok(new
        {
            provider,
            mode,
            razorpay = new
            {
                enabled = provider == "razorpay" && !string.IsNullOrWhiteSpace(keyId),
                keyId = isPlatformOwner ? keyId : string.Empty,
                checkoutKeyId = keyId,
                platformManaged = !isPlatformOwner
            }
        });
    }

    [HttpPost("razorpay/create-order")]
    public async Task<IActionResult> RazorpayCreateOrder([FromBody] ChangePlanRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.PlanCode)) return BadRequest("planCode is required.");

        var plan = await db.BillingPlans.FirstOrDefaultAsync(x => x.Code == request.PlanCode && x.IsActive, ct);
        if (plan is null) return NotFound("Plan not found.");

        var cycle = string.IsNullOrWhiteSpace(request.BillingCycle) ? "monthly" : request.BillingCycle.Trim().ToLowerInvariant();
        if (string.Equals(plan.PricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
            cycle = "usage_based";
        if (cycle != "monthly" && cycle != "yearly" && cycle != "usage_based")
            return BadRequest("billingCycle must be monthly, yearly or usage_based.");

        var cfg = await ReadPaymentSettingsAsync(ct);
        var provider = (cfg.TryGetValue("provider", out var p) ? p : "razorpay").Trim().ToLowerInvariant();
        if (provider != "razorpay") return BadRequest("Configured payment provider is not Razorpay.");
        var mode = NormalizeRazorpayMode(cfg.TryGetValue("mode", out var m) ? m : "test");
        var keyId = cfg.TryGetValue("keyId", out var kid) ? kid : string.Empty;
        var keySecret = cfg.TryGetValue("keySecret", out var ks) ? ks : string.Empty;
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            return BadRequest("Razorpay keyId/keySecret not configured in platform settings.");
        if (!IsRazorpayKeyModeValid(keyId, mode))
            return BadRequest($"Razorpay keyId does not match configured mode '{mode}'.");

        var amount = cycle == "yearly" ? plan.PriceYearly : plan.PriceMonthly;
        var (taxRate, isTaxExempt, isReverseCharge) = await ResolveTaxProfileAsync(tenancy.TenantId, ct);
        var invoicePreview = ComputeInvoiceAmounts(amount, taxRate, plan.TaxMode, isTaxExempt, isReverseCharge);
        var amountPaise = (int)Math.Round(invoicePreview.Total * 100m, MidpointRounding.AwayFromZero);
        if (amountPaise <= 0) return BadRequest("Invalid plan amount.");

        var receipt = BuildRazorpayReceipt("pln", tenancy.TenantId);
        var notes = new Dictionary<string, string>
        {
            ["tenantId"] = tenancy.TenantId.ToString(),
            ["planId"] = plan.Id.ToString(),
            ["planCode"] = plan.Code,
            ["billingCycle"] = cycle,
            ["mode"] = mode
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var authBytes = Encoding.UTF8.GetBytes($"{keyId}:{keySecret}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        var payload = new
        {
            amount = amountPaise,
            currency = string.IsNullOrWhiteSpace(plan.Currency) ? "INR" : plan.Currency.ToUpperInvariant(),
            receipt,
            notes
        };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync("https://api.razorpay.com/v1/orders", content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Razorpay order create failed status={Status} body={Body}", (int)resp.StatusCode, redactor.RedactText(raw));
            return BadRequest(ExtractRazorpayErrorMessage(raw, "Failed to create Razorpay order."));
        }

        using var doc = JsonDocument.Parse(raw);
        var orderId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(orderId)) return BadRequest("Razorpay did not return order id.");

        var attempt = new BillingPaymentAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            PlanId = plan.Id,
            BillingCycle = cycle,
            Provider = "razorpay",
            OrderId = orderId,
            Amount = invoicePreview.Total,
            Currency = payload.currency,
            Status = "created",
            NotesJson = JsonSerializer.Serialize(notes),
            RawResponse = raw,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.BillingPaymentAttempts.Add(attempt);
        await CreateOrUpdateProformaInvoiceAsync(
            tenancy.TenantId,
            plan,
            cycle,
            orderId,
            invoicePreview,
            ct);
        await db.SaveChangesAsync(ct);
        await TrySendBillingEventAsync(
            tenancy.TenantId,
            "Payment initiated",
            "A Razorpay order and proforma invoice were created for your purchase.",
            new Dictionary<string, string>
            {
                ["Plan"] = plan.Name,
                ["Billing Cycle"] = cycle,
                ["Amount"] = FormatCurrency(invoicePreview.Total, payload.currency),
                ["Order ID"] = orderId,
                ["Mode"] = mode,
                ["Invoice Type"] = "Proforma Invoice"
            },
            ct);

        return Ok(new
        {
            provider = "razorpay",
            mode,
            keyId,
            orderId,
            amount = amountPaise,
            currency = payload.currency,
            planCode = plan.Code,
            billingCycle = cycle,
            tenantId = tenancy.TenantId
        });
    }

    [HttpPost("razorpay/create-integration-order")]
    public async Task<IActionResult> RazorpayCreateIntegrationOrder([FromBody] CreateIntegrationOrderRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingWrite)) return Forbid();

        var slug = (request.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug)) return BadRequest("slug is required.");

        var item = await FindIntegrationCatalogItemAsync(slug, ct);
        if (item is null || !item.IsVisible || !item.IsActive)
            return NotFound("Integration not found.");
        if (!string.Equals(item.PricingType, "paid", StringComparison.OrdinalIgnoreCase))
            return BadRequest("This integration does not require paid checkout.");

        var entitlementTokens = await ResolveTenantIntegrationEntitlementTokensAsync(ct);
        if (IsIntegrationEntitled(item, entitlementTokens))
            return BadRequest("This integration is already purchased for this tenant.");

        var cfg = await ReadPaymentSettingsAsync(ct);
        var provider = (cfg.TryGetValue("provider", out var p) ? p : "razorpay").Trim().ToLowerInvariant();
        if (provider != "razorpay") return BadRequest("Configured payment provider is not Razorpay.");
        var mode = NormalizeRazorpayMode(cfg.TryGetValue("mode", out var m) ? m : "test");
        var keyId = cfg.TryGetValue("keyId", out var kid) ? kid : string.Empty;
        var keySecret = cfg.TryGetValue("keySecret", out var ks) ? ks : string.Empty;
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(keySecret))
            return BadRequest("Razorpay keyId/keySecret not configured in platform settings.");
        if (!IsRazorpayKeyModeValid(keyId, mode))
            return BadRequest($"Razorpay keyId does not match configured mode '{mode}'.");

        var (taxRate, isTaxExempt, isReverseCharge) = await ResolveTaxProfileAsync(tenancy.TenantId, ct);
        var invoicePreview = ComputeInvoiceAmounts(item.Price, taxRate, item.TaxMode, isTaxExempt, isReverseCharge);
        var amountPaise = (int)Math.Round(invoicePreview.Total * 100m, MidpointRounding.AwayFromZero);
        if (amountPaise <= 0) return BadRequest("Invalid integration amount.");

        var cycle = string.Equals(item.BillingFrequency, "one_time", StringComparison.OrdinalIgnoreCase) ? "one_time" : "monthly";
        var receipt = BuildRazorpayReceipt("int", tenancy.TenantId);
        var notes = new Dictionary<string, string>
        {
            ["tenantId"] = tenancy.TenantId.ToString(),
            ["purchaseType"] = "integration",
            ["integrationSlug"] = item.Slug,
            ["integrationName"] = item.Name,
            ["billingCycle"] = cycle,
            ["taxMode"] = item.TaxMode,
            ["mode"] = mode
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var authBytes = Encoding.UTF8.GetBytes($"{keyId}:{keySecret}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        var payload = new
        {
            amount = amountPaise,
            currency = string.IsNullOrWhiteSpace(item.Currency) ? "INR" : item.Currency.ToUpperInvariant(),
            receipt,
            notes
        };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync("https://api.razorpay.com/v1/orders", content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogWarning("Razorpay integration order create failed status={Status} body={Body}", (int)resp.StatusCode, redactor.RedactText(raw));
            return BadRequest(ExtractRazorpayErrorMessage(raw, "Failed to create Razorpay order."));
        }

        using var doc = JsonDocument.Parse(raw);
        var orderId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(orderId)) return BadRequest("Razorpay did not return order id.");

        var attempt = new BillingPaymentAttempt
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            PlanId = Guid.Empty,
            BillingCycle = cycle,
            Provider = "razorpay",
            OrderId = orderId,
            Amount = invoicePreview.Total,
            Currency = payload.currency,
            Status = "created",
            NotesJson = JsonSerializer.Serialize(notes),
            RawResponse = raw,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.BillingPaymentAttempts.Add(attempt);
        await CreateOrUpdateIntegrationProformaInvoiceAsync(tenancy.TenantId, item, cycle, orderId, invoicePreview, ct);
        await db.SaveChangesAsync(ct);
        await TrySendBillingEventAsync(
            tenancy.TenantId,
            "Integration checkout initiated",
            "A Razorpay order and proforma invoice were created for your add-on purchase.",
            new Dictionary<string, string>
            {
                ["Integration"] = item.Name,
                ["Billing Cycle"] = cycle,
                ["Amount"] = FormatCurrency(invoicePreview.Total, payload.currency),
                ["Order ID"] = orderId,
                ["Mode"] = mode,
                ["Invoice Type"] = "Proforma Invoice"
            },
            ct);

        return Ok(new
        {
            provider = "razorpay",
            mode,
            keyId,
            orderId,
            amount = amountPaise,
            currency = payload.currency,
            integrationSlug = item.Slug,
            integrationName = item.Name,
            billingCycle = cycle,
            tenantId = tenancy.TenantId
        });
    }

    [HttpPost("razorpay/verify")]
    public async Task<IActionResult> RazorpayVerify([FromBody] RazorpayVerifyRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.RazorpayOrderId) ||
            string.IsNullOrWhiteSpace(request.RazorpayPaymentId) ||
            string.IsNullOrWhiteSpace(request.RazorpaySignature))
            return BadRequest("Missing Razorpay payment fields.");

        var cfg = await ReadPaymentSettingsAsync(ct);
        var mode = NormalizeRazorpayMode(cfg.TryGetValue("mode", out var m) ? m : "test");
        var keyId = cfg.TryGetValue("keyId", out var kid) ? kid : string.Empty;
        var keySecret = cfg.TryGetValue("keySecret", out var ks) ? ks : string.Empty;
        if (string.IsNullOrWhiteSpace(keySecret) || string.IsNullOrWhiteSpace(keyId)) return BadRequest("Razorpay keyId/keySecret not configured.");
        if (!IsRazorpayKeyModeValid(keyId, mode))
            return BadRequest($"Razorpay keyId does not match configured mode '{mode}'.");

        var attempt = await db.BillingPaymentAttempts
            .Where(x => x.Provider == "razorpay" && x.OrderId == request.RazorpayOrderId && x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (attempt is null) return NotFound("Payment attempt not found.");

        if (attempt.Status == "paid")
        {
            await ActivatePurchaseFromPaymentAsync(attempt, ct);
            await db.SaveChangesAsync(ct);
            var existingNotes = ParseAttemptNotes(attempt.NotesJson);
            return Ok(new
            {
                verified = true,
                alreadyProcessed = true,
                planCode = request.PlanCode,
                billingCycle = attempt.BillingCycle,
                purchaseType = existingNotes.TryGetValue("purchaseType", out var existingPurchaseType) ? existingPurchaseType : "plan",
                integrationSlug = existingNotes.TryGetValue("integrationSlug", out var existingIntegrationSlug) ? existingIntegrationSlug : string.Empty
            });
        }

        var valid = VerifyRazorpaySignature(request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature, keySecret);
        if (!valid)
        {
            attempt.Status = "signature_failed";
            attempt.LastError = "Razorpay signature mismatch";
            attempt.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await TrySendBillingEventAsync(
                tenancy.TenantId,
                "Payment verification failed",
                "We could not verify your payment signature.",
                new Dictionary<string, string>
                {
                    ["Order ID"] = request.RazorpayOrderId,
                    ["Payment ID"] = request.RazorpayPaymentId,
                    ["Reason"] = "Signature mismatch"
                },
                ct);
            return BadRequest("Invalid payment signature.");
        }

        attempt.PaymentId = request.RazorpayPaymentId;
        attempt.Signature = request.RazorpaySignature;

        var validation = await razorpayPaymentValidator.ValidateAsync(
            keyId,
            keySecret,
            request.RazorpayPaymentId,
            request.RazorpayOrderId,
            attempt.Amount,
            attempt.Currency,
            ct);
        if (!validation.Ok)
        {
            attempt.Status = "payment_validation_failed";
            attempt.LastError = validation.Error;
            attempt.UpdatedAtUtc = DateTime.UtcNow;
            attempt.RawResponse = validation.Raw ?? attempt.RawResponse;
            await db.SaveChangesAsync(ct);
            await TrySendBillingEventAsync(
                tenancy.TenantId,
                "Payment validation failed",
                "Payment did not pass final validation checks.",
                new Dictionary<string, string>
                {
                    ["Order ID"] = request.RazorpayOrderId,
                    ["Payment ID"] = request.RazorpayPaymentId,
                    ["Reason"] = validation.Error
                },
                ct);
            return BadRequest(validation.Error);
        }

        attempt.Status = "paid";
        attempt.PaidAtUtc = DateTime.UtcNow;
        attempt.UpdatedAtUtc = DateTime.UtcNow;
        attempt.RawResponse = validation.Raw ?? attempt.RawResponse;

        await ActivatePurchaseFromPaymentAsync(attempt, ct);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("billing.razorpay.verify.success", $"tenant={tenancy.TenantId}; order={attempt.OrderId}", ct);
        await TrySendBillingEventAsync(
            tenancy.TenantId,
            "Payment successful",
            "Your payment is verified and subscription is active.",
            new Dictionary<string, string>
            {
                ["Order ID"] = attempt.OrderId,
                ["Payment ID"] = attempt.PaymentId ?? request.RazorpayPaymentId,
                ["Billing Cycle"] = attempt.BillingCycle,
                ["Amount"] = FormatCurrency(attempt.Amount, attempt.Currency)
            },
            ct);
        var purchaseNotes = ParseAttemptNotes(attempt.NotesJson);
        return Ok(new
        {
            verified = true,
            planCode = request.PlanCode,
            billingCycle = attempt.BillingCycle,
            purchaseType = purchaseNotes.TryGetValue("purchaseType", out var purchaseType) ? purchaseType : "plan",
            integrationSlug = purchaseNotes.TryGetValue("integrationSlug", out var integrationSlug) ? integrationSlug : string.Empty
        });
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!rbac.HasPermission(BillingWrite)) return Forbid();
        var sub = await db.TenantSubscriptions.Where(x => x.TenantId == tenancy.TenantId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (sub is null) return NotFound("Subscription not found.");
        sub.Status = "cancelled";
        sub.CancelledAtUtc = DateTime.UtcNow;
        sub.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await TrySendBillingEventAsync(
            tenancy.TenantId,
            "Subscription cancelled",
            "Your subscription has been cancelled.",
            new Dictionary<string, string>
            {
                ["Status"] = sub.Status,
                ["Cancelled At"] = (sub.CancelledAtUtc ?? DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            },
            ct);
        return Ok(new { cancelled = true });
    }

    private static object MapPlan(Textzy.Api.Models.BillingPlan p) => new
    {
        p.Id,
        p.Code,
        p.Name,
        p.PricingModel,
        p.PriceMonthly,
        p.PriceYearly,
        p.TaxMode,
        p.UsageUnitName,
        p.IncludedQuantity,
        p.Currency,
        p.IsActive,
        p.SortOrder,
        Features = ParseStringList(p.FeaturesJson),
        Limits = ParseLimits(p.LimitsJson)
    };

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; }
    }
    private static Dictionary<string, int> ParseLimits(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new(); } catch { return new(); }
    }

    public sealed class ChangePlanRequest
    {
        public string PlanCode { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = "monthly";
    }

    public sealed class CreateIntegrationOrderRequest
    {
        public string Slug { get; set; } = string.Empty;
    }

    public sealed class RazorpayVerifyRequest
    {
        public string PlanCode { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = "monthly";
        public string RazorpayOrderId { get; set; } = string.Empty;
        public string RazorpayPaymentId { get; set; } = string.Empty;
        public string RazorpaySignature { get; set; } = string.Empty;
    }

    private async Task<Dictionary<string, string>> ReadPaymentSettingsAsync(CancellationToken ct)
    {
        var scopeRows = await db.PlatformSettings.Where(x => x.Scope == "payment-gateway").ToListAsync(ct);
        return scopeRows.ToDictionary(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase);
    }

    private static bool VerifyRazorpaySignature(string orderId, string paymentId, string signature, string secret)
    {
        var payload = $"{orderId}|{paymentId}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(expected, signature.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static string NormalizeRazorpayMode(string? mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized == "live" ? "live" : "test";
    }

    private static string ExtractRazorpayErrorMessage(string raw, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("description", out var desc) && !string.IsNullOrWhiteSpace(desc.GetString()))
                    return desc.GetString()!;
                if (err.TryGetProperty("reason", out var reason) && !string.IsNullOrWhiteSpace(reason.GetString()))
                    return reason.GetString()!;
                if (err.TryGetProperty("code", out var code) && !string.IsNullOrWhiteSpace(code.GetString()))
                    return $"Razorpay error: {code.GetString()}";
            }
        }
        catch
        {
            // ignore parse failures
        }
        return fallback;
    }

    private static bool IsRazorpayKeyModeValid(string keyId, string mode)
    {
        var key = (keyId ?? string.Empty).Trim().ToLowerInvariant();
        if (mode == "live") return key.StartsWith("rzp_live_");
        return key.StartsWith("rzp_test_");
    }

    private async Task<(decimal taxRatePercent, bool isTaxExempt, bool isReverseCharge)> ResolveTaxProfileAsync(Guid tenantId, CancellationToken ct)
    {
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (profile is null) return (18m, false, false);
        return (Math.Clamp(profile.TaxRatePercent, 0m, 100m), profile.IsTaxExempt, profile.IsReverseCharge);
    }

    private async Task<object> GetCreditBalancesAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.TenantUsageCreditBalances
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.MetricKey, x => x.UnitsRemaining, StringComparer.OrdinalIgnoreCase);
    }

    private async Task CreateOrUpdateProformaInvoiceAsync(
        Guid tenantId,
        BillingPlan plan,
        string billingCycle,
        string orderId,
        InvoiceAmounts invoice,
        CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        var periodStart = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = ResolvePeriodEndUtc(periodStart, billingCycle);
        var invoiceNo = $"PI-{start:yyyyMMdd}-{orderId}";
        var existing = await db.BillingInvoices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.InvoiceNo == invoiceNo, ct);
        if (existing is null)
        {
            existing = new BillingInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNo = invoiceNo,
                TenantId = tenantId,
                InvoiceKind = "proforma_invoice",
                BillingCycle = billingCycle,
                TaxMode = plan.TaxMode,
                ReferenceNo = orderId,
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                Status = "issued",
                PaidAtUtc = null,
                PdfUrl = string.Empty,
                IntegrityAlgo = "SHA256",
                IssuedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.BillingInvoices.Add(existing);
        }

        existing.Subtotal = invoice.Subtotal;
        existing.TaxAmount = invoice.TaxAmount;
        existing.Total = invoice.Total;
        existing.TaxMode = plan.TaxMode;
        existing.BillingCycle = billingCycle;
        existing.ReferenceNo = orderId;
        existing.Description = ResolveInvoiceDescription(plan.Name, billingCycle, plan.PricingModel);
        existing.IntegrityHash = ComputeInvoiceIntegrityHash(existing);
    }

    private async Task CreateOrUpdateIntegrationProformaInvoiceAsync(
        Guid tenantId,
        IntegrationCatalogController.IntegrationCatalogItem item,
        string billingCycle,
        string orderId,
        InvoiceAmounts invoice,
        CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        var periodStart = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = ResolveAddOnPeriodEndUtc(periodStart, billingCycle);
        var invoiceNo = $"PI-ADD-{start:yyyyMMdd}-{orderId}";
        var existing = await db.BillingInvoices.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.InvoiceNo == invoiceNo, ct);
        if (existing is null)
        {
            existing = new BillingInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNo = invoiceNo,
                TenantId = tenantId,
                InvoiceKind = "proforma_invoice",
                BillingCycle = billingCycle,
                TaxMode = item.TaxMode,
                ReferenceNo = orderId,
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                Status = "issued",
                PaidAtUtc = null,
                PdfUrl = string.Empty,
                IntegrityAlgo = "SHA256",
                IssuedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.BillingInvoices.Add(existing);
        }

        existing.Subtotal = invoice.Subtotal;
        existing.TaxAmount = invoice.TaxAmount;
        existing.Total = invoice.Total;
        existing.TaxMode = item.TaxMode;
        existing.BillingCycle = billingCycle;
        existing.ReferenceNo = orderId;
        existing.Description = $"{item.Name} purchase";
        existing.IntegrityHash = ComputeInvoiceIntegrityHash(existing);
    }

    private readonly record struct InvoiceAmounts(decimal Subtotal, decimal TaxAmount, decimal Total);

    private static InvoiceAmounts ComputeInvoiceAmounts(
        decimal planAmount,
        decimal taxRatePercent,
        string? taxMode,
        bool isTaxExempt,
        bool isReverseCharge,
        bool amountIsGross = false)
    {
        var amounts = BillingComputation.ComputeInvoiceAmounts(planAmount, taxRatePercent, taxMode, isTaxExempt, isReverseCharge, amountIsGross);
        return new InvoiceAmounts(amounts.Subtotal, amounts.TaxAmount, amounts.Total);
    }

    private static int ResolvePackUnitsFromLimits(string json, string? usageUnitName)
    {
        try
        {
            var limits = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
            var key = string.IsNullOrWhiteSpace(usageUnitName) ? "smsCredits" : usageUnitName.Trim();
            return Math.Max(0, limits.TryGetValue(key, out var value) ? value : 0);
        }
        catch
        {
            return 0;
        }
    }

    private async Task<List<IntegrationCatalogController.IntegrationCatalogItem>> ReadIntegrationCatalogAsync(CancellationToken ct)
    {
        var row = await db.PlatformSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == "integration-catalog" && x.Key == "items", ct);
        if (row is null) return DefaultIntegrationCatalog();
        try
        {
            var json = crypto.Decrypt(row.ValueEncrypted);
            return JsonSerializer.Deserialize<List<IntegrationCatalogController.IntegrationCatalogItem>>(json) ?? DefaultIntegrationCatalog();
        }
        catch
        {
            return DefaultIntegrationCatalog();
        }
    }

    private async Task<IntegrationCatalogController.IntegrationCatalogItem?> FindIntegrationCatalogItemAsync(string slug, CancellationToken ct)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSlug)) return null;
        var items = await ReadIntegrationCatalogAsync(ct);
        return items.FirstOrDefault(x => string.Equals((x.Slug ?? string.Empty).Trim(), normalizedSlug, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HashSet<string>> ResolveTenantIntegrationEntitlementTokensAsync(CancellationToken ct)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activeStatuses = new[] { "active", "trial", "trialing" };
        var subscription = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && activeStatuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (subscription is not null)
        {
            foreach (var token in ExpandIntegrationEntitlementTokens(subscription.BillingCycle))
                tokens.Add(token);

            var plan = await db.BillingPlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == subscription.PlanId, ct);
            if (plan is not null)
            {
                foreach (var token in ExpandIntegrationEntitlementTokens(plan.Code))
                    tokens.Add(token);
                foreach (var token in ExpandIntegrationEntitlementTokens(plan.Name))
                    tokens.Add(token);
                foreach (var feature in ParseStringList(plan.FeaturesJson))
                {
                    foreach (var token in ExpandIntegrationEntitlementTokens(feature))
                        tokens.Add(token);
                }
            }
        }

        var tenantFlags = await db.TenantFeatureFlags.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.IsEnabled)
            .Select(x => x.FeatureKey)
            .ToListAsync(ct);
        foreach (var featureKey in tenantFlags)
        {
            foreach (var token in ExpandIntegrationEntitlementTokens(featureKey))
                tokens.Add(token);
        }

        return tokens;
    }

    private static bool IsIntegrationEntitled(IntegrationCatalogController.IntegrationCatalogItem item, HashSet<string> entitlementTokens)
    {
        if (entitlementTokens.Count == 0) return false;

        var itemTokens = ExpandIntegrationEntitlementTokens(item.Slug)
            .Concat(ExpandIntegrationEntitlementTokens(item.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (itemTokens.Any(entitlementTokens.Contains)) return true;

        return itemTokens.Any(token =>
            entitlementTokens.Contains($"integration:{token}") ||
            entitlementTokens.Contains($"plugin:{token}") ||
            entitlementTokens.Contains($"addon:{token}"));
    }

    private static IEnumerable<string> ExpandIntegrationEntitlementTokens(string? raw)
    {
        var original = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(original))
            yield return original;

        var normalized = NormalizeIntegrationToken(raw);
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        yield return normalized;

        if (normalized.Contains('-'))
            yield return normalized.Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalized.Contains('_'))
            yield return normalized.Replace("_", string.Empty, StringComparison.Ordinal);

        if (normalized is "google-authenticator" or "googleauthenticator")
            yield return "google_authenticator";
        else if (normalized is "google_authenticator")
        {
            yield return "google-authenticator";
            yield return "googleauthenticator";
        }

        if (normalized is "microsoft-authenticator" or "microsoftauthenticator")
            yield return "microsoft_authenticator";
        else if (normalized is "microsoft_authenticator")
        {
            yield return "microsoft-authenticator";
            yield return "microsoftauthenticator";
        }
    }

    private static string NormalizeIntegrationToken(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Replace("&", " and ", StringComparison.Ordinal);
        value = Regex.Replace(value, @"[^a-z0-9]+", "-").Trim('-');
        return value;
    }

    private static List<IntegrationCatalogController.IntegrationCatalogItem> DefaultIntegrationCatalog() =>
    [
        new() { Slug = "shopify", Name = "Shopify", Category = "e-commerce", Description = "Sync orders and automate status updates.", PricingType = "paid", BillingFrequency = "monthly", Price = 999m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 1 },
        new() { Slug = "woocommerce", Name = "WooCommerce", Category = "e-commerce", Description = "WordPress store messaging and order sync.", PricingType = "paid", BillingFrequency = "monthly", Price = 799m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 2 },
        new() { Slug = "razorpay", Name = "Razorpay", Category = "payments", Description = "Payment collection events and invoice updates.", PricingType = "free", BillingFrequency = "monthly", Price = 0m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 3 },
        new() { Slug = "zapier", Name = "Zapier", Category = "automation", Description = "Bridge Textzy with external tools.", PricingType = "paid", BillingFrequency = "monthly", Price = 1499m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 4 },
        new() { Slug = "google-authenticator", Name = "Google Authenticator", Category = "security", Description = "QR-based TOTP for secure account sign-in.", PricingType = "free", BillingFrequency = "monthly", Price = 0m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 5 },
        new() { Slug = "microsoft-authenticator", Name = "Microsoft Authenticator", Category = "security", Description = "QR-based TOTP enrollment for Microsoft Authenticator.", PricingType = "free", BillingFrequency = "monthly", Price = 0m, Currency = "INR", TaxMode = "exclusive", IsActive = true, IsVisible = true, SortOrder = 6 }
    ];

    private static string ComputeInvoiceIntegrityHash(BillingInvoice invoice) => InvoiceIntegrityHasher.Compute(invoice);

    private static string ComputeInvoiceIntegrityHashLegacy(BillingInvoice invoice) => InvoiceIntegrityHasher.ComputeLegacy(invoice);

    private static string? NormalizeBillingCycle(string? billingCycle)
        => BillingComputation.NormalizeBillingCycle(billingCycle);

    private static DateTime ResolveRenewAtUtc(DateTime startUtc, string cycle)
        => BillingComputation.ResolveRenewAtUtc(startUtc, cycle);

    private static DateTime ResolvePeriodEndUtc(DateTime periodStartUtc, string cycle)
        => BillingComputation.ResolvePeriodEndUtc(periodStartUtc, cycle);


    private static int ResolveGraceDaysForApi(IConfiguration config)
    {
        var raw = (config["Billing:GraceDays"] ?? config["BILLING_GRACE_DAYS"] ?? "7").Trim();
        if (int.TryParse(raw, out var days)) return Math.Clamp(days, 0, 60);
        return 7;
    }

    private static HashSet<int> ResolveDayOffsetsForApi(string? raw, IEnumerable<int> defaults)
    {
        var set = new HashSet<int>();
        var source = string.IsNullOrWhiteSpace(raw) ? string.Join(",", defaults) : raw;
        foreach (var token in source.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, out var day))
                set.Add(Math.Clamp(day, 0, 60));
        }
        if (set.Count == 0)
        {
            foreach (var d in defaults) set.Add(Math.Clamp(d, 0, 60));
        }
        return set;
    }

    private async Task ActivatePurchaseFromPaymentAsync(BillingPaymentAttempt attempt, CancellationToken ct)
    {
        var notes = ParseAttemptNotes(attempt.NotesJson);
        var purchaseType = notes.TryGetValue("purchaseType", out var type) ? type : "plan";
        if (string.Equals(purchaseType, "integration", StringComparison.OrdinalIgnoreCase))
        {
            await ActivateIntegrationPurchaseAsync(attempt, notes, ct);
            return;
        }

        await ActivateSubscriptionFromPaymentAsync(attempt, ct);
    }

    private async Task ActivateSubscriptionFromPaymentAsync(BillingPaymentAttempt attempt, CancellationToken ct)
    {
        var plan = await db.BillingPlans.FirstOrDefaultAsync(x => x.Id == attempt.PlanId, ct);
        if (plan is null) return;

        var start = DateTime.UtcNow;
        var periodStart = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var billingCycle = NormalizeBillingCycle(attempt.BillingCycle) ?? "monthly";
        var periodEnd = ResolvePeriodEndUtc(periodStart, billingCycle);
        var invoiceNo = $"INV-{start:yyyyMMdd}-{attempt.OrderId}";
        var existingInvoice = await db.BillingInvoices
            .FirstOrDefaultAsync(x => x.TenantId == attempt.TenantId && x.InvoiceNo == invoiceNo, ct);
        if (existingInvoice is null)
        {
            var (taxRate, isTaxExempt, isReverseCharge) = await ResolveTaxProfileAsync(attempt.TenantId, ct);
            var invoiceBreakdown = ComputeInvoiceAmounts(attempt.Amount, taxRate, plan.TaxMode, isTaxExempt, isReverseCharge, amountIsGross: true);
            var invoice = new BillingInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNo = invoiceNo,
                TenantId = attempt.TenantId,
                InvoiceKind = "tax_invoice",
                BillingCycle = billingCycle,
                TaxMode = plan.TaxMode,
                ReferenceNo = attempt.OrderId,
                Description = ResolveInvoiceDescription(plan.Name, billingCycle, plan.PricingModel),
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                Subtotal = invoiceBreakdown.Subtotal,
                TaxAmount = invoiceBreakdown.TaxAmount,
                Total = invoiceBreakdown.Total,
                Status = "paid",
                PaidAtUtc = attempt.PaidAtUtc ?? DateTime.UtcNow,
                PdfUrl = string.Empty,
                IntegrityAlgo = "SHA256",
                IssuedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            invoice.IntegrityHash = ComputeInvoiceIntegrityHash(invoice);
            db.BillingInvoices.Add(invoice);
            await TrySendBillingEventAsync(
                attempt.TenantId,
                "Invoice generated",
                "A paid tax invoice has been generated for your purchase.",
                new Dictionary<string, string>
                {
                    ["Invoice No"] = invoiceNo,
                    ["Service"] = invoice.Description,
                    ["Period"] = $"{periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}",
                    ["Subtotal"] = FormatCurrency(invoiceBreakdown.Subtotal, attempt.Currency),
                    ["Tax"] = FormatCurrency(invoiceBreakdown.TaxAmount, attempt.Currency),
                    ["Tax Rate"] = $"{taxRate:0.##}%",
                    ["Total"] = FormatCurrency(invoiceBreakdown.Total, attempt.Currency),
                    ["Invoice Type"] = "Tax Invoice"
                },
                ct,
                invoice);
        }

        if (string.Equals(plan.PricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
        {
            var units = plan.IncludedQuantity > 0 ? plan.IncludedQuantity : ResolvePackUnitsFromLimits(plan.LimitsJson, plan.UsageUnitName);
            var metricKey = string.IsNullOrWhiteSpace(plan.UsageUnitName) ? "smsCredits" : plan.UsageUnitName.Trim();
            await billingGuard.AddCreditUnitsAsync(attempt.TenantId, metricKey, units, ct);
            return;
        }

        var sub = await db.TenantSubscriptions
            .Where(x => x.TenantId == attempt.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (sub is null)
        {
            sub = new TenantSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = attempt.TenantId,
                PlanId = attempt.PlanId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.TenantSubscriptions.Add(sub);
        }

        sub.PlanId = plan.Id;
        sub.BillingCycle = billingCycle;
        sub.Status = "active";
        sub.CancelledAtUtc = null;
        sub.StartedAtUtc = start;
        sub.RenewAtUtc = ResolveRenewAtUtc(start, sub.BillingCycle);
        sub.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task ActivateIntegrationPurchaseAsync(BillingPaymentAttempt attempt, Dictionary<string, string> notes, CancellationToken ct)
    {
        var slug = notes.TryGetValue("integrationSlug", out var integrationSlug) ? integrationSlug.Trim().ToLowerInvariant() : string.Empty;
        if (string.IsNullOrWhiteSpace(slug)) return;

        var name = notes.TryGetValue("integrationName", out var integrationName) && !string.IsNullOrWhiteSpace(integrationName)
            ? integrationName.Trim()
            : slug;
        var billingCycle = notes.TryGetValue("billingCycle", out var cycleValue) && !string.IsNullOrWhiteSpace(cycleValue)
            ? cycleValue.Trim().ToLowerInvariant()
            : "monthly";
        var taxMode = notes.TryGetValue("taxMode", out var taxModeValue) && !string.IsNullOrWhiteSpace(taxModeValue)
            ? taxModeValue.Trim().ToLowerInvariant()
            : "exclusive";

        var start = DateTime.UtcNow;
        var periodStart = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = ResolveAddOnPeriodEndUtc(periodStart, billingCycle);
        var invoiceNo = $"INV-ADD-{start:yyyyMMdd}-{attempt.OrderId}";
        var existingInvoice = await db.BillingInvoices
            .FirstOrDefaultAsync(x => x.TenantId == attempt.TenantId && x.InvoiceNo == invoiceNo, ct);
        if (existingInvoice is null)
        {
            var (taxRate, isTaxExempt, isReverseCharge) = await ResolveTaxProfileAsync(attempt.TenantId, ct);
            var invoiceBreakdown = ComputeInvoiceAmounts(attempt.Amount, taxRate, taxMode, isTaxExempt, isReverseCharge, amountIsGross: true);
            var invoice = new BillingInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNo = invoiceNo,
                TenantId = attempt.TenantId,
                InvoiceKind = "tax_invoice",
                BillingCycle = billingCycle,
                TaxMode = taxMode,
                ReferenceNo = attempt.OrderId,
                Description = $"{name} purchase",
                PeriodStartUtc = periodStart,
                PeriodEndUtc = periodEnd,
                Subtotal = invoiceBreakdown.Subtotal,
                TaxAmount = invoiceBreakdown.TaxAmount,
                Total = invoiceBreakdown.Total,
                Status = "paid",
                PaidAtUtc = attempt.PaidAtUtc ?? DateTime.UtcNow,
                PdfUrl = string.Empty,
                IntegrityAlgo = "SHA256",
                IssuedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            invoice.IntegrityHash = ComputeInvoiceIntegrityHash(invoice);
            db.BillingInvoices.Add(invoice);
            await TrySendBillingEventAsync(
                attempt.TenantId,
                "Integration invoice generated",
                "A paid tax invoice has been generated for your integration purchase.",
                new Dictionary<string, string>
                {
                    ["Invoice No"] = invoiceNo,
                    ["Service"] = invoice.Description,
                    ["Period"] = $"{periodStart:yyyy-MM-dd} to {periodEnd:yyyy-MM-dd}",
                    ["Total"] = FormatCurrency(invoiceBreakdown.Total, attempt.Currency),
                    ["Invoice Type"] = "Tax Invoice"
                },
                ct,
                invoice);
        }

        var featureKey = $"integration:{slug}";
        var flag = await db.TenantFeatureFlags.FirstOrDefaultAsync(x => x.TenantId == attempt.TenantId && x.FeatureKey == featureKey, ct);
        if (flag is null)
        {
            flag = new TenantFeatureFlag
            {
                Id = Guid.NewGuid(),
                TenantId = attempt.TenantId,
                FeatureKey = featureKey
            };
            db.TenantFeatureFlags.Add(flag);
        }

        flag.IsEnabled = true;
        flag.UpdatedAtUtc = DateTime.UtcNow;
        flag.UpdatedByUserId = auth.UserId;
    }

    private async Task TrySendBillingEventAsync(Guid tenantId, string title, string description, Dictionary<string, string> details, CancellationToken ct, BillingInvoice? invoice = null)
    {
        try
        {
            var recipient = await ResolveBillingRecipientAsync(tenantId, ct);
            if (string.IsNullOrWhiteSpace(recipient.email)) return;
            var attachments = invoice is null
                ? null
                : new[] { await invoiceAttachmentService.BuildPdfAttachmentAsync(invoice, Request, ct) };
            await emailService.SendBillingEventAsync(recipient.email, recipient.name, recipient.companyName, title, description, details, ct, attachments);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Non-blocking billing email failed tenant={TenantId}: {Error}", tenantId, redactor.RedactText(ex.Message));
        }
    }

    private async Task<(string email, string name, string companyName)> ResolveBillingRecipientAsync(Guid tenantId, CancellationToken ct)
    {
        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        var companyName = string.IsNullOrWhiteSpace(profile?.CompanyName) ? (tenant?.Name ?? "Textzy Workspace") : profile!.CompanyName;

        if (!string.IsNullOrWhiteSpace(profile?.BillingEmail))
            return (profile.BillingEmail.Trim(), profile.CompanyName, companyName);

        var ownerUserId = await db.TenantUsers.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Role.ToLower() == "owner")
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId != Guid.Empty)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ownerUserId, ct);
            if (!string.IsNullOrWhiteSpace(owner?.Email))
                return (owner.Email.Trim(), owner.FullName, companyName);
        }

        if (!string.IsNullOrWhiteSpace(auth.Email))
            return (auth.Email.Trim(), string.IsNullOrWhiteSpace(auth.FullName) ? auth.Email : auth.FullName, companyName);

        return (string.Empty, string.Empty, companyName);
    }

    private static string FormatCurrency(decimal amount, string currency)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? "INR" : currency.Trim().ToUpperInvariant();
        return $"{(code == "INR" ? "INR " : code + " ")}{amount:0.00}";
    }

    private static string ResolveInvoiceDescription(string? planName, string? billingCycle, string? pricingModel)
        => BillingComputation.ResolveInvoiceDescription(planName, billingCycle, pricingModel);

    private static string BuildRazorpayReceipt(string kind, Guid tenantId)
        => BillingComputation.BuildRazorpayReceipt(kind, tenantId);

    private static Dictionary<string, string> ParseAttemptNotes(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static DateTime ResolveAddOnPeriodEndUtc(DateTime periodStartUtc, string billingCycle)
        => BillingComputation.ResolveAddOnPeriodEndUtc(periodStartUtc, billingCycle);

    private static string Csv(string value)
    {
        var v = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{v}\"";
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
        BillingInvoice inv,
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
        var safeInvoiceNo = WebUtility.HtmlEncode(inv.InvoiceNo);
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
        var safeReference = WebUtility.HtmlEncode(inv.ReferenceNo);
        var safeDescription = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(inv.Description) ? "Platform service purchase" : inv.Description);
        var invoiceLabel = string.Equals(inv.InvoiceKind, "proforma_invoice", StringComparison.OrdinalIgnoreCase) ? "Proforma Invoice" : "Tax Invoice";
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
        var safeBillingCycle = WebUtility.HtmlEncode((inv.BillingCycle ?? string.Empty).Replace("_", " "));
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
                        <div><span class="key">Invoice Date</span><br />{{inv.IssuedAtUtc:yyyy-MM-dd}}</div>
                        <div><span class="key">Status</span><br />{{inv.Status}}</div>
                        <div><span class="key">Paid Date</span><br />{{(inv.PaidAtUtc ?? inv.IssuedAtUtc):yyyy-MM-dd}}</div>
                        <div><span class="key">Reference</span><br />{{(string.IsNullOrWhiteSpace(inv.ReferenceNo) ? "-" : safeReference)}}</div>
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
                      <div class="muted">Service Period: {{inv.PeriodStartUtc:yyyy-MM-dd}} to {{inv.PeriodEndUtc:yyyy-MM-dd}}</div>
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
                            <span class="muted">Reference: {{(string.IsNullOrWhiteSpace(inv.ReferenceNo) ? "-" : safeReference)}} | Cycle: {{safeBillingCycle}}</span>
                          </td>
                          <td>998314</td>
                          <td class="right">{{inv.Subtotal:0.00}}</td>
                        </tr>
                        <tr class="summary">
                          <td colspan="3">{{safeTaxLineLabel}}</td>
                          <td class="right">{{inv.TaxAmount:0.00}}</td>
                        </tr>
                        <tr class="total">
                          <td colspan="3">Invoice Total</td>
                          <td class="right">{{inv.Total:0.00}}</td>
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
                      <div>Integrity: {{inv.IntegrityAlgo}} / {{inv.IntegrityHash}}</div>
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
}

