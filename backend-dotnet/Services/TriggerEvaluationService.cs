using System.Text.Json;
using Textzy.Api.Models;

namespace Textzy.Api.Services;

public class TriggerEvaluationService
{
    public sealed class ShadowMatch
    {
        public Guid FlowId { get; init; }
        public bool Matched { get; init; }
        public int MatchScore { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    public Dictionary<Guid, ShadowMatch> EvaluateShadowMatches(
        IReadOnlyCollection<AutomationFlow> flows,
        IReadOnlyDictionary<Guid, string> definitionByFlowId,
        string inboundText)
    {
        var result = new Dictionary<Guid, ShadowMatch>();
        foreach (var flow in flows)
        {
            var eval = EvaluateFlow(flow, definitionByFlowId.TryGetValue(flow.Id, out var def) ? def : null, inboundText);
            result[flow.Id] = new ShadowMatch
            {
                FlowId = flow.Id,
                Matched = eval.Matched,
                MatchScore = eval.Score,
                Reason = eval.Reason
            };
        }
        return result;
    }

    private static (bool Matched, int Score, string Reason) EvaluateFlow(AutomationFlow flow, string? definitionJson, string inboundText)
    {
        var triggerType = (flow.TriggerType ?? string.Empty).Trim().ToLowerInvariant();
        if (triggerType is not ("keyword" or "intent"))
            return (false, 0, "unsupported_trigger_type");

        var text = NormalizeMatchText(inboundText);
        if (string.IsNullOrWhiteSpace(text))
            return (false, 0, "empty_inbound_text");

        if (TryMatchFromJson(flow.TriggerConfigJson, text, out var flowReason, out var flowScore))
            return (true, flowScore, $"flow_config:{flowReason}");

        if (!string.IsNullOrWhiteSpace(definitionJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(definitionJson);
                if (doc.RootElement.TryGetProperty("trigger", out var triggerNode))
                {
                    if (TryMatchFromJson(triggerNode.GetRawText(), text, out var defReason, out var defScore))
                        return (true, defScore, $"definition:{defReason}");
                }
            }
            catch
            {
                return (false, 0, "definition_parse_error");
            }
        }

        return (false, 0, "no_match");
    }

    private static bool TryMatchFromJson(string json, string inbound, out string reason, out int score)
    {
        reason = "no_match";
        score = 0;
        try
        {
            using var cfgDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = cfgDoc.RootElement;
            var mode = root.TryGetProperty("match", out var modeNode)
                ? (modeNode.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                : "contains";

            if (root.TryGetProperty("keywords", out var keywordsNode) && keywordsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in keywordsNode.EnumerateArray())
                {
                    var keyword = k.ToString() ?? string.Empty;
                    if (MatchByMode(inbound, keyword, mode))
                    {
                        reason = $"keywords_array:{mode}";
                        score = mode == "exact" ? 100 : mode.StartsWith("starts", StringComparison.OrdinalIgnoreCase) ? 80 : 60;
                        return true;
                    }
                }
            }

            if (root.TryGetProperty("keywords", out var keywordsCsvNode) && keywordsCsvNode.ValueKind == JsonValueKind.String)
            {
                foreach (var raw in (keywordsCsvNode.GetString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (MatchByMode(inbound, raw, mode))
                    {
                        reason = $"keywords_csv:{mode}";
                        score = mode == "exact" ? 100 : mode.StartsWith("starts", StringComparison.OrdinalIgnoreCase) ? 80 : 60;
                        return true;
                    }
                }
            }

            if (root.TryGetProperty("keyword", out var keywordNode))
            {
                if (MatchByMode(inbound, keywordNode.ToString() ?? string.Empty, mode))
                {
                    reason = $"keyword:{mode}";
                    score = mode == "exact" ? 100 : mode.StartsWith("starts", StringComparison.OrdinalIgnoreCase) ? 80 : 60;
                    return true;
                }
            }

            if (root.TryGetProperty("triggerKeywords", out var triggerKeywordsNode) && triggerKeywordsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var k in triggerKeywordsNode.EnumerateArray())
                {
                    if (MatchByMode(inbound, k.ToString() ?? string.Empty, mode))
                    {
                        reason = $"trigger_keywords:{mode}";
                        score = mode == "exact" ? 100 : mode.StartsWith("starts", StringComparison.OrdinalIgnoreCase) ? 80 : 60;
                        return true;
                    }
                }
            }
        }
        catch
        {
            reason = "trigger_parse_error";
            score = 0;
            return false;
        }
        return false;
    }

    private static string NormalizeMatchText(string value)
    {
        var s = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var chars = s.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray();
        return new string(chars).Trim();
    }

    private static bool MatchByMode(string text, string keyword, string mode)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword)) return false;
        var t = NormalizeMatchText(text);
        var k = NormalizeMatchText(keyword);
        if (string.IsNullOrWhiteSpace(t) || string.IsNullOrWhiteSpace(k)) return false;
        return mode switch
        {
            "exact" => string.Equals(t, k, StringComparison.OrdinalIgnoreCase),
            "starts" or "starts_with" => t.StartsWith(k, StringComparison.OrdinalIgnoreCase),
            _ => t.Contains(k, StringComparison.OrdinalIgnoreCase)
        };
    }
}

