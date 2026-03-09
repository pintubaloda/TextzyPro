using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/team")]
public class TeamController(
    ControlDbContext db,
    AuthContext auth,
    TenancyContext tenancy,
    PasswordHasher hasher,
    IEmailService emailService,
    BillingGuardService billingGuard,
    SecretCryptoService crypto,
    IConfiguration config,
    AuditLogService audit) : ControllerBase
{
    private static readonly TimeSpan StepUpFreshWindow = TimeSpan.FromMinutes(10);
    [HttpGet("members")]
    public IActionResult Members()
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();

        var pendingInvites = db.TeamInvitations
            .Where(i => i.TenantId == tenancy.TenantId && i.Status == "pending" && i.ExpiresAtUtc > DateTime.UtcNow)
            .ToList()
            .GroupBy(i => (i.Email ?? string.Empty).ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SentAtUtc).First());

        var memberRows = db.TenantUsers
            .Where(tu => tu.TenantId == tenancy.TenantId)
            .Join(db.Users, tu => tu.UserId, u => u.Id, (tu, u) => new
            {
                id = u.Id,
                name = string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName,
                email = u.Email,
                role = tu.Role,
                status = u.IsActive ? "active" : "inactive",
                joinedAtUtc = tu.CreatedAtUtc
            })
            .OrderBy(x => x.name)
            .ToList()
            .Select(x =>
            {
                pendingInvites.TryGetValue((x.email ?? string.Empty).ToLowerInvariant(), out var inv);
                return new
                {
                    x.id,
                    x.name,
                    x.email,
                    x.role,
                    status = inv is not null ? "pending" : x.status,
                    invitationStatus = inv?.Status ?? "accepted",
                    invitationSentAtUtc = inv?.SentAtUtc,
                    invitationExpiresAtUtc = inv?.ExpiresAtUtc,
                    invitationSendCount = inv?.SendCount ?? 0,
                    joinedAtUtc = (DateTime?)x.joinedAtUtc,
                    inviteOnly = false
                };
            })
            .ToList();

        var memberEmails = memberRows
            .Select(x => (x.email ?? string.Empty).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inviteOnlyRows = pendingInvites.Values
            .Where(inv => !memberEmails.Contains((inv.Email ?? string.Empty).ToLowerInvariant()))
            .Select(inv => new
            {
                id = inv.Id,
                name = string.IsNullOrWhiteSpace(inv.Name) ? inv.Email : inv.Name.Trim(),
                email = (string?)inv.Email,
                role = inv.Role,
                status = "pending",
                invitationStatus = inv.Status,
                invitationSentAtUtc = (DateTime?)inv.SentAtUtc,
                invitationExpiresAtUtc = (DateTime?)inv.ExpiresAtUtc,
                invitationSendCount = inv.SendCount,
                joinedAtUtc = (DateTime?)null,
                inviteOnly = true
            })
            .ToList();

        var rows = memberRows
            .Concat(inviteOnlyRows)
            .OrderBy(x => x.name)
            .ThenBy(x => x.email)
            .ToList();

        return Ok(rows);
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteMemberRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();
        if (!HasFreshStepUp())
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action = "team_invite",
                title = "Verify team invitation",
                message = "Enter your authenticator code to invite or update a team member."
            });
        }

        string email;
        try
        {
            email = InputGuardService.ValidateEmailOrEmpty(request.Email, "Email").ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        request.Name = (request.Name ?? string.Empty).Trim();
        if (request.Name.Length > 256) return BadRequest("Name is too long.");
        var role = NormalizeRole(request.Role);
        if (string.IsNullOrWhiteSpace(email)) return BadRequest("Email is required.");
        if (string.IsNullOrWhiteSpace(role)) return BadRequest("Valid role is required.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
        if (user is null)
        {
            var tempPassword = $"Welcome@{Random.Shared.Next(100000, 999999)}";
            var (hash, salt) = hasher.HashPassword(tempPassword);
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                FullName = (request.Name ?? string.Empty).Trim(),
                PasswordHash = hash,
                PasswordSalt = salt,
                IsActive = true,
                IsSuperAdmin = false
            };
            db.Users.Add(user);
        }
        else if (string.IsNullOrWhiteSpace(user.FullName) && !string.IsNullOrWhiteSpace(request.Name))
        {
            user.FullName = request.Name.Trim();
        }

        var existingMembership = await db.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == tenancy.TenantId && tu.UserId == user.Id, ct);
        var currentMembers = await db.TenantUsers.CountAsync(tu => tu.TenantId == tenancy.TenantId, ct);
        var projectedMembers = existingMembership is null ? currentMembers + 1 : currentMembers;
        var limit = await billingGuard.CheckLimitAsync(tenancy.TenantId, "teamMembers", projectedMembers, ct);
        if (!limit.Allowed) return BadRequest(limit.Message);
        var tenantOwnerGroupId = await EnsureTenantOwnerGroupAsync(tenancy.TenantId, auth.UserId, ct);

        if (!await CanUserJoinOwnerGroupAsync(user.Id, tenantOwnerGroupId, ct))
            return BadRequest("User already belongs to another tenant owner group and cannot be added to this tenant.");

        if (existingMembership is not null)
        {
            var previousRole = existingMembership.Role;
            existingMembership.Role = role;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("team.role.update", $"tenant={tenancy.TenantId}; user={user.Id}; from={previousRole}; to={role}", ct);
            return Ok(new
            {
                email,
                role,
                invitationStatus = "accepted",
                message = "User is already a team member. Role updated."
            });
        }

        var activeInvite = await db.TeamInvitations
            .FirstOrDefaultAsync(i => i.TenantId == tenancy.TenantId && i.Email.ToLower() == email && i.Status == "pending", ct);

        var rawToken = CreateOpaqueToken();
        var tokenHash = HashToken(rawToken);
        if (activeInvite is null)
        {
            activeInvite = new TeamInvitation
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                Email = email,
                Name = (request.Name ?? string.Empty).Trim(),
                Role = role,
                TokenHash = tokenHash,
                Status = "pending",
                SendCount = 1,
                SentAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                CreatedByUserId = auth.UserId
            };
            db.TeamInvitations.Add(activeInvite);
        }
        else
        {
            activeInvite.Role = role;
            activeInvite.Name = (request.Name ?? activeInvite.Name).Trim();
            activeInvite.TokenHash = tokenHash;
            activeInvite.SendCount += 1;
            activeInvite.SentAtUtc = DateTime.UtcNow;
            activeInvite.ExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        }

        await db.SaveChangesAsync(ct);
        var inviteUrl = await BuildInviteUrlAsync(rawToken, ct);
        try
        {
            await emailService.SendInviteAsync(email, request.Name ?? string.Empty, inviteUrl, ct);
        }
        catch (Exception ex)
        {
            await audit.WriteAsync("team.invite.email_failed", $"tenant={tenancy.TenantId}; email={email}; err={ex.Message}", ct);
            return StatusCode(StatusCodes.Status502BadGateway, "Invite created but email delivery failed. Check SMTP settings.");
        }
        await audit.WriteAsync("team.invite", $"tenant={tenancy.TenantId}; email={email}; role={role}", ct);

        return Ok(new { email, role, invitationStatus = "pending", inviteUrl });
    }

    [HttpPost("invitations/resend")]
    public async Task<IActionResult> Resend([FromBody] ResendInvitationRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();

        string email;
        try
        {
            email = InputGuardService.ValidateEmailOrEmpty(request.Email, "Email").ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        if (string.IsNullOrWhiteSpace(email)) return BadRequest("Email is required.");

        var invite = await db.TeamInvitations
            .Where(i => i.TenantId == tenancy.TenantId && i.Email.ToLower() == email)
            .OrderByDescending(i => i.SentAtUtc)
            .FirstOrDefaultAsync(ct);
        if (invite is null) return NotFound("Invitation not found.");
        if (invite.Status == "accepted") return BadRequest("Invitation already accepted.");

        invite.Status = "pending";
        invite.SentAtUtc = DateTime.UtcNow;
        invite.ExpiresAtUtc = DateTime.UtcNow.AddDays(7);
        invite.SendCount += 1;
        var rawToken = CreateOpaqueToken();
        invite.TokenHash = HashToken(rawToken);
        await db.SaveChangesAsync(ct);

        var inviteUrl = await BuildInviteUrlAsync(rawToken, ct);
        try
        {
            await emailService.SendInviteAsync(invite.Email, invite.Name, inviteUrl, ct);
        }
        catch (Exception ex)
        {
            await audit.WriteAsync("team.invite.resend_email_failed", $"tenant={tenancy.TenantId}; email={email}; err={ex.Message}", ct);
            return StatusCode(StatusCodes.Status502BadGateway, "Invitation resent in system but email delivery failed. Check SMTP settings.");
        }
        await audit.WriteAsync("team.invite.resend", $"tenant={tenancy.TenantId}; email={email}; count={invite.SendCount}", ct);
        return Ok(new { email = invite.Email, invitationStatus = invite.Status, sentAtUtc = invite.SentAtUtc, sendCount = invite.SendCount, inviteUrl });
    }

    [HttpPost("invitations/cancel")]
    public async Task<IActionResult> CancelInvitation([FromBody] CancelInvitationRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();

        string email;
        try
        {
            email = InputGuardService.ValidateEmailOrEmpty(request.Email, "Email").ToLowerInvariant();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        if (string.IsNullOrWhiteSpace(email)) return BadRequest("Email is required.");

        var invites = await db.TeamInvitations
            .Where(i => i.TenantId == tenancy.TenantId && i.Email.ToLower() == email && i.Status == "pending")
            .ToListAsync(ct);
        if (invites.Count == 0) return NotFound("Pending invitation not found.");

        foreach (var inv in invites)
        {
            inv.Status = "cancelled";
            inv.ExpiresAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("team.invite.cancel", $"tenant={tenancy.TenantId}; email={email}; count={invites.Count}", ct);
        return Ok(new { email, cancelled = true });
    }

    [HttpPatch("members/{userId:guid}/role")]
    public async Task<IActionResult> UpdateRole(Guid userId, [FromBody] UpdateMemberRoleRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();
        if (!HasFreshStepUp())
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action = "team_role_change",
                title = "Verify role change",
                message = "Enter your authenticator code to change a team member role."
            });
        }

        var nextRole = NormalizeRole(request.Role);
        if (string.IsNullOrWhiteSpace(nextRole)) return BadRequest("Valid role is required.");

        var member = await db.TenantUsers
            .FirstOrDefaultAsync(tu => tu.TenantId == tenancy.TenantId && tu.UserId == userId, ct);
        if (member is null) return NotFound("Member not found.");
        if (member.UserId == auth.UserId && !string.Equals(nextRole, member.Role, StringComparison.OrdinalIgnoreCase))
            return BadRequest("You cannot change your own role.");

        var prevRole = member.Role;
        member.Role = nextRole;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("team.role.update", $"tenant={tenancy.TenantId}; user={userId}; from={prevRole}; to={nextRole}", ct);

        return Ok(new { userId, role = nextRole });
    }

    [HttpDelete("members/{userId:guid}")]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();
        if (!HasFreshStepUp())
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action = "team_remove",
                title = "Verify member removal",
                message = "Enter your authenticator code to remove a team member."
            });
        }

        var member = await db.TenantUsers
            .FirstOrDefaultAsync(tu => tu.TenantId == tenancy.TenantId && tu.UserId == userId, ct);
        if (member is null) return NotFound("Member not found.");
        if (member.UserId == auth.UserId) return BadRequest("You cannot remove yourself.");

        db.TenantUsers.Remove(member);
        var overrides = db.TenantUserPermissionOverrides.Where(x => x.TenantId == tenancy.TenantId && x.UserId == userId);
        db.TenantUserPermissionOverrides.RemoveRange(overrides);
        await db.SaveChangesAsync(ct);
        var memberCount = await db.TenantUsers.CountAsync(tu => tu.TenantId == tenancy.TenantId, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "teamMembers", memberCount, ct);
        await audit.WriteAsync("team.remove", $"tenant={tenancy.TenantId}; user={userId}; role={member.Role}", ct);
        return NoContent();
    }

    [HttpGet("members/{userId:guid}/activity")]
    public IActionResult Activity(Guid userId, [FromQuery] int limit = 50)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();

        var safeLimit = Math.Clamp(limit, 1, 200);
        var rows = db.AuditLogs
            .Where(a => a.TenantId == tenancy.TenantId && a.ActorUserId == userId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(safeLimit)
            .Select(a => new
            {
                id = a.Id,
                action = a.Action,
                details = a.Details,
                ipAddress = a.IpAddress,
                userAgent = a.UserAgent,
                deviceLabel = a.DeviceLabel,
                createdAtUtc = a.CreatedAtUtc
            })
            .ToList();

        return Ok(rows);
    }

    [HttpGet("members/{userId:guid}/permissions")]
    public IActionResult MemberPermissions(Guid userId)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();

        var membership = db.TenantUsers.FirstOrDefault(tu => tu.TenantId == tenancy.TenantId && tu.UserId == userId);
        if (membership is null) return NotFound("Member not found.");

        var rolePerms = RolePermissionCatalog.GetPermissions(membership.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = db.TenantUserPermissionOverrides
            .Where(x => x.TenantId == tenancy.TenantId && x.UserId == userId)
            .ToList();
        var effective = rolePerms.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ov in overrides)
        {
            if (ov.IsAllowed) effective.Add(ov.Permission);
            else effective.Remove(ov.Permission);
        }

        return Ok(new
        {
            userId,
            role = membership.Role,
            catalog = PermissionCatalog.All,
            rolePermissions = rolePerms.ToArray(),
            overrides = overrides.Select(x => new { permission = x.Permission, isAllowed = x.IsAllowed }).ToArray(),
            effectivePermissions = effective.ToArray()
        });
    }

    [HttpPut("members/{userId:guid}/permissions")]
    public async Task<IActionResult> UpdateMemberPermissions(Guid userId, [FromBody] UpdatePermissionOverridesRequest request, CancellationToken ct)
    {
        if (!auth.IsAuthenticated || !tenancy.IsSet) return Unauthorized();
        if (!CanManageTeam(auth.Role)) return Forbid();
        if (!HasFreshStepUp())
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired, new
            {
                stepUpRequired = true,
                action = "team_permission_change",
                title = "Verify permission changes",
                message = "Enter your authenticator code to update member permissions."
            });
        }
        var membership = await db.TenantUsers.FirstOrDefaultAsync(tu => tu.TenantId == tenancy.TenantId && tu.UserId == userId, ct);
        if (membership is null) return NotFound("Member not found.");

        var rolePerms = RolePermissionCatalog.GetPermissions(membership.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incoming = (request.Overrides ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Permission) && PermissionCatalog.All.Contains(o.Permission, StringComparer.OrdinalIgnoreCase))
            .GroupBy(o => o.Permission, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();

        var current = await db.TenantUserPermissionOverrides
            .Where(x => x.TenantId == tenancy.TenantId && x.UserId == userId)
            .ToListAsync(ct);
        db.TenantUserPermissionOverrides.RemoveRange(current);

        foreach (var ov in incoming)
        {
            var inRole = rolePerms.Contains(ov.Permission);
            if ((inRole && ov.IsAllowed) || (!inRole && !ov.IsAllowed)) continue;
            db.TenantUserPermissionOverrides.Add(new TenantUserPermissionOverride
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                UserId = userId,
                Permission = ov.Permission,
                IsAllowed = ov.IsAllowed
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("team.permission.overrides.update", $"tenant={tenancy.TenantId}; user={userId}; count={incoming.Count}", ct);
        return Ok(new { updated = true });
    }

    private bool HasFreshStepUp()
        => auth.StepUpVerifiedAtUtc.HasValue && auth.StepUpVerifiedAtUtc.Value >= DateTime.UtcNow.Subtract(StepUpFreshWindow);

    private static bool CanManageTeam(string role)
        => string.Equals(role, RolePermissionCatalog.Owner, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, RolePermissionCatalog.Admin, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, RolePermissionCatalog.SuperAdmin, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRole(string? role)
    {
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            RolePermissionCatalog.Owner => RolePermissionCatalog.Owner,
            RolePermissionCatalog.Admin => RolePermissionCatalog.Admin,
            RolePermissionCatalog.Manager => RolePermissionCatalog.Manager,
            RolePermissionCatalog.Support => RolePermissionCatalog.Support,
            RolePermissionCatalog.Marketing => RolePermissionCatalog.Marketing,
            RolePermissionCatalog.Finance => RolePermissionCatalog.Finance,
            RolePermissionCatalog.SuperAdmin => RolePermissionCatalog.SuperAdmin,
            _ => string.Empty
        };
    }

    private async Task<string> BuildInviteUrlAsync(string token, CancellationToken ct)
    {
        var baseUrl = await ResolveInviteBaseUrlAsync(ct);
        return $"{baseUrl.TrimEnd('/')}/accept-invite?token={Uri.EscapeDataString(token)}";
    }

    private async Task<string> ResolveInviteBaseUrlAsync(CancellationToken ct)
    {
        var rows = await db.PlatformSettings.AsNoTracking()
            .Where(x =>
                (x.Scope == "invite" && x.Key == "acceptBaseUrl") ||
                (x.Scope == "mobile-app" && x.Key == "baseDomain") ||
                (x.Scope == "platform-branding" && x.Key == "website"))
            .ToListAsync(ct);

        string? inviteScope = null;
        string? appBaseDomain = null;
        string? brandingWebsite = null;

        foreach (var row in rows)
        {
            var value = crypto.Decrypt(row.ValueEncrypted);
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (row.Scope == "invite" && row.Key == "acceptBaseUrl") inviteScope = value;
            else if (row.Scope == "mobile-app" && row.Key == "baseDomain") appBaseDomain = value;
            else if (row.Scope == "platform-branding" && row.Key == "website") brandingWebsite = value;
        }

        return NormalizeBaseUrl(inviteScope)
            ?? NormalizeBaseUrl(appBaseDomain)
            ?? NormalizeBaseUrl(brandingWebsite)
            ?? NormalizeBaseUrl(config["Invite:AcceptBaseUrl"])
            ?? NormalizeBaseUrl(config["APP_BASE_URL"])
            ?? "https://textzy-frontend-production.up.railway.app";
    }

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

    private static string CreateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public sealed class InviteMemberRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = RolePermissionCatalog.Support;
    }

    public sealed class ResendInvitationRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public sealed class CancelInvitationRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public sealed class UpdateMemberRoleRequest
    {
        public string Role { get; set; } = string.Empty;
    }

    public sealed class UpdatePermissionOverridesRequest
    {
        public List<PermissionOverrideDto> Overrides { get; set; } = [];
    }

    public sealed class PermissionOverrideDto
    {
        public string Permission { get; set; } = string.Empty;
        public bool IsAllowed { get; set; }
    }

    private async Task<Guid> EnsureTenantOwnerGroupAsync(Guid tenantId, Guid actorUserId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct) ?? throw new InvalidOperationException("Tenant not found.");
        if (tenant.OwnerGroupId.HasValue && tenant.OwnerGroupId.Value != Guid.Empty) return tenant.OwnerGroupId.Value;

        var ownerUserId = await db.TenantUsers
            .Where(x => x.TenantId == tenantId && x.Role.ToLower() == "owner")
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty) ownerUserId = actorUserId;

        var group = await db.TenantOwnerGroups
            .Where(g => g.OwnerUserId == ownerUserId && g.IsActive)
            .OrderBy(g => g.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (group is null)
        {
            group = new TenantOwnerGroup
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                Name = $"{tenant.Name} Group",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.TenantOwnerGroups.Add(group);
        }

        tenant.OwnerGroupId = group.Id;
        await db.SaveChangesAsync(ct);
        return group.Id;
    }

    private async Task<bool> CanUserJoinOwnerGroupAsync(Guid userId, Guid tenantOwnerGroupId, CancellationToken ct)
    {
        var userTenantIds = await db.TenantUsers.Where(x => x.UserId == userId).Select(x => x.TenantId).Distinct().ToListAsync(ct);
        if (userTenantIds.Count == 0) return true;
        var otherGroupIds = await db.Tenants
            .Where(t => userTenantIds.Contains(t.Id) && t.OwnerGroupId.HasValue)
            .Select(t => t.OwnerGroupId!.Value)
            .Distinct()
            .ToListAsync(ct);
        return otherGroupIds.Count == 0 || (otherGroupIds.Count == 1 && otherGroupIds[0] == tenantOwnerGroupId);
    }
}
