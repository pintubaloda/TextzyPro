using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public sealed class OpsMetricsService(
    ControlDbContext controlDb,
    SensitiveDataRedactor redactor)
{
    public sealed record WebhookLagMetrics(
        DateTime FromUtc,
        int Total,
        int Processed,
        int Pending,
        int DeadLetter,
        int Unmapped,
        int Ignored,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double OldestPendingAgeSec,
        IReadOnlyDictionary<string, int> TopDeadLetterReasons);

    public async Task<WebhookLagMetrics> GetWebhookLagAsync(Guid? tenantId, int days, CancellationToken ct)
    {
        var safeDays = Math.Clamp(days, 1, 90);
        var fromUtc = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        var q = controlDb.WebhookEvents.AsNoTracking().Where(x => x.ReceivedAtUtc >= fromUtc && x.Provider == "meta");
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            q = q.Where(x => x.TenantId == tenantId.Value);

        var rows = await q
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Take(20000)
            .Select(x => new { x.Status, x.ReceivedAtUtc, x.ProcessedAtUtc, x.LastError })
            .ToListAsync(ct);

        var total = rows.Count;
        var processedRows = rows.Where(x => x.ProcessedAtUtc.HasValue).ToList();
        var processed = processedRows.Count;
        var pendingRows = rows.Where(x => !x.ProcessedAtUtc.HasValue &&
                                          (string.Equals(x.Status, "Queued", StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(x.Status, "Processing", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var pending = pendingRows.Count;

        var deadLetter = rows.Count(x => string.Equals(x.Status, "DeadLetter", StringComparison.OrdinalIgnoreCase));
        var unmapped = rows.Count(x => string.Equals(x.Status, "Unmapped", StringComparison.OrdinalIgnoreCase));
        var ignored = rows.Count(x => string.Equals(x.Status, "Ignored", StringComparison.OrdinalIgnoreCase));

        var lags = processedRows
            .Select(x => (x.ProcessedAtUtc!.Value - x.ReceivedAtUtc).TotalMilliseconds)
            .Where(x => x >= 0 && x <= 1000 * 60 * 60) // cap at 1 hour for SLO charts
            .OrderBy(x => x)
            .ToList();

        var p50 = Percentile(lags, 50);
        var p95 = Percentile(lags, 95);
        var p99 = Percentile(lags, 99);

        var oldestPendingAgeSec = pendingRows.Count == 0
            ? 0
            : Math.Max(0, (DateTime.UtcNow - pendingRows.Min(x => x.ReceivedAtUtc)).TotalSeconds);

        var deadLetterReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(x => string.Equals(x.Status, "DeadLetter", StringComparison.OrdinalIgnoreCase)))
        {
            var reason = NormalizeReason(row.LastError);
            deadLetterReasons.TryGetValue(reason, out var c);
            deadLetterReasons[reason] = c + 1;
        }

        var topReasons = deadLetterReasons
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return new WebhookLagMetrics(
            FromUtc: fromUtc,
            Total: total,
            Processed: processed,
            Pending: pending,
            DeadLetter: deadLetter,
            Unmapped: unmapped,
            Ignored: ignored,
            P50Ms: Math.Round(p50, 1),
            P95Ms: Math.Round(p95, 1),
            P99Ms: Math.Round(p99, 1),
            OldestPendingAgeSec: Math.Round(oldestPendingAgeSec, 0),
            TopDeadLetterReasons: topReasons);
    }

    public sealed record OutboundSendLatencyMetrics(
        DateTime FromUtc,
        int Samples,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        int QueuedCount,
        double OldestQueuedAgeSec,
        IReadOnlyDictionary<string, int> TopFailureCodes);

    public async Task<OutboundSendLatencyMetrics> GetOutboundLatencyAsync(TenantDbContext tenantDb, Guid tenantId, int days, CancellationToken ct)
    {
        var safeDays = Math.Clamp(days, 1, 365);
        var fromUtc = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        var queuedRows = await tenantDb.Messages.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == "Queued" && x.CreatedAtUtc >= fromUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.CreatedAtUtc)
            .Take(5000)
            .ToListAsync(ct);
        var queuedCount = queuedRows.Count;
        var oldestQueuedAgeSec = queuedRows.Count == 0 ? 0 : Math.Max(0, (DateTime.UtcNow - queuedRows[0]).TotalSeconds);

        // Latency samples: queued -> first non-queued state transition for same message.
        var eventRows = await tenantDb.MessageEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId &&
                        x.Direction == "outbound" &&
                        x.MessageId != null &&
                        x.CreatedAtUtc >= fromUtc)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20000)
            .Select(x => new { x.MessageId, x.State, x.StatePriority, x.EventTimestampUtc, x.CreatedAtUtc, x.RawPayloadJson })
            .ToListAsync(ct);

        var perMessage = new Dictionary<Guid, (DateTime? queuedAt, DateTime? firstProgressAt)>();
        var failureCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in eventRows)
        {
            if (ev.MessageId is null) continue;
            var mid = ev.MessageId.Value;
            perMessage.TryGetValue(mid, out var curr);

            var at = ev.EventTimestampUtc ?? ev.CreatedAtUtc;
            if (string.Equals(ev.State, "Queued", StringComparison.OrdinalIgnoreCase) || ev.StatePriority == 10)
            {
                if (curr.queuedAt is null || at < curr.queuedAt) curr.queuedAt = at;
            }
            else if (ev.StatePriority > 10)
            {
                if (curr.firstProgressAt is null || at < curr.firstProgressAt) curr.firstProgressAt = at;
            }

            if (string.Equals(ev.State, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                var code = ExtractFirstErrorCode(ev.RawPayloadJson);
                if (string.IsNullOrWhiteSpace(code)) code = "unknown";
                failureCodes.TryGetValue(code, out var c);
                failureCodes[code] = c + 1;
            }

            perMessage[mid] = curr;
        }

        var samples = perMessage.Values
            .Where(x => x.queuedAt.HasValue && x.firstProgressAt.HasValue)
            .Select(x => (x.firstProgressAt!.Value - x.queuedAt!.Value).TotalMilliseconds)
            .Where(x => x >= 0 && x <= 1000 * 60 * 15) // cap at 15 minutes
            .OrderBy(x => x)
            .ToList();

        var p50 = Percentile(samples, 50);
        var p95 = Percentile(samples, 95);
        var p99 = Percentile(samples, 99);

        var topFailureCodes = failureCodes
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return new OutboundSendLatencyMetrics(
            FromUtc: fromUtc,
            Samples: samples.Count,
            P50Ms: Math.Round(p50, 1),
            P95Ms: Math.Round(p95, 1),
            P99Ms: Math.Round(p99, 1),
            QueuedCount: queuedCount,
            OldestQueuedAgeSec: Math.Round(oldestQueuedAgeSec, 0),
            TopFailureCodes: topFailureCodes);
    }

    public sealed record TenantProductivityMetrics(
        DateTime FromUtc,
        int Days,
        IReadOnlyDictionary<string, int> TemplateStatus,
        int OptOutsNew,
        int OptOutsActive,
        int BroadcastJobs,
        int BroadcastCompleted,
        int BroadcastFailed,
        int BroadcastSent,
        int BroadcastFailedCount,
        double BroadcastAvgDurationSec,
        int AutomationRuns,
        int AutomationCompleted,
        int AutomationFailed,
        double AutomationAvgDurationSec);

    public async Task<TenantProductivityMetrics> GetTenantProductivityAsync(TenantDbContext tenantDb, Guid tenantId, int days, CancellationToken ct)
    {
        var safeDays = Math.Clamp(days, 1, 365);
        var fromUtc = DateTime.UtcNow.Date.AddDays(-safeDays + 1);

        var templates = await tenantDb.Templates.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.Status)
            .ToListAsync(ct);
        var templateStatus = templates
            .GroupBy(x => string.IsNullOrWhiteSpace(x) ? "unknown" : x.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var optOutsNew = await tenantDb.SmsOptOuts.AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.CreatedAtUtc >= fromUtc, ct);
        var optOutsActive = await tenantDb.SmsOptOuts.AsNoTracking()
            .CountAsync(x => x.TenantId == tenantId && x.IsActive, ct);

        var broadcasts = await tenantDb.BroadcastJobs.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.CreatedAtUtc >= fromUtc)
            .Select(x => new { x.Status, x.SentCount, x.FailedCount, x.StartedAtUtc, x.CompletedAtUtc })
            .ToListAsync(ct);
        var broadcastJobs = broadcasts.Count;
        var broadcastCompleted = broadcasts.Count(x => string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase));
        var broadcastFailed = broadcasts.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var broadcastSent = broadcasts.Sum(x => x.SentCount);
        var broadcastFailedCount = broadcasts.Sum(x => x.FailedCount);
        var broadcastDurations = broadcasts
            .Where(x => x.StartedAtUtc.HasValue && x.CompletedAtUtc.HasValue)
            .Select(x => (x.CompletedAtUtc!.Value - x.StartedAtUtc!.Value).TotalSeconds)
            .Where(x => x >= 0 && x <= 60 * 60 * 24)
            .ToList();
        var broadcastAvgDuration = broadcastDurations.Count == 0 ? 0 : broadcastDurations.Average();

        var runs = await tenantDb.AutomationRuns.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StartedAtUtc >= fromUtc)
            .Select(x => new { x.Status, x.StartedAtUtc, x.CompletedAtUtc })
            .ToListAsync(ct);
        var automationRuns = runs.Count;
        var automationCompleted = runs.Count(x => string.Equals(x.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
                                                string.Equals(x.Status, "Success", StringComparison.OrdinalIgnoreCase));
        var automationFailed = runs.Count(x => string.Equals(x.Status, "Failed", StringComparison.OrdinalIgnoreCase));
        var runDurations = runs
            .Where(x => x.CompletedAtUtc.HasValue)
            .Select(x => (x.CompletedAtUtc!.Value - x.StartedAtUtc).TotalSeconds)
            .Where(x => x >= 0 && x <= 60 * 60)
            .ToList();
        var runAvgDuration = runDurations.Count == 0 ? 0 : runDurations.Average();

        return new TenantProductivityMetrics(
            FromUtc: fromUtc,
            Days: safeDays,
            TemplateStatus: templateStatus,
            OptOutsNew: optOutsNew,
            OptOutsActive: optOutsActive,
            BroadcastJobs: broadcastJobs,
            BroadcastCompleted: broadcastCompleted,
            BroadcastFailed: broadcastFailed,
            BroadcastSent: broadcastSent,
            BroadcastFailedCount: broadcastFailedCount,
            BroadcastAvgDurationSec: Math.Round(broadcastAvgDuration, 1),
            AutomationRuns: automationRuns,
            AutomationCompleted: automationCompleted,
            AutomationFailed: automationFailed,
            AutomationAvgDurationSec: Math.Round(runAvgDuration, 1));
    }

    public static string ToCsv(params (string Key, object? Value)[] fields)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", fields.Select(x => EscapeCsv(x.Key))));
        sb.AppendLine(string.Join(",", fields.Select(x => EscapeCsv(x.Value))));
        return sb.ToString();
    }

    private static string EscapeCsv(object? value)
    {
        var s = value?.ToString() ?? string.Empty;
        if (s.Contains('"')) s = s.Replace("\"", "\"\"");
        var mustQuote = s.Contains(',') || s.Contains('\n') || s.Contains('\r');
        return mustQuote ? $"\"{s}\"" : s;
    }

    private string NormalizeReason(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return "unknown";
        // Avoid leaking secrets from exception messages.
        text = redactor.RedactText(text);
        if (text.Length > 140) text = text[..140];
        return text;
    }

    private static double Percentile(IReadOnlyList<double> sorted, int p)
    {
        if (sorted.Count == 0) return 0;
        if (p <= 0) return sorted[0];
        if (p >= 100) return sorted[^1];
        var rank = (p / 100.0) * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high) return sorted[low];
        var w = rank - low;
        return sorted[low] * (1 - w) + sorted[high] * w;
    }

    private static string ExtractFirstErrorCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var e = errors[0];
                if (e.TryGetProperty("code", out var code)) return code.ToString();
            }
            if (root.TryGetProperty("provider", out _))
            {
                if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
                {
                    if (payload.TryGetProperty("code", out var code2)) return code2.ToString();
                }
            }
            return string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

