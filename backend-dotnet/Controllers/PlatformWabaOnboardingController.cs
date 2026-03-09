using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Headers;
using System.Text.Json;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/platform/waba")]
public class PlatformWabaOnboardingController(
    ControlDbContext controlDb,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    AuthContext auth,
    RbacService rbac) : ControllerBase
{
    private readonly WhatsAppOptions _waOptions = configuration.GetSection("WhatsApp").Get<WhatsAppOptions>() ?? new WhatsAppOptions();

    [HttpGet("onboarding-summary")]
    public async Task<IActionResult> GetOnboardingSummary(CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();

        var tenants = await controlDb.Tenants
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        var rows = new List<object>(tenants.Count);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var knownStates = new[] { "requested", "code_received", "assets_linked", "webhook_subscribed", "ready", "degraded", "disabled", "cancelled", "not_configured", "error" };
        foreach (var s in knownStates) counts[s] = 0;

        foreach (var tenant in tenants)
        {
            try
            {
                using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
                var cfg = await tenantDb.TenantWabaConfigs
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenant.Id)
                    .OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.ConnectedAtUtc)
                    .ThenByDescending(x => x.OnboardingStartedAtUtc)
                    .FirstOrDefaultAsync(ct);

                if (cfg is null)
                {
                    counts["not_configured"] += 1;
                    rows.Add(new
                    {
                        tenantId = tenant.Id,
                        tenantName = tenant.Name,
                        tenantSlug = tenant.Slug,
                        state = "not_configured",
                        isActive = false,
                        wabaId = "",
                        phoneNumberId = "",
                        displayPhoneNumber = "",
                        businessAccountName = "",
                        startedAtUtc = (DateTime?)null,
                        connectedAtUtc = (DateTime?)null,
                        lastError = ""
                    });
                    continue;
                }

                var state = NormalizeState(cfg.OnboardingState, cfg);
                if (!counts.ContainsKey(state)) counts[state] = 0;
                counts[state] += 1;

                rows.Add(new
                {
                    tenantId = tenant.Id,
                    tenantName = tenant.Name,
                    tenantSlug = tenant.Slug,
                    state,
                    isActive = cfg.IsActive,
                    wabaId = cfg.WabaId,
                    phoneNumberId = cfg.PhoneNumberId,
                    displayPhoneNumber = cfg.DisplayPhoneNumber,
                    businessAccountName = cfg.BusinessAccountName,
                    startedAtUtc = cfg.OnboardingStartedAtUtc,
                    connectedAtUtc = cfg.ConnectedAtUtc,
                    lastError = cfg.LastError
                });
            }
            catch (Exception ex)
            {
                counts["error"] += 1;
                rows.Add(new
                {
                    tenantId = tenant.Id,
                    tenantName = tenant.Name,
                    tenantSlug = tenant.Slug,
                    state = "error",
                    isActive = false,
                    wabaId = "",
                    phoneNumberId = "",
                    displayPhoneNumber = "",
                    businessAccountName = "",
                    startedAtUtc = (DateTime?)null,
                    connectedAtUtc = (DateTime?)null,
                    lastError = $"Tenant DB read failed: {ex.GetType().Name}"
                });
            }
        }

        return Ok(new
        {
            totalProjects = tenants.Count,
            counts,
            projects = rows
        });
    }

    [HttpPost("cancel-request")]
    public async Task<IActionResult> CancelRequest([FromBody] CancelRequestDto request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (request.TenantId == Guid.Empty) return BadRequest("tenantId is required.");

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == request.TenantId, ct);
        if (tenant is null) return NotFound("Project not found.");

        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var cfg = await tenantDb.TenantWabaConfigs
            .Where(x => x.TenantId == request.TenantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .ThenByDescending(x => x.OnboardingStartedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (cfg is null) return Ok(new { cancelled = false, reason = "not_configured" });

        cfg.IsActive = false;
        cfg.OnboardingState = "cancelled";
        cfg.LastError = string.IsNullOrWhiteSpace(request.Reason)
            ? "Onboarding request cancelled by platform owner."
            : $"Onboarding request cancelled: {request.Reason.Trim()}";
        cfg.LastGraphError = string.Empty;
        cfg.WebhookSubscribedAtUtc = null;
        cfg.WebhookVerifiedAtUtc = null;
        await tenantDb.SaveChangesAsync(ct);

        return Ok(new { cancelled = true, tenantId = request.TenantId });
    }

    [HttpGet("lifecycle")]
    public async Task<IActionResult> LifecycleStatus([FromQuery] Guid tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (tenantId == Guid.Empty) return BadRequest("tenantId is required.");

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");
        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var cfg = await tenantDb.TenantWabaConfigs
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (cfg is null) return NotFound("Tenant WABA config not found.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) return BadRequest("Tenant token missing.");

        var checks = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(cfg.BusinessManagerId))
        {
            var systemUsersUrl = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{cfg.BusinessManagerId}/system_users?fields=id,name";
            var (ok, status, body) = await GraphGetRawAsync(systemUsersUrl, token, ct);
            checks["systemUsers"] = new { ok, status, body };
        }
        if (!string.IsNullOrWhiteSpace(cfg.WabaId) && !string.IsNullOrWhiteSpace(cfg.BusinessManagerId))
        {
            var assignedUrl = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{cfg.WabaId}/assigned_users?business={Uri.EscapeDataString(cfg.BusinessManagerId)}";
            var (ok, status, body) = await GraphGetRawAsync(assignedUrl, token, ct);
            checks["assignedUsers"] = new { ok, status, body };
        }

        return Ok(new
        {
            tenantId = tenant.Id,
            tenantName = tenant.Name,
            tenantSlug = tenant.Slug,
            cfg.WabaId,
            cfg.PhoneNumberId,
            cfg.BusinessManagerId,
            cfg.SystemUserId,
            cfg.SystemUserName,
            cfg.TokenSource,
            cfg.PermanentTokenIssuedAtUtc,
            cfg.PermanentTokenExpiresAtUtc,
            cfg.OnboardingState,
            cfg.PermissionAuditPassed,
            checks
        });
    }

    [HttpPost("lifecycle/reissue-token")]
    public async Task<IActionResult> ReissueSystemUserToken([FromBody] LifecycleTenantDto request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (request.TenantId == Guid.Empty) return BadRequest("tenantId is required.");

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == request.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");
        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var cfg = await tenantDb.TenantWabaConfigs
            .Where(x => x.TenantId == request.TenantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (cfg is null) return NotFound("Tenant WABA config not found.");
        if (string.IsNullOrWhiteSpace(cfg.SystemUserId)) return BadRequest("SystemUserId missing on tenant config.");
        var token = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(token)) return BadRequest("Tenant token missing.");

        var form = new Dictionary<string, string>
        {
            ["business_app"] = _waOptions.AppId,
            ["scope"] = "whatsapp_business_management,whatsapp_business_messaging,business_management"
        };
        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{cfg.SystemUserId}/access_tokens";
        var (ok, status, body) = await GraphPostFormRawAsync(url, token, form, ct);
        if (!ok) return StatusCode(status, new { error = "graph_system_user_access_token_failed", status, detail = body });

        using var doc = JsonDocument.Parse(body);
        var newToken = TryGetString(doc.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(newToken)) return StatusCode(502, new { error = "missing_access_token", detail = body });

        cfg.AccessToken = ProtectToken(newToken);
        cfg.TokenSource = "system_user";
        cfg.PermanentTokenIssuedAtUtc = DateTime.UtcNow;
        cfg.PermanentTokenExpiresAtUtc = null;
        cfg.LastError = string.Empty;
        await tenantDb.SaveChangesAsync(ct);

        return Ok(new { ok = true, cfg.TenantId, cfg.SystemUserId, cfg.TokenSource, cfg.PermanentTokenIssuedAtUtc });
    }

    [HttpPost("lifecycle/deactivate")]
    public async Task<IActionResult> DeactivateTenantWaba([FromBody] LifecycleTenantDto request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (request.TenantId == Guid.Empty) return BadRequest("tenantId is required.");

        var tenant = await controlDb.Tenants.FirstOrDefaultAsync(x => x.Id == request.TenantId, ct);
        if (tenant is null) return NotFound("Tenant not found.");
        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var cfg = await tenantDb.TenantWabaConfigs
            .Where(x => x.TenantId == request.TenantId)
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (cfg is null) return NotFound("Tenant WABA config not found.");
        cfg.IsActive = false;
        cfg.OnboardingState = "disabled";
        cfg.LastError = "Disabled by platform owner.";
        await tenantDb.SaveChangesAsync(ct);
        return Ok(new { ok = true, cfg.TenantId, cfg.OnboardingState });
    }

    [HttpGet("lookup/by-phone")]
    public async Task<IActionResult> LookupByPhone([FromQuery] Guid? tenantId, [FromQuery] string phoneNumberId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(phoneNumberId)) return BadRequest("phoneNumberId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var normalizedPhoneId = phoneNumberId.Trim();
        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{normalizedPhoneId}?fields=id,display_phone_number,verified_name,quality_rating,name_status,status";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_lookup_failed", status, detail = body });

        string wabaId = string.Empty;
        string wabaName = string.Empty;
        string display = string.Empty;
        string verifiedName = string.Empty;
        string quality = string.Empty;
        string nameStatus = string.Empty;
        string phoneStatus = string.Empty;

        using (var doc = JsonDocument.Parse(body))
        {
            var root = doc.RootElement;
            display = TryGetString(root, "display_phone_number");
            verifiedName = TryGetString(root, "verified_name");
            quality = TryGetString(root, "quality_rating");
            nameStatus = TryGetString(root, "name_status");
            phoneStatus = TryGetString(root, "status");
            if (root.TryGetProperty("whatsapp_business_account", out var wabaNode))
            {
                wabaId = TryGetString(wabaNode, "id");
                wabaName = TryGetString(wabaNode, "name");
            }
        }

        // Some Graph versions do not include whatsapp_business_account in phone fields.
        if (string.IsNullOrWhiteSpace(wabaId))
        {
            var wabaEdgeUrl = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{normalizedPhoneId}/whatsapp_business_account?fields=id,name";
            var (okEdge, _, edgeBody) = await GraphGetRawAsync(wabaEdgeUrl, ctx.AccessToken, ct);
            if (okEdge && !string.IsNullOrWhiteSpace(edgeBody))
            {
                try
                {
                    using var edgeDoc = JsonDocument.Parse(edgeBody);
                    var root = edgeDoc.RootElement;
                    wabaId = TryGetString(root, "id");
                    wabaName = TryGetString(root, "name");
                }
                catch
                {
                    // keep lookup resilient; return phone details even if edge parse fails
                }
            }
        }

        return Ok(new
        {
            tenantId = ctx.TenantId,
            tenantName = ctx.TenantName,
            tenantSlug = ctx.TenantSlug,
            tokenSource = ctx.TokenSource,
            phoneNumberId = normalizedPhoneId,
            displayPhoneNumber = display,
            verifiedName,
            qualityRating = quality,
            nameStatus,
            phoneStatus,
            wabaId,
            wabaName,
            raw = body
        });
    }

    [HttpGet("lookup/by-phone-number-id")]
    public Task<IActionResult> LookupByPhoneAlias([FromQuery] Guid? tenantId, [FromQuery] string phoneNumberId, CancellationToken ct)
        => LookupByPhone(tenantId, phoneNumberId, ct);

    [HttpGet("lookup/by-waba")]
    public async Task<IActionResult> LookupByWaba([FromQuery] Guid? tenantId, [FromQuery] string wabaId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(wabaId)) return BadRequest("wabaId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var wabaUrl = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{wabaId.Trim()}?fields=id,name,business_verification_status,account_review_status";
        var phonesUrl = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{wabaId.Trim()}/phone_numbers?fields=id,display_phone_number,verified_name,quality_rating,name_status,status";

        var (okWaba, statusWaba, bodyWaba) = await GraphGetRawAsync(wabaUrl, ctx.AccessToken, ct);
        if (!okWaba) return StatusCode(statusWaba, new { error = "graph_lookup_failed", status = statusWaba, detail = bodyWaba });

        var (okPhones, statusPhones, bodyPhones) = await GraphGetRawAsync(phonesUrl, ctx.AccessToken, ct);
        if (!okPhones) return StatusCode(statusPhones, new { error = "graph_lookup_failed", status = statusPhones, detail = bodyPhones });

        string wabaName = string.Empty;
        string verification = string.Empty;
        string reviewStatus = string.Empty;
        var phoneRows = new List<object>();

        using (var doc = JsonDocument.Parse(bodyWaba))
        {
            var root = doc.RootElement;
            wabaName = TryGetString(root, "name");
            verification = TryGetString(root, "business_verification_status");
            reviewStatus = TryGetString(root, "account_review_status");
        }

        using (var doc = JsonDocument.Parse(bodyPhones))
        {
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in data.EnumerateArray())
                {
                    phoneRows.Add(new
                    {
                        id = TryGetString(x, "id"),
                        displayPhoneNumber = TryGetString(x, "display_phone_number"),
                        verifiedName = TryGetString(x, "verified_name"),
                        qualityRating = TryGetString(x, "quality_rating"),
                        nameStatus = TryGetString(x, "name_status"),
                        status = TryGetString(x, "status")
                    });
                }
            }
        }

        return Ok(new
        {
            tenantId = ctx.TenantId,
            tenantName = ctx.TenantName,
            tenantSlug = ctx.TenantSlug,
            tokenSource = ctx.TokenSource,
            wabaId = wabaId.Trim(),
            wabaName,
            businessVerificationStatus = verification,
            accountReviewStatus = reviewStatus,
            phones = phoneRows,
            rawWaba = bodyWaba,
            rawPhones = bodyPhones
        });
    }

    [HttpGet("lookup/by-waba-id")]
    public Task<IActionResult> LookupByWabaAlias([FromQuery] Guid? tenantId, [FromQuery] string wabaId, CancellationToken ct)
        => LookupByWaba(tenantId, wabaId, ct);

    [HttpGet("meta/businesses")]
    public async Task<IActionResult> MetaBusinesses([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/me/businesses?fields=id,name";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_businesses_failed", status, detail = body });
        return Content(body, "application/json");
    }

    [HttpGet("meta/system-users")]
    public async Task<IActionResult> MetaSystemUsers([FromQuery] string businessId, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(businessId)) return BadRequest("businessId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{businessId.Trim()}/system_users?fields=id,name,role,created_time";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_system_users_failed", status, detail = body });
        return Content(body, "application/json");
    }

    // Explicit Postman-parity path wrapper: GET /{business-id}/system_users
    [HttpGet("meta/{businessId}/system_users")]
    public Task<IActionResult> MetaSystemUsersByPath([FromRoute] string businessId, [FromQuery] Guid? tenantId, CancellationToken ct)
        => MetaSystemUsers(businessId, tenantId, ct);

    [HttpGet("meta/owned-wabas")]
    public async Task<IActionResult> MetaOwnedWabas([FromQuery] string businessId, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(businessId)) return BadRequest("businessId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{businessId.Trim()}/owned_whatsapp_business_accounts?fields=id,name,business_verification_status";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_owned_wabas_failed", status, detail = body });
        return Content(body, "application/json");
    }

    [HttpGet("meta/phone-numbers")]
    public async Task<IActionResult> MetaPhoneNumbers([FromQuery] string wabaId, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(wabaId)) return BadRequest("wabaId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{wabaId.Trim()}/phone_numbers?fields=id,display_phone_number,verified_name,quality_rating,name_status,status";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_phone_numbers_failed", status, detail = body });
        return Content(body, "application/json");
    }

    [HttpGet("meta/assigned-users")]
    public async Task<IActionResult> MetaAssignedUsers([FromQuery] string wabaId, [FromQuery] string businessId, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(wabaId)) return BadRequest("wabaId is required.");
        if (string.IsNullOrWhiteSpace(businessId)) return BadRequest("businessId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{wabaId.Trim()}/assigned_users?business={Uri.EscapeDataString(businessId.Trim())}";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_assigned_users_failed", status, detail = body });
        return Content(body, "application/json");
    }

    // Explicit Postman-parity path wrapper: GET /{assigned-waba-id}/assigned_users?business=...
    [HttpGet("meta/{wabaId}/assigned_users")]
    public Task<IActionResult> MetaAssignedUsersByPath([FromRoute] string wabaId, [FromQuery] string business, [FromQuery] Guid? tenantId, CancellationToken ct)
        => MetaAssignedUsers(wabaId, business, tenantId, ct);

    [HttpPost("meta/assigned-users")]
    public async Task<IActionResult> MetaAssignUser([FromBody] MetaAssignUserRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.WabaId)) return BadRequest("wabaId is required.");
        if (string.IsNullOrWhiteSpace(request.BusinessId)) return BadRequest("businessId is required.");
        if (string.IsNullOrWhiteSpace(request.UserId)) return BadRequest("userId is required.");

        var ctx = await ResolveLookupTokenAsync(request.TenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var tasks = request.Tasks is { Length: > 0 } ? request.Tasks : ["MANAGE"];
        var form = new Dictionary<string, string>
        {
            ["user"] = request.UserId.Trim(),
            ["tasks"] = JsonSerializer.Serialize(tasks),
            ["business"] = request.BusinessId.Trim()
        };
        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{request.WabaId.Trim()}/assigned_users";
        var (ok, status, body) = await GraphPostFormRawAsync(url, ctx.AccessToken, form, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_assign_user_failed", status, detail = body });
        return Content(body, "application/json");
    }

    // Explicit Postman-parity path wrapper: POST /{assigned-waba-id}/assigned_users?user=...&tasks=...&business=...
    [HttpPost("meta/{wabaId}/assigned_users")]
    public Task<IActionResult> MetaAssignUserByPath(
        [FromRoute] string wabaId,
        [FromQuery] string user,
        [FromQuery] string tasks,
        [FromQuery] string business,
        [FromQuery] Guid? tenantId,
        CancellationToken ct)
    {
        var parsedTasks = ParseTasks(tasks);
        return MetaAssignUser(new MetaAssignUserRequest
        {
            TenantId = tenantId,
            WabaId = wabaId,
            BusinessId = business,
            UserId = user,
            Tasks = parsedTasks
        }, ct);
    }

    [HttpGet("meta/subscribed-apps")]
    public async Task<IActionResult> MetaSubscribedApps([FromQuery] string wabaId, [FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsRead)) return Forbid();
        if (string.IsNullOrWhiteSpace(wabaId)) return BadRequest("wabaId is required.");

        var ctx = await ResolveLookupTokenAsync(tenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{wabaId.Trim()}/subscribed_apps";
        var (ok, status, body) = await GraphGetRawAsync(url, ctx.AccessToken, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_subscribed_apps_failed", status, detail = body });
        return Content(body, "application/json");
    }

    [HttpPost("meta/subscribed-apps")]
    public async Task<IActionResult> MetaSubscribeApp([FromBody] MetaSubscribeAppRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.WabaId)) return BadRequest("wabaId is required.");

        var ctx = await ResolveLookupTokenAsync(request.TenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{request.WabaId.Trim()}/subscribed_apps";
        var (ok, status, body) = await GraphPostFormRawAsync(url, ctx.AccessToken, null, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_subscribe_app_failed", status, detail = body });
        return Content(body, "application/json");
    }

    [HttpPost("meta/register-phone")]
    public async Task<IActionResult> MetaRegisterPhone([FromBody] MetaRegisterPhoneRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated) return Unauthorized();
        if (!rbac.HasPermission(PlatformSettingsWrite)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.PhoneNumberId)) return BadRequest("phoneNumberId is required.");
        if (string.IsNullOrWhiteSpace(request.Pin)) return BadRequest("pin is required.");

        var ctx = await ResolveLookupTokenAsync(request.TenantId, ct);
        if (ctx is null) return NotFound("No lookup token configured. Add System User Access Token in Platform Settings > Waba Master Config.");

        var form = new Dictionary<string, string>
        {
            ["messaging_product"] = "whatsapp",
            ["pin"] = request.Pin.Trim()
        };
        var url = $"{_waOptions.GraphApiBase}/{_waOptions.ApiVersion}/{request.PhoneNumberId.Trim()}/register";
        var (ok, status, body) = await GraphPostFormRawAsync(url, ctx.AccessToken, form, ct);
        if (!ok) return StatusCode(status, new { error = "graph_meta_register_phone_failed", status, detail = body });
        return Content(body, "application/json");
    }

    private async Task<LookupTokenContext?> ResolveLookupTokenAsync(Guid? tenantId, CancellationToken ct)
    {
        var platformToken = await ResolvePlatformTokenAsync(ct);
        if (!string.IsNullOrWhiteSpace(platformToken))
        {
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                var tenant = await controlDb.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId.Value, ct);
                if (tenant is not null)
                {
                    return new LookupTokenContext(tenant.Id, tenant.Name, tenant.Slug, platformToken, "platform");
                }
            }
            return new LookupTokenContext(null, string.Empty, string.Empty, platformToken, "platform");
        }

        if (!tenantId.HasValue || tenantId.Value == Guid.Empty) return null;
        var tenantCtx = await ResolveTenantTokenAsync(tenantId.Value, ct);
        if (tenantCtx is null) return null;
        return new LookupTokenContext(tenantCtx.Value.tenantId, tenantCtx.Value.tenantName, tenantCtx.Value.tenantSlug, tenantCtx.Value.accessToken, "tenant");
    }

    private async Task<string> ResolvePlatformTokenAsync(CancellationToken ct)
    {
        var rows = await controlDb.PlatformSettings
            .AsNoTracking()
            .Where(x => x.Scope == "waba-master")
            .ToListAsync(ct);
        if (rows.Count == 0) return string.Empty;

        var values = rows.ToDictionary(x => x.Key, x => crypto.Decrypt(x.ValueEncrypted), StringComparer.OrdinalIgnoreCase);
        var candidates = new[]
        {
            "systemUserAccessToken",
            "accessToken",
            "permanentAccessToken"
        };

        foreach (var key in candidates)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private async Task<(Guid tenantId, string tenantName, string tenantSlug, string accessToken)?> ResolveTenantTokenAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await controlDb.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == tenantId, ct);
        if (tenant is null) return null;

        using var tenantDb = SeedData.CreateTenantDbContext(tenant.DataConnectionString);
        var cfg = await tenantDb.TenantWabaConfigs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !string.IsNullOrWhiteSpace(x.AccessToken))
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.ConnectedAtUtc)
            .ThenByDescending(x => x.ExchangedAtUtc)
            .ThenByDescending(x => x.CodeReceivedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.AccessToken)) return null;

        var accessToken = UnprotectToken(cfg.AccessToken);
        if (string.IsNullOrWhiteSpace(accessToken)) return null;
        return (tenant.Id, tenant.Name, tenant.Slug, accessToken);
    }

    private string UnprotectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (!token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        try { return crypto.Decrypt(token[4..]); } catch { return string.Empty; }
    }

    private string ProtectToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return string.Empty;
        if (token.StartsWith("enc:", StringComparison.Ordinal)) return token;
        return "enc:" + crypto.Encrypt(token);
    }

    private async Task<(bool ok, int status, string body)> GraphGetRawAsync(string url, string accessToken, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("whatsapp-cloud");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    private async Task<(bool ok, int status, string body)> GraphPostFormRawAsync(string url, string accessToken, Dictionary<string, string>? form, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("whatsapp-cloud");
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (form is not null && form.Count > 0)
            req.Content = new FormUrlEncodedContent(form);
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, body);
    }

    private static string[] ParseTasks(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ["MANAGE"];
        var value = raw.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(value);
                if (arr is { Length: > 0 }) return arr.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch
            {
                // Fallback to CSV parsing below.
            }
        }

        var csv = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return csv.Length > 0 ? csv : ["MANAGE"];
    }

    private static string TryGetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var p)) return string.Empty;
        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString() ?? string.Empty,
            JsonValueKind.Number => p.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static string NormalizeState(string state, TenantWabaConfig cfg)
    {
        if (IsPlaceholderNotConfigured(cfg)) return "not_configured";

        var s = (state ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) s = "not_configured";
        if (cfg.IsActive && string.Equals(s, "webhook_subscribed", StringComparison.OrdinalIgnoreCase) && cfg.PermissionAuditPassed)
            return "ready";
        return s;
    }

    private static bool IsPlaceholderNotConfigured(TenantWabaConfig cfg)
    {
        return !cfg.IsActive
               && string.IsNullOrWhiteSpace(cfg.WabaId)
               && string.IsNullOrWhiteSpace(cfg.PhoneNumberId)
               && string.IsNullOrWhiteSpace(cfg.AccessToken)
               && string.IsNullOrWhiteSpace(cfg.BusinessManagerId)
               && string.IsNullOrWhiteSpace(cfg.SystemUserId)
               && !cfg.ExchangedAtUtc.HasValue
               && (string.IsNullOrWhiteSpace(cfg.OnboardingState)
                   || string.Equals(cfg.OnboardingState, "requested", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(cfg.OnboardingState, "not_configured", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class CancelRequestDto
    {
        public Guid TenantId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class MetaAssignUserRequest
    {
        public Guid? TenantId { get; set; }
        public string WabaId { get; set; } = string.Empty;
        public string BusinessId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string[] Tasks { get; set; } = [];
    }

    public sealed class MetaSubscribeAppRequest
    {
        public Guid? TenantId { get; set; }
        public string WabaId { get; set; } = string.Empty;
    }

    public sealed class MetaRegisterPhoneRequest
    {
        public Guid? TenantId { get; set; }
        public string PhoneNumberId { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;
    }

    public sealed class LifecycleTenantDto
    {
        public Guid TenantId { get; set; }
    }

    private sealed record LookupTokenContext(Guid? TenantId, string TenantName, string TenantSlug, string AccessToken, string TokenSource);
}
