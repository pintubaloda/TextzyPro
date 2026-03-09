using Microsoft.AspNetCore.SignalR;

namespace Textzy.Api.Services;

public class InboxHub(UserPresenceService presence) : Hub
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(2);

    private string ResolveUserKey()
    {
        var email = Context.User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(email)) return email.Trim().ToLowerInvariant();
        var queryUser = Context.GetHttpContext()?.Request.Query["userKey"].ToString();
        if (!string.IsNullOrWhiteSpace(queryUser)) return queryUser.Trim().ToLowerInvariant();
        return $"conn:{Context.ConnectionId}";
    }

    private static bool IsExpired(DateTime lastHeartbeatUtc) => DateTime.UtcNow - lastHeartbeatUtc > PresenceTtl;

    public async Task JoinTenantRoom(string tenantSlug)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantSlug}");
        presence.SetConnected(Context.ConnectionId, tenantSlug, ResolveUserKey());
    }

    public async Task LeaveTenantRoom(string tenantSlug)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant:{tenantSlug}");

    public Task SetUserActive(string tenantSlug, string? activeConversationId = null)
    {
        if (!string.IsNullOrWhiteSpace(tenantSlug))
            presence.SetConnected(Context.ConnectionId, tenantSlug, ResolveUserKey());
        presence.SetTabState(Context.ConnectionId, true, activeConversationId);
        return Task.CompletedTask;
    }

    public Task SetUserInactive(string tenantSlug, string? activeConversationId = null)
    {
        if (!string.IsNullOrWhiteSpace(tenantSlug))
            presence.SetConnected(Context.ConnectionId, tenantSlug, ResolveUserKey());
        presence.SetTabState(Context.ConnectionId, false, activeConversationId);
        return Task.CompletedTask;
    }

    public Task Heartbeat(string tenantSlug, string? activeConversationId = null)
    {
        if (!string.IsNullOrWhiteSpace(tenantSlug))
            presence.SetConnected(Context.ConnectionId, tenantSlug, ResolveUserKey());
        presence.Heartbeat(Context.ConnectionId, activeConversationId);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        presence.SetDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public Task<object> GetPresenceSummary(string tenantSlug)
    {
        var rows = presence.Snapshot(tenantSlug);
        var active = rows.Count(x => x.IsOnline && x.IsTabActive && !IsExpired(x.LastHeartbeatUtc));
        var background = rows.Count(x => x.IsOnline && !x.IsTabActive && !IsExpired(x.LastHeartbeatUtc));
        var stale = rows.Count(x => IsExpired(x.LastHeartbeatUtc));
        return Task.FromResult<object>(new
        {
            tenantSlug,
            active,
            background,
            stale
        });
    }
}
