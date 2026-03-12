using System.Security.Cryptography;
using System.Text;
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
    public async Task<IActionResult> List(
        [FromQuery] string q = "",
        [FromQuery] Guid? ownerUserId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        var safePageSize = Math.Clamp(pageSize, 10, 200);
        var requestedPage = Math.Max(1, page);

        IQueryable<Tenant> tenantQuery = db.Tenants.AsNoTracking();
        if (ownerUserId.HasValue && ownerUserId.Value != Guid.Empty)
        {
            var ownedGroupIds = await db.TenantOwnerGroups.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId.Value)
                .Select(x => x.Id)
                .ToListAsync(ct);

            tenantQuery = tenantQuery.Where(t =>
                (t.OwnerGroupId.HasValue && ownedGroupIds.Contains(t.OwnerGroupId.Value)) ||
                db.TenantUsers.Any(u => u.TenantId == t.Id && u.UserId == ownerUserId.Value && u.Role.ToLower() == "owner"));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var companyTenantIds = await db.TenantCompanyProfiles.AsNoTracking()
                .Where(x =>
                    x.CompanyName.ToLower().Contains(search) ||
                    x.LegalName.ToLower().Contains(search) ||
                    x.BillingEmail.ToLower().Contains(search))
                .Select(x => x.TenantId)
                .Distinct()
                .ToListAsync(ct);

            tenantQuery = tenantQuery.Where(t =>
                t.Name.ToLower().Contains(search) ||
                t.Slug.ToLower().Contains(search) ||
                companyTenantIds.Contains(t.Id));
        }

        var totalCount = await tenantQuery.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)safePageSize));
        var safePage = Math.Min(requestedPage, totalPages);
        var skip = (safePage - 1) * safePageSize;

        var tenantRows = await tenantQuery
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(safePageSize)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Slug,
                x.OwnerGroupId,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var tenantIds = tenantRows.Select(x => x.Id).ToList();
        if (tenantIds.Count == 0)
        {
            return Ok(new
            {
                page = safePage,
                pageSize = safePageSize,
                totalCount,
                totalPages,
                hasPreviousPage = safePage > 1,
                hasNextPage = safePage < totalPages,
                items = Array.Empty<object>()
            });
        }

        var profiles = await db.TenantCompanyProfiles.AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId))
            .ToDictionaryAsync(x => x.TenantId, ct);
        var ownerGroupIds = tenantRows
            .Where(x => x.OwnerGroupId.HasValue)
            .Select(x => x.OwnerGroupId!.Value)
            .Distinct()
            .ToList();
        var ownerGroups = ownerGroupIds.Count == 0
            ? new Dictionary<Guid, TenantOwnerGroup>()
            : await db.TenantOwnerGroups.AsNoTracking()
                .Where(x => ownerGroupIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var memberships = await db.TenantUsers.AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId))
            .ToListAsync(ct);
        var membershipUserIds = memberships.Select(x => x.UserId)
            .Concat(ownerGroups.Values.Select(x => x.OwnerUserId))
            .Distinct()
            .ToList();
        var users = membershipUserIds.Count == 0
            ? new Dictionary<Guid, User>()
            : await db.Users.AsNoTracking()
                .Where(x => membershipUserIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var invoiceAgg = await db.BillingInvoices.AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId))
            .GroupBy(x => x.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                InvoiceCount = g.Count(),
                TotalRevenue = g.Sum(x => x.Total)
            })
            .ToDictionaryAsync(x => x.TenantId, ct);

        var latestSubscriptions = await db.TenantSubscriptions.AsNoTracking()
            .Where(x => tenantIds.Contains(x.TenantId))
            .GroupBy(x => x.TenantId)
            .Select(g => g.OrderByDescending(x => x.CreatedAtUtc).First())
            .ToListAsync(ct);
        var planIds = latestSubscriptions.Select(x => x.PlanId).Distinct().ToList();
        var plans = planIds.Count == 0
            ? new Dictionary<Guid, BillingPlan>()
            : await db.BillingPlans.AsNoTracking()
                .Where(x => planIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);

        var rows = tenantRows.Select(tenant =>
        {
            profiles.TryGetValue(tenant.Id, out var profile);
            var tenantMemberships = memberships.Where(x => x.TenantId == tenant.Id).ToList();
            User? owner = null;
            if (tenant.OwnerGroupId.HasValue &&
                ownerGroups.TryGetValue(tenant.OwnerGroupId.Value, out var ownerGroup) &&
                users.TryGetValue(ownerGroup.OwnerUserId, out var ownerGroupUser))
            {
                owner = ownerGroupUser;
            }
            else
            {
                var ownerMembership = tenantMemberships
                    .OrderBy(x => RolePriority(x.Role))
                    .ThenBy(x => x.CreatedAtUtc)
                    .FirstOrDefault();
                owner = ownerMembership is not null && users.TryGetValue(ownerMembership.UserId, out var ownerUser)
                    ? ownerUser
                    : null;
            }
            var activeUsers = tenantMemberships.Count(x =>
                users.TryGetValue(x.UserId, out var u) && u.IsActive);
            var latestSubscription = latestSubscriptions.FirstOrDefault(x => x.TenantId == tenant.Id);
            var plan = latestSubscription is not null && plans.TryGetValue(latestSubscription.PlanId, out var planRow)
                ? planRow
                : null;
            invoiceAgg.TryGetValue(tenant.Id, out var invoiceSummary);

            return new
            {
                tenantId = tenant.Id,
                tenantName = tenant.Name,
                tenantSlug = tenant.Slug,
                companyName = !string.IsNullOrWhiteSpace(profile?.CompanyName) ? profile.CompanyName : tenant.Name,
                ownerGroupId = tenant.OwnerGroupId,
                ownerUserId = owner?.Id,
                createdAtUtc = tenant.CreatedAtUtc,
                ownerName = !string.IsNullOrWhiteSpace(owner?.FullName) ? owner.FullName : owner?.Email ?? "-",
                ownerEmail = owner?.Email ?? "-",
                users = tenantMemberships.Select(x => x.UserId).Distinct().Count(),
                activeUsers,
                planCode = plan?.Code ?? string.Empty,
                planName = !string.IsNullOrWhiteSpace(plan?.Name) ? plan.Name : "No Plan",
                subscriptionStatus = latestSubscription?.Status ?? "none",
                monthlyPrice = plan?.PriceMonthly ?? 0m,
                invoiceCount = invoiceSummary?.InvoiceCount ?? 0,
                totalRevenue = invoiceSummary?.TotalRevenue ?? 0m
            };
        }).ToList();

        return Ok(new
        {
            page = safePage,
            pageSize = safePageSize,
            totalCount,
            totalPages,
            hasPreviousPage = safePage > 1,
            hasNextPage = safePage < totalPages,
            items = rows.Select(x => new
            {
                x.tenantId,
                x.tenantName,
                x.tenantSlug,
                x.companyName,
                x.ownerGroupId,
                x.ownerUserId,
                x.createdAtUtc,
                x.ownerName,
                x.ownerEmail,
                x.users,
                x.activeUsers,
                x.planCode,
                x.planName,
                x.subscriptionStatus,
                x.monthlyPrice,
                x.invoiceCount,
                x.totalRevenue
            })
        });
    }

    [HttpGet("owners")]
    public async Task<IActionResult> Owners([FromQuery] string q = "", CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        var ownerMembershipUserIds = await db.TenantUsers.AsNoTracking()
            .Where(x => x.Role.ToLower() == "owner")
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
        var ownerGroupUserIds = await db.TenantOwnerGroups.AsNoTracking()
            .Select(x => x.OwnerUserId)
            .Distinct()
            .ToListAsync(ct);
        var ownerUserIds = ownerMembershipUserIds
            .Concat(ownerGroupUserIds)
            .Distinct()
            .ToList();

        if (ownerUserIds.Count == 0) return Ok(Array.Empty<object>());

        IQueryable<User> ownerQuery = db.Users.AsNoTracking().Where(u => ownerUserIds.Contains(u.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            ownerQuery = ownerQuery.Where(u => u.Email.ToLower().Contains(search) || u.FullName.ToLower().Contains(search));
        }

        var owners = await ownerQuery
            .OrderBy(u => u.FullName)
            .ThenBy(u => u.Email)
            .Take(500)
            .ToListAsync(ct);

        var ownerIds = owners.Select(x => x.Id).ToList();
        var ownedGroupCounts = await db.TenantOwnerGroups.AsNoTracking()
            .Where(x => ownerIds.Contains(x.OwnerUserId))
            .GroupBy(x => x.OwnerUserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return Ok(owners.Select(owner => new
        {
            userId = owner.Id,
            name = string.IsNullOrWhiteSpace(owner.FullName) ? owner.Email : owner.FullName,
            email = owner.Email,
            isActive = owner.IsActive,
            ownerGroupCount = ownedGroupCounts.TryGetValue(owner.Id, out var count) ? count : 0
        }));
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(
        [FromQuery] string q = "",
        [FromQuery] bool ownersOnly = false,
        [FromQuery] bool includeSuperAdmins = false,
        CancellationToken ct = default)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var search = (q ?? string.Empty).Trim().ToLowerInvariant();
        List<User> users;
        List<TenantUser> memberships;

        if (ownersOnly)
        {
            var ownerMembershipUserIds = await db.TenantUsers.AsNoTracking()
                .Where(x => x.Role.ToLower() == "owner")
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(ct);
            var ownerGroupUserIds = await db.TenantOwnerGroups.AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.OwnerUserId)
                .Distinct()
                .ToListAsync(ct);
            var ownerUserIds = ownerMembershipUserIds
                .Concat(ownerGroupUserIds)
                .Distinct()
                .ToList();

            var ownerQuery = db.Users.AsQueryable().Where(u => ownerUserIds.Contains(u.Id));
            if (!string.IsNullOrWhiteSpace(search))
            {
                ownerQuery = ownerQuery.Where(u => u.Email.ToLower().Contains(search) || u.FullName.ToLower().Contains(search));
            }

            users = await ownerQuery
                .OrderByDescending(u => u.CreatedAtUtc)
                .Take(500)
                .ToListAsync(ct);

            var ownerIds = users.Select(u => u.Id).ToList();
            memberships = ownerIds.Count == 0
                ? new List<TenantUser>()
                : await db.TenantUsers.AsNoTracking().Where(x => ownerIds.Contains(x.UserId)).ToListAsync(ct);
        }
        else
        {
            var baseQuery = db.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                baseQuery = baseQuery.Where(u => u.Email.ToLower().Contains(search) || u.FullName.ToLower().Contains(search));
            }

            users = await baseQuery
                .OrderByDescending(u => u.CreatedAtUtc)
                .Take(500)
                .ToListAsync(ct);

            var userIds = users.Select(u => u.Id).ToList();
            memberships = userIds.Count == 0
                ? new List<TenantUser>()
                : await db.TenantUsers.AsNoTracking().Where(x => userIds.Contains(x.UserId)).ToListAsync(ct);

            if (!includeSuperAdmins)
            {
                users = users.Where(u => !u.IsSuperAdmin).ToList();
            }
        }

        var rows = users.Select(u =>
        {
            var m = memberships.Where(x => x.UserId == u.Id).ToList();
            var roles = m
                .Select(x => (x.Role ?? string.Empty).Trim().ToLowerInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            if (u.IsSuperAdmin && !roles.Contains(RolePermissionCatalog.SuperAdmin, StringComparer.OrdinalIgnoreCase))
            {
                roles.Insert(0, RolePermissionCatalog.SuperAdmin);
            }

            return new
            {
                userId = u.Id,
                name = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName,
                email = u.Email,
                isActive = u.IsActive,
                isSuperAdmin = u.IsSuperAdmin,
                tenantCount = m.Select(x => x.TenantId).Distinct().Count(),
                roles,
                rolePreview = string.Join(", ", roles),
                createdAtUtc = u.CreatedAtUtc
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
        var ownedGroupIds = await db.TenantOwnerGroups.AsNoTracking()
            .Where(x => x.OwnerUserId == userId && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var membershipTenantIds = memberships.Select(x => x.TenantId).Distinct().ToList();
        var ownedGroupTenantIds = ownedGroupIds.Count == 0
            ? new List<Guid>()
            : await db.Tenants.AsNoTracking()
                .Where(x => x.OwnerGroupId.HasValue && ownedGroupIds.Contains(x.OwnerGroupId.Value))
                .Select(x => x.Id)
                .Distinct()
                .ToListAsync(ct);
        var tenantIds = membershipTenantIds.Concat(ownedGroupTenantIds).Distinct().ToList();
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
                var role = memberships.FirstOrDefault(m => m.TenantId == t.Id)?.Role
                    ?? (ownedGroupIds.Contains(t.OwnerGroupId ?? Guid.Empty) ? "owner" : "member");
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
                ownerGroupSmsProviderRoute = "tata",
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
            ownerGroupSmsProviderRoute = await db.TenantOwnerGroups.AsNoTracking().Where(x => x.Id == tenant.OwnerGroupId).Select(x => x.SmsProviderRoute).FirstOrDefaultAsync(ct) ?? "tata",
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

        if (tenant.OwnerGroupId.HasValue && tenant.OwnerGroupId.Value != Guid.Empty)
        {
            var ownerGroup = await db.TenantOwnerGroups.FirstOrDefaultAsync(x => x.Id == tenant.OwnerGroupId.Value, ct);
            if (ownerGroup is not null)
            {
                ownerGroup.SmsProviderRoute = NormalizeSmsProvider(request.OwnerGroupSmsProviderRoute);
                ownerGroup.UpdatedAtUtc = now;
            }
        }

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
            ownerGroupSmsProviderRoute = await db.TenantOwnerGroups.AsNoTracking().Where(x => x.Id == tenant.OwnerGroupId).Select(x => x.SmsProviderRoute).FirstOrDefaultAsync(ct) ?? "tata",
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
        var now = DateTime.UtcNow;

        var sub = await db.TenantSubscriptions
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var shouldCreateNewSubscription = sub is null
            || request.ResetStartDate
            || sub.PlanId != plan.Id
            || !string.Equals(sub.BillingCycle, cycle, StringComparison.OrdinalIgnoreCase);

        if (shouldCreateNewSubscription)
        {
            if (sub is not null)
            {
                sub.CancelledAtUtc = now;
                sub.UpdatedAtUtc = now;
            }

            sub = new TenantSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                Status = status,
                BillingCycle = cycle,
                StartedAtUtc = now,
                RenewAtUtc = ResolveRenewAtUtc(cycle, status, trialDays),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.TenantSubscriptions.Add(sub);
        }
        else
        {
            var currentSubscription = sub!;
            currentSubscription.PlanId = plan.Id;
            currentSubscription.Status = status;
            currentSubscription.BillingCycle = cycle;
            currentSubscription.RenewAtUtc = ResolveRenewAtUtc(cycle, status, trialDays);
            if (request.ResetStartDate) currentSubscription.StartedAtUtc = now;
            currentSubscription.CancelledAtUtc = status == "cancelled" ? now : null;
            currentSubscription.UpdatedAtUtc = now;
        }

        var manualInvoiceId = status is "active" or "trial" or "trialing"
            ? await CreateManualAssignmentInvoiceAsync(tenant, plan, sub!, now, ct)
            : Guid.Empty;
        var savedSubscription = sub!;

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("platform.customer.assign_plan", $"tenant={tenantId}; plan={plan.Code}; cycle={cycle}; status={status}; trialDays={trialDays}", ct);

        return Ok(new
        {
            assigned = true,
            tenantId,
            invoiceId = manualInvoiceId == Guid.Empty ? (Guid?)null : manualInvoiceId,
            plan = new { plan.Id, plan.Code, plan.Name },
            subscription = new
            {
                savedSubscription.Id,
                savedSubscription.Status,
                savedSubscription.BillingCycle,
                savedSubscription.StartedAtUtc,
                savedSubscription.RenewAtUtc,
                savedSubscription.CancelledAtUtc,
                savedSubscription.UpdatedAtUtc
            }
        });
    }

    private async Task<Guid> CreateManualAssignmentInvoiceAsync(Tenant tenant, BillingPlan plan, TenantSubscription subscription, DateTime issuedAtUtc, CancellationToken ct)
    {
        var referenceNo = $"manual-plan:{subscription.Id:D}";
        var existingInvoice = await db.BillingInvoices
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ReferenceNo == referenceNo, ct);
        if (existingInvoice is not null)
            return existingInvoice.Id;

        var profile = await db.TenantCompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        var invoiceNo = $"INV-MAN-{issuedAtUtc:yyyyMMdd}-{subscription.Id.ToString("N")[..8].ToUpperInvariant()}";
        var periodStartUtc = DateTime.SpecifyKind(subscription.StartedAtUtc, DateTimeKind.Utc);
        var periodEndUtc = ResolvePeriodEndUtc(periodStartUtc, subscription.BillingCycle);
        var invoiceAmounts = ComputeInvoiceAmounts(
            ResolvePlanAmount(plan, subscription.BillingCycle),
            profile?.TaxRatePercent ?? 18m,
            plan.TaxMode,
            profile?.IsTaxExempt ?? false,
            profile?.IsReverseCharge ?? false);

        var invoice = new BillingInvoice
        {
            Id = Guid.NewGuid(),
            InvoiceNo = invoiceNo,
            TenantId = tenant.Id,
            InvoiceKind = "tax_invoice",
            BillingCycle = subscription.BillingCycle,
            TaxMode = plan.TaxMode,
            ReferenceNo = referenceNo,
            Description = ResolveInvoiceDescription(plan.Name, subscription.BillingCycle, plan.PricingModel),
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            Subtotal = invoiceAmounts.Subtotal,
            TaxAmount = invoiceAmounts.TaxAmount,
            Total = invoiceAmounts.Total,
            Status = "issued",
            PaidAtUtc = null,
            PdfUrl = string.Empty,
            IntegrityAlgo = "SHA256",
            IssuedAtUtc = issuedAtUtc,
            CreatedAtUtc = issuedAtUtc
        };
        invoice.IntegrityHash = ComputeInvoiceIntegrityHash(invoice);
        db.BillingInvoices.Add(invoice);
        return invoice.Id;
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

    private readonly record struct InvoiceAmounts(decimal Subtotal, decimal TaxAmount, decimal Total);

    private static InvoiceAmounts ComputeInvoiceAmounts(
        decimal planAmount,
        decimal taxRatePercent,
        string? taxMode,
        bool isTaxExempt,
        bool isReverseCharge)
    {
        var amounts = BillingComputation.ComputeInvoiceAmounts(planAmount, taxRatePercent, taxMode, isTaxExempt, isReverseCharge);
        return new InvoiceAmounts(amounts.Subtotal, amounts.TaxAmount, amounts.Total);
    }

    private static decimal ResolvePlanAmount(BillingPlan plan, string billingCycle)
    {
        var cycle = (billingCycle ?? string.Empty).Trim().ToLowerInvariant();
        return cycle switch
        {
            "yearly" => plan.PriceYearly > 0 ? plan.PriceYearly : plan.PriceMonthly,
            "lifetime" => plan.PriceYearly > 0 ? plan.PriceYearly : plan.PriceMonthly,
            "usage_based" => plan.PriceMonthly,
            _ => plan.PriceMonthly
        };
    }

    private static string ResolveInvoiceDescription(string? planName, string? billingCycle, string? pricingModel)
    {
        var name = (planName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (string.Equals(pricingModel, "usage_pack", StringComparison.OrdinalIgnoreCase))
                return $"{name} purchase";

            if (name.Contains("authenticator", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("integration", StringComparison.OrdinalIgnoreCase))
                return $"{name} purchase";

            return $"{name} plan purchase";
        }

        return (billingCycle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "yearly" => "Yearly subscription purchase",
            "monthly" => "Monthly subscription purchase",
            "lifetime" => "Lifetime plan purchase",
            "usage_based" => "Usage pack purchase",
            _ => "Platform service purchase"
        };
    }

    private static string ComputeInvoiceIntegrityHash(BillingInvoice invoice) => InvoiceIntegrityHasher.Compute(invoice);

    private static DateTime ResolvePeriodEndUtc(DateTime periodStartUtc, string cycle)
    {
        return cycle switch
        {
            "yearly" => periodStartUtc.AddYears(1).AddSeconds(-1),
            "lifetime" => periodStartUtc.AddYears(100).AddSeconds(-1),
            "usage_based" => periodStartUtc.AddMonths(1).AddSeconds(-1),
            _ => periodStartUtc.AddMonths(1).AddSeconds(-1)
        };
    }

    private sealed class OwnerSqlRow
    {
        public Guid TenantId { get; init; }
        public Guid UserId { get; init; }
    }

    private sealed class MembershipAggSqlRow
    {
        public Guid TenantId { get; init; }
        public int Users { get; init; }
        public int ActiveUsers { get; init; }
    }

    private sealed class InvoiceAggSqlRow
    {
        public Guid TenantId { get; init; }
        public int InvoiceCount { get; init; }
        public decimal TotalRevenue { get; init; }
    }

    private sealed class SubscriptionSqlRow
    {
        public Guid TenantId { get; init; }
        public Guid PlanId { get; init; }
        public string Status { get; init; } = string.Empty;
    }

    private sealed class PlatformCustomerListRow
    {
        public Guid TenantId { get; init; }
        public string TenantName { get; init; } = string.Empty;
        public string TenantSlug { get; init; } = string.Empty;
        public string CompanyName { get; init; } = string.Empty;
        public Guid? OwnerGroupId { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public string OwnerName { get; init; } = string.Empty;
        public string OwnerEmail { get; init; } = string.Empty;
        public int Users { get; init; }
        public int ActiveUsers { get; init; }
        public string PlanCode { get; init; } = string.Empty;
        public string PlanName { get; init; } = string.Empty;
        public string SubscriptionStatus { get; init; } = string.Empty;
        public decimal MonthlyPrice { get; init; }
        public int InvoiceCount { get; init; }
        public decimal TotalRevenue { get; init; }
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
        public string OwnerGroupSmsProviderRoute { get; set; } = "tata";
        public decimal TaxRatePercent { get; set; } = 18m;
        public bool IsTaxExempt { get; set; }
        public bool IsReverseCharge { get; set; }
    }

    private static string NormalizeSmsProvider(string? provider)
    {
        var value = (provider ?? "tata").Trim().ToLowerInvariant();
        return value == "equence" ? "equence" : "tata";
    }

    private bool HasFreshStepUp()
        => auth.StepUpVerifiedAtUtc.HasValue && auth.StepUpVerifiedAtUtc.Value >= DateTime.UtcNow.Subtract(StepUpFreshWindow);
}
