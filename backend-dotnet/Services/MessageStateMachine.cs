using Textzy.Api.Models;

namespace Textzy.Api.Services;

public static class MessageStateMachine
{
    public const string Queued = "Queued";
    public const string Accepted = "Accepted";
    public const string AcceptedByMeta = "AcceptedByMeta";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Read = "Read";
    public const string Failed = "Failed";
    public const string Received = "Received";
    public const string Processing = "Processing";
    public const string RetryScheduled = "RetryScheduled";

    private static readonly Dictionary<string, int> Priorities = new(StringComparer.OrdinalIgnoreCase)
    {
        [Queued] = 10,
        [Accepted] = 30,
        [AcceptedByMeta] = 30,
        [Sent] = 50,
        [Delivered] = 70,
        [Read] = 90,
        [Failed] = 100,
        [Received] = 100,
        [Processing] = 15,
        [RetryScheduled] = 20,
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Queued] = new(StringComparer.OrdinalIgnoreCase) { AcceptedByMeta, Sent, Failed, RetryScheduled },
        [Accepted] = new(StringComparer.OrdinalIgnoreCase) { Sent, Failed },
        [AcceptedByMeta] = new(StringComparer.OrdinalIgnoreCase) { Sent, Failed },
        [Sent] = new(StringComparer.OrdinalIgnoreCase) { Delivered, Failed },
        [Delivered] = new(StringComparer.OrdinalIgnoreCase) { Read, Failed },
        [Processing] = new(StringComparer.OrdinalIgnoreCase) { AcceptedByMeta, Sent, Failed, RetryScheduled },
        [RetryScheduled] = new(StringComparer.OrdinalIgnoreCase) { Processing, AcceptedByMeta, Sent, Failed }
    };

    public static int Priority(string state) => Priorities.TryGetValue(state ?? string.Empty, out var p) ? p : 0;
    public static bool IsTerminal(string state)
        => string.Equals(state, Read, StringComparison.OrdinalIgnoreCase)
           || string.Equals(state, Failed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(state, Received, StringComparison.OrdinalIgnoreCase);

    public static bool CanTransition(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next)) return false;
        if (string.IsNullOrWhiteSpace(current)) return true;
        if (IsTerminal(current)) return false;

        // Explicit transition matrix first, then fallback to monotonic priority.
        if (AllowedTransitions.TryGetValue(current, out var allowed))
            return allowed.Contains(next);
        return Priority(next) > Priority(current);
    }

    public static string NormalizeWebhookStatus(string raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "accepted" => AcceptedByMeta,
        "sent" => Sent,
        "delivered" => Delivered,
        "read" => Read,
        "failed" => Failed,
        _ => string.Empty
    };

    public static MessageEvent BuildEvent(
        Guid tenantId,
        Guid? messageId,
        string providerMessageId,
        string direction,
        string eventType,
        string state,
        string rawPayloadJson = "{}")
    {
        return new MessageEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MessageId = messageId,
            ProviderMessageId = providerMessageId,
            Direction = direction,
            EventType = eventType,
            State = state,
            StatePriority = Priority(state),
            EventTimestampUtc = DateTime.UtcNow,
            RawPayloadJson = rawPayloadJson,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
