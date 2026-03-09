using System.Collections.Concurrent;

namespace Textzy.Api.Services;

public sealed class UserPresenceService
{
    public sealed class PresenceState
    {
        public string ConnectionId { get; init; } = string.Empty;
        public string TenantSlug { get; init; } = string.Empty;
        public string UserKey { get; init; } = string.Empty;
        public bool IsOnline { get; set; }
        public bool IsTabActive { get; set; }
        public string ActiveConversationId { get; set; } = string.Empty;
        public DateTime LastHeartbeatUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, PresenceState> _byConnection = new(StringComparer.OrdinalIgnoreCase);

    public void SetConnected(string connectionId, string tenantSlug, string userKey)
    {
        _byConnection[connectionId] = new PresenceState
        {
            ConnectionId = connectionId,
            TenantSlug = tenantSlug,
            UserKey = userKey,
            IsOnline = true,
            IsTabActive = true,
            LastHeartbeatUtc = DateTime.UtcNow
        };
    }

    public void SetDisconnected(string connectionId)
    {
        _byConnection.TryRemove(connectionId, out _);
    }

    public void SetTabState(string connectionId, bool isActive, string? activeConversationId = null)
    {
        if (!_byConnection.TryGetValue(connectionId, out var row)) return;
        row.IsTabActive = isActive;
        row.IsOnline = true;
        row.LastHeartbeatUtc = DateTime.UtcNow;
        row.ActiveConversationId = activeConversationId ?? row.ActiveConversationId;
    }

    public void Heartbeat(string connectionId, string? activeConversationId = null)
    {
        if (!_byConnection.TryGetValue(connectionId, out var row)) return;
        row.IsOnline = true;
        row.LastHeartbeatUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(activeConversationId))
            row.ActiveConversationId = activeConversationId.Trim();
    }

    public IReadOnlyList<PresenceState> Snapshot(string tenantSlug)
        => _byConnection.Values.Where(x => string.Equals(x.TenantSlug, tenantSlug, StringComparison.OrdinalIgnoreCase)).ToList();
}

