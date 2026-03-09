using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/customers")]
public class PlatformCustomersController(
    ControlDbContext db,
    AuthContext auth,
    RbacService rbac,
    AuditLogService audit,
    SecretCryptoService crypto) : ControllerBase
{
    private const string TenantSmsGatewayReportFeatureKey = "tenant.smsGatewayReport.enabled";
    private static readonly TimeSpan StepUpFreshWindow = TimeSpan.FromMinutes(10);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string q = "", CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var query = db.Tenants.AsQueryable();
        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t => t.Name.ToLower().Contains(search) || t.Slug.ToLower().Contains(search));
        }

        var tenants = await query.OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct);
        var tenantIds = tenants.Select(t => t.Id).ToList();

        var users = await db.TenantUsers.Where(x => tenantIds.Contains(x.TenantId)).ToListAsync(ct);
        var userIds = users.Select(x => x.UserId).Distinct().ToList();
        var userMap = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var companyProfiles = await db.TenantCompanyProfiles.Where(x => tenantIds.Contains(x.TenantId)).ToDictionaryAsync(x => x.TenantId, ct);
        var subs = await db.TenantSubscriptions.Where(s => tenantIds.Contains(s.TenantId)).ToListAsync(ct);
        var plans = await db.BillingPlans.ToListAsync(ct);
        var planMap = plans.ToDictionary(x => x.Id, x => x);
        var invoices = await db.BillingInvoices.Where(i => tenantIds.Contains(i.TenantId)).ToListAsync(ct);

        var result = tenants.Select(t =>
        {
            var members = users.Where(u => u.TenantId == t.Id).ToList();
            var owner = members
                .OrderBy(m => RolePriority(m.Role))
                .Select(m => userMap.TryGetValue(m.UserId, out var u) ? u : null)
                .FirstOrDefault(u => u is not null);
            var sub = subs.Where(x => x.TenantId == t.Id).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
            var plan = sub is not null && planMap.TryGetValue(sub.PlanId, out var p) ? p : null;
            var inv = invoices.Where(i => i.TenantId == t.Id).ToList();
            var revenue = inv.Sum(i => i.Total);
            return new
            {
                tenantId = t.Id,
                tenantName = t.Name,
                tenantSlug = t.Slug,
                companyName = companyProfiles.TryGetValue(t.Id, out var company) ? company.CompanyName : t.Name,
                ownerGroupId = t.OwnerGroupId,
                createdAtUtc = t.CreatedAtUtc,
                ownerName = owner?.FullName ?? owner?.Email ?? "-",
                ownerEmail = owner?.Email ?? "-",
                users = members.Count,
                activeUsers = members.Count(m => userMap.TryGetValue(m.UserId, out var u) && u.IsActive),
                planCode = plan?.Code ?? "",
                planName = plan?.Name ?? "No Plan",
                subscriptionStatus = sub?.Status ?? "none",
                monthlyPrice = plan?.PriceMonthly ?? 0,
                invoiceCount = inv.Count,
                totalRevenue = revenue
            };
        });

        return Ok(result);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string q = "", CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        var query = db.Users.Where(u => !u.IsSuperAdmin);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Email.ToLower().Contains(search) || u.FullName.ToLower().Contains(search));
        }

        var users = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Take(500)
            .ToListAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();
        var memberships = await db.TenantUsers.Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);

        var rows = users.Select(u =>
        {
            var m = memberships.Where(x => x.UserId == u.Id).ToList();
            return new
            {
                userId = u.Id,
                name = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName,
                email = u.Email,
                isActive = u.IsActive,
                tenantCount = m.Select(x => x.TenantId).Distinct().Count(),
                rolePreview = string.Join(", ", m.Select(x => x.Role).Distinct().OrderBy(x => x))
            };
        });

        return Ok(rows);
    }

    [HttpGet("user-tenants")]
    public async Task<IActionResult> UserTenants([FromQuery] Guid userId, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (userId == Guid.Empty) return BadRequest("userId is required.");

        var user = await db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return NotFound("User not found.");

        var memberships = await db.TenantUsers.Where(x => x.UserId == userId).ToListAsync(ct);
        var tenantIds = memberships.Select(x => x.TenantId).Distinct().ToList();
        var tenants = await db.Tenants.Where(x => tenantIds.Contains(x.Id)).ToListAsync(ct);
        var profiles = await db.TenantCompanyProfiles.Where(x => tenantIds.Contains(x.TenantId)).ToDictionaryAsync(x => x.TenantId, ct);

        var latestSubs = await db.TenantSubscriptions
            .Where(x => tenantIds.Contains(x.TenantId))
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.CreatedAtUtc).First())
            .ToListAsync(ct);
        var planIds = latestSubs.Select(x => x.PlanId).Distinct().ToList();
        var planMap = await db.BillingPlans.Where(x => planIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var grouped = tenants.GroupBy(t => t.OwnerGroupId).Select(g => new
        {
            ownerGroupId = g.Key,
            companies = g.Select(t =>
            {
                var sub = latestSubs.FirstOrDefault(x => x.TenantId == t.Id);
                var plan = sub is not null && planMap.TryGetValue(sub.PlanId, out var p) ? p : null;
                var role = memberships.FirstOrDefault(m => m.TenantId == t.Id)?.Role ?? "member";
                var status = (sub?.Status ?? "inactive").ToLowerInvariant();
                return new
                {
                    tenantId = t.Id,
                    tenantName = t.Name,
                    tenantSlug = t.Slug,
                    role,
                    isActive = status == "active" || status == "trialing" || status == "trial",
                    companyName = profiles.TryGetValue(t.Id, out var cp) && !string.IsNullOrWhiteSpace(cp.CompanyName) ? cp.CompanyName : t.Name,
                    billingStatus = sub?.Status ?? "none",
                    planCode = plan?.Code ?? "",
                    planName = plan?.Name ?? "No Plan",
                    planCycle = sub?.BillingCycle ?? "",
                    renewAtUtc = sub?.RenewAtUtc
                };
            }).OrderBy(x => x.companyName).ToList()
        }).ToList();

        return Ok(new
        {
            user = new
            {
                userId = user.Id,
                name = string.IsNullOrWhiteSpace(user.FullName) ? user.Email : user.FullName,
                user.Email,
                user.IsActive
            },
            ownerGroupCount = grouped.Count,
            tenantCount = tenants.Count,
            groups = grouped
        });
    }

    [HttpGet("{tenantId:guid}")]
    public async Task<IActionResult> Details(Guid tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var members = await db.TenantUsers.Where(x => x.TenantId == tenantId).ToListAsync(ct);
        var userIds = members.Select(x => x.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToListAsync(ct);
        var company = await db.TenantCompanyProfiles.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var latestSub = await db.TenantSubscriptions.Where(x => x.TenantId == tenantId).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var plan = latestSub is null ? null : await db.BillingPlans.FirstOrDefaultAsync(x => x.Id == latestSub.PlanId, ct);

        return Ok(new
        {
            tenant = new { tenant.Id, tenant.Name, tenant.Slug, tenant.CreatedAtUtc },
            company = company is null ? null : new
            {
                company.TenantId,
                company.OwnerGroupId,
                company.CompanyName,
                company.LegalName,
                company.Industry,
                company.Website,
                company.CompanySize,
                company.Gstin,
                company.Pan,
                company.Address,
                company.BillingEmail,
                company.BillingPhone,
                company.IsActive,
                company.UpdatedAtUtc
            },
            subscription = latestSub is null ? null : new
            {
                latestSub.Id,
                latestSub.Status,
                latestSub.BillingCycle,
                latestSub.StartedAtUtc,
                latestSub.RenewAtUtc,
                latestSub.CancelledAtUtc,
                plan = plan is null ? null : new
                {
                    plan.Id,
                    plan.Code,
                    plan.Name,
                    plan.PriceMonthly,
                    plan.PriceYearly,
                    plan.Currency,
                    features = ParseStringList(plan.FeaturesJson),
                    limits = ParseLimits(plan.LimitsJson)
                }
            },
            members = members.Select(m =>
            {
                var user = users.FirstOrDefault(u => u.Id == m.UserId);
                return new
                {
                    userId = m.UserId,
                    role = m.Role,
                    joinedAtUtc = m.CreatedAtUtc,
                    name = user?.FullName ?? user?.Email ?? "-",
                    email = user?.Email ?? "-",
                    isActive = user?.IsActive ?? false
                };
            }).OrderBy(x => x.role).ThenBy(x => x.name)
        });
    }

    [HttpGet("{tenantId:guid}/usage")]
    public async Task<IActionResult> Usage(Guid tenantId, [FromQuery] string month = "", CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var monthKey = string.IsNullOrWhiteSpace(month) ? DateTime.UtcNow.ToString("yyyy-MM") : month.Trim();
        var usage = await db.TenantUsages
            .Where(x => x.TenantId == tenantId && x.MonthKey == monthKey)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (usage is null) return Ok(new { tenantId, monthKey, values = new Dictionary<string, int>() });

        return Ok(new
        {
            tenantId,
            monthKey,
            usage.UpdatedAtUtc,
            values = new Dictionary<string, int>
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

    [HttpGet("{tenantId:guid}/features")]
    public async Task<IActionResult> GetTenantFeatures(Guid tenantId, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var tenantExists = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct);
        if (!tenantExists) return NotFound("Tenant not found.");

        var smsGatewayReportEnabled = await db.TenantFeatureFlags.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.FeatureKey == TenantSmsGatewayReportFeatureKey)
            .Select(x => x.IsEnabled)
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            tenantId,
            smsGatewayReportEnabled
        });
    }

    [HttpGet("{tenantId:guid}/company-settings")]
    public async Task<IActionResult> GetTenantCompanySettings(Guid tenantId, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var profile = await db.TenantCompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (profile is null)
        {
            return Ok(new
            {
                tenantId,
                tenantName = tenant.Name,
                tenantSlug = tenant.Slug,
                companyName = tenant.Name,
                legalName = tenant.Name,
                billingEmail = string.Empty,
                billingPhone = string.Empty,
                publicApiEnabled = false,
                apiUsername = string.Empty,
                apiPassword = string.Empty,
                apiKey = string.Empty,
                apiIpWhitelist = string.Empty,
                taxRatePercent = 18m,
                isTaxExempt = false,
                isReverseCharge = false,
                updatedAtUtc = (DateTime?)null
            });
        }

        return Ok(new
        {
            tenantId,
            tenantName = tenant.Name,
            tenantSlug = tenant.Slug,
            companyName = profile.CompanyName,
            legalName = profile.LegalName,
            billingEmail = profile.BillingEmail,
            billingPhone = profile.BillingPhone,
            publicApiEnabled = profile.PublicApiEnabled,
            apiUsername = profile.ApiUsername,
            apiPassword = crypto.Decrypt(profile.ApiPasswordEncrypted),
            apiKey = crypto.Decrypt(profile.ApiKeyEncrypted),
            apiIpWhitelist = profile.ApiIpWhitelist,
            taxRatePercent = profile.TaxRatePercent,
            isTaxExempt = profile.IsTaxExempt,
            isReverseCharge = profile.IsReverseCharge,
            updatedAtUtc = profile.UpdatedAtUtc
        });
    }

    [HttpPut("{tenantId:guid}/company-settings")]
    public async Task<IActionResult> UpsertTenantCompanySettings(Guid tenantId, [FromBody] TenantCompanySettingsRequest request, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if ((request.PublicApiEnabled || !string.IsNullOrWhiteSpace(request.ApiUsername) || !string.IsNullOrWhiteSpace(request.ApiPassword) || !string.IsNullOrWhiteSpace(request.ApiKey))
            && !HasFreshStepUp())
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action = "api_credentials_write",
                title = "Verify API credential update",
                message = "Enter your authenticator code to change tenant API credentials."
            });
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var now = DateTime.UtcNow;
        var profile = await db.TenantCompanyProfiles.FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (profile is null)
        {
            profile = new TenantCompanyProfile
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                OwnerGroupId = tenant.OwnerGroupId,
                CompanyName = tenant.Name,
                LegalName = tenant.Name,
                CreatedAtUtc = now,
            };
            db.TenantCompanyProfiles.Add(profile);
        }

        profile.BillingEmail = InputGuardService.ValidateEmailOrEmpty(request.BillingEmail, "Billing email");
        profile.BillingPhone = (request.BillingPhone ?? string.Empty).Trim();
        profile.PublicApiEnabled = request.PublicApiEnabled;
        profile.ApiUsername = (request.ApiUsername ?? string.Empty).Trim();
        profile.ApiPasswordEncrypted = crypto.Encrypt((request.ApiPassword ?? string.Empty).Trim());
        profile.ApiKeyEncrypted = crypto.Encrypt((request.ApiKey ?? string.Empty).Trim());
        profile.ApiIpWhitelist = (request.ApiIpWhitelist ?? string.Empty).Trim();
        profile.TaxRatePercent = Math.Clamp(request.TaxRatePercent, 0m, 100m);
        profile.IsTaxExempt = request.IsTaxExempt;
        profile.IsReverseCharge = request.IsReverseCharge;
        profile.UpdatedAtUtc = now;

        if (profile.PublicApiEnabled)
        {
            if (string.IsNullOrWhiteSpace(profile.ApiUsername)) return BadRequest("API username is required when public API is enabled.");
            if (string.IsNullOrWhiteSpace(request.ApiPassword)) return BadRequest("API password is required when public API is enabled.");
            if (string.IsNullOrWhiteSpace(request.ApiKey)) return BadRequest("API key is required when public API is enabled.");
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(
            "platform.customer.company_settings.updated",
            $"tenant={tenantId}; taxRate={profile.TaxRatePercent}; exempt={profile.IsTaxExempt}; reverseCharge={profile.IsReverseCharge}",
            ct);

        return Ok(new
        {
            tenantId,
            tenantName = tenant.Name,
            tenantSlug = tenant.Slug,
            companyName = profile.CompanyName,
            legalName = profile.LegalName,
            billingEmail = profile.BillingEmail,
            billingPhone = profile.BillingPhone,
            publicApiEnabled = profile.PublicApiEnabled,
            apiUsername = profile.ApiUsername,
            apiPassword = crypto.Decrypt(profile.ApiPasswordEncrypted),
            apiKey = crypto.Decrypt(profile.ApiKeyEncrypted),
            apiIpWhitelist = profile.ApiIpWhitelist,
            taxRatePercent = profile.TaxRatePercent,
            isTaxExempt = profile.IsTaxExempt,
            isReverseCharge = profile.IsReverseCharge,
            updatedAtUtc = profile.UpdatedAtUtc
        });
    }

    [HttpPut("{tenantId:guid}/features")]
    public async Task<IActionResult> UpsertTenantFeatures(Guid tenantId, [FromBody] TenantFeaturesRequest request, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var tenantExists = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct);
        if (!tenantExists) return NotFound("Tenant not found.");

        var row = await db.TenantFeatureFlags
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FeatureKey == TenantSmsGatewayReportFeatureKey, ct);
        if (row is null)
        {
            row = new TenantFeatureFlag
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FeatureKey = TenantSmsGatewayReportFeatureKey
            };
            db.TenantFeatureFlags.Add(row);
        }

        row.IsEnabled = request.SmsGatewayReportEnabled;
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedByUserId = auth.UserId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.customer.features.updated", $"tenant={tenantId}; smsGatewayReportEnabled={row.IsEnabled}", ct);

        return Ok(new
        {
            tenantId,
            smsGatewayReportEnabled = row.IsEnabled
        });
    }

    [HttpGet("{tenantId:guid}/subscriptions")]
    public async Task<IActionResult> Subscriptions(Guid tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var rows = await db.TenantSubscriptions
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var planIds = rows.Select(x => x.PlanId).Distinct().ToList();
        var plans = await db.BillingPlans.Where(x => planIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        return Ok(rows.Select(x => new
        {
            x.Id,
            x.TenantId,
            x.Status,
            x.BillingCycle,
            x.StartedAtUtc,
            x.RenewAtUtc,
            x.CancelledAtUtc,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            plan = plans.TryGetValue(x.PlanId, out var p) ? new { p.Id, p.Code, p.Name, p.PriceMonthly, p.PriceYearly, p.Currency } : null
        }));
    }

    [HttpGet("{tenantId:guid}/invoices")]
    public async Task<IActionResult> Invoices(Guid tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var rows = await db.BillingInvoices
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);
        return Ok(rows.Select(x => new
        {
            x.Id,
            x.InvoiceNo,
            x.PeriodStartUtc,
            x.PeriodEndUtc,
            x.Subtotal,
            x.TaxAmount,
            x.Total,
            x.Status,
            x.PaidAtUtc,
            x.PdfUrl,
            x.CreatedAtUtc
        }));
    }

    [HttpGet("{tenantId:guid}/members")]
    public async Task<IActionResult> Members(Guid tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var rows = await db.TenantUsers
            .Where(x => x.TenantId == tenantId)
            .Join(db.Users, tu => tu.UserId, u => u.Id, (tu, u) => new
            {
                userId = u.Id,
                u.FullName,
                u.Email,
                u.IsActive,
                tu.Role,
                tu.CreatedAtUtc
            })
            .OrderBy(x => x.Role)
            .ThenBy(x => x.FullName)
            .ToListAsync(ct);
        return Ok(rows.Select(x => new
        {
            x.userId,
            name = string.IsNullOrWhiteSpace(x.FullName) ? x.Email : x.FullName,
            x.Email,
            x.IsActive,
            x.Role,
            joinedAtUtc = x.CreatedAtUtc
        }));
    }

    [HttpGet("{tenantId:guid}/activity")]
    public async Task<IActionResult> Activity(Guid tenantId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        var safeLimit = Math.Clamp(limit, 1, 500);
        var rows = await db.AuditLogs
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(safeLimit)
            .ToListAsync(ct);
        return Ok(rows.Select(x => new
        {
            x.Id,
            x.ActorUserId,
            x.Action,
            x.Details,
            x.IpAddress,
            x.UserAgent,
            x.DeviceLabel,
            x.CreatedAtUtc
        }));
    }

    [HttpPost("{tenantId:guid}/assign-plan")]
    public async Task<IActionResult> AssignPlan(Guid tenantId, [FromBody] AssignPlanRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");

        var planCode = (request.PlanCode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(planCode)) return BadRequest("planCode is required.");
        var plan = await db.BillingPlans.FirstOrDefaultAsync(x => x.Code == planCode, ct);
        if (plan is null) return NotFound("Plan not found.");

        var cycle = string.IsNullOrWhiteSpace(request.BillingCycle) ? "monthly" : request.BillingCycle.Trim().ToLowerInvariant();
        if (cycle != "monthly" && cycle != "yearly" && cycle != "lifetime" && cycle != "usage_based")
            return BadRequest("billingCycle must be monthly, yearly, lifetime, or usage_based.");
        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status.Trim().ToLowerInvariant();
        if (status is not ("active" or "trial" or "trialing" or "suspended" or "cancelled"))
            return BadRequest("status must be active, trial, suspended, or cancelled.");
        var trialDays = Math.Clamp(request.TrialDays, 0, 365);

        var sub = await db.TenantSubscriptions
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (sub is null)
        {
            sub = new TenantSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Status = status,
                BillingCycle = cycle,
                StartedAtUtc = DateTime.UtcNow,
                RenewAtUtc = ResolveRenewAtUtc(cycle, status, trialDays),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TenantSubscriptions.Add(sub);
        }
        else
        {
            sub.PlanId = plan.Id;
            sub.Status = status;
            sub.BillingCycle = cycle;
            if (request.ResetStartDate) sub.StartedAtUtc = DateTime.UtcNow;
            sub.RenewAtUtc = ResolveRenewAtUtc(cycle, status, trialDays);
            sub.CancelledAtUtc = null;
            sub.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.customer.assign_plan", $"tenant={tenantId}; plan={plan.Code}; cycle={cycle}; status={status}; trialDays={trialDays}", ct);

        return Ok(new
        {
            assigned = true,
            tenantId,
            plan = new { plan.Id, plan.Code, plan.Name },
            subscription = new
            {
                sub.Id,
                sub.Status,
                sub.BillingCycle,
                sub.StartedAtUtc,
                sub.RenewAtUtc,
                sub.CancelledAtUtc,
                sub.UpdatedAtUtc
            }
        });
    }

    private static int RolePriority(string role)
    {
        var r = (role ?? string.Empty).ToLowerInvariant();
        return r switch
        {
            "owner" => 0,
            "admin" => 1,
            "manager" => 2,
            "support" => 3,
            "marketing" => 4,
            "finance" => 5,
            _ => 99
        };
    }

    private static List<string> ParseStringList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; }
    }

    private static Dictionary<string, int> ParseLimits(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new(); } catch { return new(); }
    }

    public sealed class AssignPlanRequest
    {
        public string PlanCode { get; set; } = string.Empty;
        public string BillingCycle { get; set; } = "monthly";
        public string Status { get; set; } = "active";
        public bool ResetStartDate { get; set; } = true;
        public int TrialDays { get; set; } = 14;
    }

    private static DateTime ResolveRenewAtUtc(string cycle, string status, int trialDays)
    {
        if (status is "trial" or "trialing")
            return DateTime.UtcNow.AddDays(trialDays > 0 ? trialDays : 14);

        return cycle switch
        {
            "yearly" => DateTime.UtcNow.AddYears(1),
            "monthly" => DateTime.UtcNow.AddMonths(1),
            "usage_based" => DateTime.MaxValue,
            _ => DateTime.MaxValue
        };
    }

    public sealed class TenantFeaturesRequest
    {
        public bool SmsGatewayReportEnabled { get; set; }
    }

    public sealed class TenantCompanySettingsRequest
    {
        public string BillingEmail { get; set; } = string.Empty;
        public string BillingPhone { get; set; } = string.Empty;
        public bool PublicApiEnabled { get; set; }
        public string ApiUsername { get; set; } = string.Empty;
        public string ApiPassword { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiIpWhitelist { get; set; } = string.Empty;
        public decimal TaxRatePercent { get; set; } = 18m;
        public bool IsTaxExempt { get; set; }
        public bool IsReverseCharge { get; set; }
    }

    private bool HasFreshStepUp()
        => auth.StepUpVerifiedAtUtc.HasValue && auth.StepUpVerifiedAtUtc.Value >= DateTime.UtcNow.Subtract(StepUpFreshWindow);
}
