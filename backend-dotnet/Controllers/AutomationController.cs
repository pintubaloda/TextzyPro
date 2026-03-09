using System.Text.Json;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Textzy.Api.Data;
using Textzy.Api.DTOs;
using Textzy.Api.Models;
using Textzy.Api.Services;
using static Textzy.Api.Services.PermissionCatalog;

namespace Textzy.Api.Controllers;

[ApiController]
[Route("api/automation")]
public class AutomationController(
    TenantDbContext db,
    ControlDbContext controlDb,
    TenancyContext tenancy,
    AuthContext auth,
    RbacService rbac,
    MessagingService messaging,
    WhatsAppCloudService whatsapp,
    BillingGuardService billingGuard,
    SecretCryptoService crypto,
    IHttpClientFactory httpClientFactory,
    SensitiveDataRedactor redactor,
    WorkflowExecutionEngine workflowExecutionEngine,
    ILogger<AutomationController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SupportedPublishNodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "start",
        "text","text_message","textmessage","send_text","message",
        "media",
        "template",
        "buttons",
        "list",
        "ask_question",
        "capture_input",
        "location",
        "form",
        "bot_reply","botreply",
        "cta_url",
        "condition","split",
        "delay","wait",
        "assign_agent","assignagent","handoff",
        "request_intervention","requesthelp",
        "tag_user","taguser",
        "webhook","api_call",
        "jump",
        "end"
    };

    private void EnsureAutomationSchema()
    {
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "AutomationFlows" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Description" text NOT NULL DEFAULT '', "Channel" text NOT NULL DEFAULT 'waba', "TriggerType" text NOT NULL DEFAULT 'keyword', "TriggerConfigJson" text NOT NULL DEFAULT '{{}}', "IsActive" boolean NOT NULL DEFAULT true, "LifecycleStatus" text NOT NULL DEFAULT 'draft', "CurrentVersionId" uuid NULL, "PublishedVersionId" uuid NULL, "LastPublishedAtUtc" timestamp with time zone NULL, "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "Description" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "Channel" text NOT NULL DEFAULT 'waba';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "TriggerConfigJson" text NOT NULL DEFAULT '{{}}';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "LifecycleStatus" text NOT NULL DEFAULT 'draft';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "CurrentVersionId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "PublishedVersionId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "LastPublishedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationFlows" ADD COLUMN IF NOT EXISTS "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now();""");

        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "AutomationFlowVersions" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NOT NULL, "VersionNumber" integer NOT NULL DEFAULT 1, "Status" text NOT NULL DEFAULT 'draft', "DefinitionJson" text NOT NULL DEFAULT '{{}}', "ChangeNote" text NOT NULL DEFAULT '', "IsStagedRelease" boolean NOT NULL DEFAULT false, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "PublishedAtUtc" timestamp with time zone NULL);""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AutomationFlowVersions_FlowId" ON "AutomationFlowVersions" ("FlowId");""");

        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "AutomationNodes" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NOT NULL, "VersionId" uuid NULL, "NodeKey" text NOT NULL DEFAULT '', "NodeType" text NOT NULL DEFAULT '', "Name" text NOT NULL DEFAULT '', "ConfigJson" text NOT NULL DEFAULT '', "EdgesJson" text NOT NULL DEFAULT '[]', "Sequence" integer NOT NULL DEFAULT 0, "IsReusable" boolean NOT NULL DEFAULT false);""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationNodes" ADD COLUMN IF NOT EXISTS "VersionId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationNodes" ADD COLUMN IF NOT EXISTS "NodeKey" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationNodes" ADD COLUMN IF NOT EXISTS "Name" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationNodes" ADD COLUMN IF NOT EXISTS "EdgesJson" text NOT NULL DEFAULT '[]';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationNodes" ADD COLUMN IF NOT EXISTS "IsReusable" boolean NOT NULL DEFAULT false;""");

        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "AutomationRuns" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NOT NULL, "VersionId" uuid NULL, "Mode" text NOT NULL DEFAULT 'live', "TriggerType" text NOT NULL DEFAULT '', "IdempotencyKey" text NOT NULL DEFAULT '', "TriggerPayloadJson" text NOT NULL DEFAULT '{{}}', "Status" text NOT NULL DEFAULT 'Started', "Log" text NOT NULL DEFAULT '', "TraceJson" text NOT NULL DEFAULT '[]', "FailureReason" text NOT NULL DEFAULT '', "RetryCount" integer NOT NULL DEFAULT 0, "StartedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "CompletedAtUtc" timestamp with time zone NULL);""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "VersionId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "Mode" text NOT NULL DEFAULT 'live';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "TriggerType" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "IdempotencyKey" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "TraceJson" text NOT NULL DEFAULT '[]';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "FailureReason" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "AutomationRuns" ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0;""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AutomationRuns_IdempotencyKey" ON "AutomationRuns" ("IdempotencyKey");""");

        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "AutomationApprovals" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NOT NULL, "VersionId" uuid NOT NULL, "RequestedBy" text NOT NULL DEFAULT '', "RequestedByRole" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'pending', "DecisionComment" text NOT NULL DEFAULT '', "DecidedBy" text NOT NULL DEFAULT '', "RequestedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "DecidedAtUtc" timestamp with time zone NULL);""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AutomationApprovals_FlowId" ON "AutomationApprovals" ("FlowId");""");

        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "AutomationUsageCounters" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "BucketDateUtc" timestamp with time zone NOT NULL DEFAULT now(), "RunCount" integer NOT NULL DEFAULT 0, "ApiCallCount" integer NOT NULL DEFAULT 0, "ActiveFlowCount" integer NOT NULL DEFAULT 0, "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AutomationUsageCounters_Tenant_Bucket" ON "AutomationUsageCounters" ("TenantId","BucketDateUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FlowRuntimeEvents" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NULL, "MetaFlowId" text NOT NULL DEFAULT '', "ConversationExternalId" text NOT NULL DEFAULT '', "CustomerPhone" text NOT NULL DEFAULT '', "EventType" text NOT NULL DEFAULT '', "EventSource" text NOT NULL DEFAULT '', "Success" boolean NOT NULL DEFAULT true, "StatusCode" integer NOT NULL DEFAULT 0, "DurationMs" integer NOT NULL DEFAULT 0, "ScreenId" text NOT NULL DEFAULT '', "ActionName" text NOT NULL DEFAULT '', "PayloadJson" text NOT NULL DEFAULT '{}', "ErrorDetail" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FlowRuntimeEvents_Tenant_CreatedAt" ON "FlowRuntimeEvents" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FlowRuntimeEvents_MetaFlowId" ON "FlowRuntimeEvents" ("MetaFlowId");""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FaqKnowledgeItems" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Question" text NOT NULL DEFAULT '', "Answer" text NOT NULL DEFAULT '', "Category" text NOT NULL DEFAULT '', "IsActive" boolean NOT NULL DEFAULT true, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FaqKnowledgeItems_Tenant_Active" ON "FaqKnowledgeItems" ("TenantId","IsActive");""");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TriggerEvaluationAudit" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "FlowId" uuid NULL,
                "InboundMessageId" text NOT NULL DEFAULT '',
                "ConversationId" uuid NULL,
                "MessageText" text NOT NULL DEFAULT '',
                "TriggerType" text NOT NULL DEFAULT '',
                "IsMatch" boolean NOT NULL DEFAULT false,
                "MatchScore" integer NOT NULL DEFAULT 0,
                "Reason" text NOT NULL DEFAULT '',
                "EvaluatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_TenantId" ON "TriggerEvaluationAudit" ("TenantId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_EvaluatedAtUtc" ON "TriggerEvaluationAudit" ("EvaluatedAtUtc");""");
    }

    private bool TryEnsureAutomationSchema(out IActionResult? errorResult)
    {
        try
        {
            EnsureAutomationSchema();
            errorResult = null;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError("Automation schema ensure failed for tenant {TenantId}: {Error}", tenancy.TenantId, redactor.RedactText(ex.Message));
            errorResult = StatusCode(500, new
            {
                error = "automation_schema_init_failed",
                message = "Automation DB initialization failed."
            });
            return false;
        }
    }

    [HttpGet("catalogs/node-types")]
    public IActionResult NodeTypes()
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        return Ok(new[]
        {
            new { type = "start", category = "trigger", reusable = false },
            new { type = "text", category = "message", reusable = true },
            new { type = "media", category = "message", reusable = true },
            new { type = "template", category = "message", reusable = true },
            new { type = "bot_reply", category = "message", reusable = true },
            new { type = "cta_url", category = "message", reusable = true },
            new { type = "buttons", category = "message", reusable = true },
            new { type = "list", category = "message", reusable = true },
            new { type = "ask_question", category = "input", reusable = true },
            new { type = "capture_input", category = "input", reusable = true },
            new { type = "location", category = "input", reusable = true },
            new { type = "form", category = "input", reusable = true },
            new { type = "condition", category = "logic", reusable = true },
            new { type = "split", category = "logic", reusable = true },
            new { type = "delay", category = "logic", reusable = true },
            new { type = "jump", category = "logic", reusable = true },
            new { type = "api_call", category = "integration", reusable = true },
            new { type = "db_query", category = "integration", reusable = true },
            new { type = "function", category = "compute", reusable = true },
            new { type = "webhook", category = "integration", reusable = true },
            new { type = "handoff", category = "operator", reusable = true },
            new { type = "request_intervention", category = "operator", reusable = true },
            new { type = "tag_user", category = "operator", reusable = true },
            new { type = "subflow", category = "flow", reusable = true },
            new { type = "end", category = "flow", reusable = false }
        });
    }

    [HttpGet("limits")]
    public IActionResult Limits()
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        var limits = GetLimits();
        if (!TryEnsureAutomationSchema(out _))
        {
            return Ok(new
            {
                limits,
                usage = new
                {
                    runsToday = 0,
                    apiCallsToday = 0,
                    activeFlows = 0,
                    bucketDateUtc = DateTime.UtcNow.Date
                }
            });
        }

        try
        {
            var usage = GetOrCreateUsageCounter();
            return Ok(new
            {
                limits,
                usage = new
                {
                    runsToday = usage.RunCount,
                    apiCallsToday = usage.ApiCallCount,
                    activeFlows = db.AutomationFlows.Count(x => x.TenantId == tenancy.TenantId && x.IsActive),
                    bucketDateUtc = usage.BucketDateUtc
                }
            });
        }
        catch (Exception ex)
        {
            logger.LogError("Automation limits query failed for tenant {TenantId}: {Error}", tenancy.TenantId, redactor.RedactText(ex.Message));
            return Ok(new
            {
                limits,
                usage = new
                {
                    runsToday = 0,
                    apiCallsToday = 0,
                    activeFlows = 0,
                    bucketDateUtc = DateTime.UtcNow.Date
                },
                warning = "automation_limits_query_failed"
            });
        }
    }

    [HttpGet("debug/tenant-flow-counts")]
    public async Task<IActionResult> DebugTenantFlowCounts(CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        if (tenancy.TenantId == Guid.Empty) return BadRequest("Tenant context missing.");

        List<object> automationFlows = [];
        List<object> smsFlows = [];
        string automationError = string.Empty;
        string smsError = string.Empty;

        try
        {
            automationFlows = await db.AutomationFlows
                .AsNoTracking()
                .Where(x => x.TenantId == tenancy.TenantId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Select(x => (object)new
                {
                    x.Id,
                    x.Name,
                    x.IsActive,
                    x.LifecycleStatus,
                    x.UpdatedAtUtc
                })
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            automationError = redactor.RedactText(ex.Message);
        }

        try
        {
            smsFlows = await db.SmsFlows
                .AsNoTracking()
                .Where(x => x.TenantId == tenancy.TenantId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (object)new
                {
                    x.Id,
                    x.Name,
                    x.Status,
                    x.CreatedAtUtc
                })
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            smsError = redactor.RedactText(ex.Message);
        }

        var automationPublished = automationFlows.Count(x =>
        {
            if (x is not null)
            {
                var prop = x.GetType().GetProperty("LifecycleStatus");
                var val = prop?.GetValue(x)?.ToString() ?? string.Empty;
                return string.Equals(val, "published", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        });
        var automationActive = automationFlows.Count(x =>
        {
            if (x is not null)
            {
                var prop = x.GetType().GetProperty("IsActive");
                var val = prop?.GetValue(x);
                return val is bool b && b;
            }
            return false;
        });

        return Ok(new
        {
            tenantId = tenancy.TenantId,
            tenantSlug = tenancy.TenantSlug,
            ok = string.IsNullOrWhiteSpace(automationError) && string.IsNullOrWhiteSpace(smsError),
            errors = new
            {
                automation = automationError,
                sms = smsError
            },
            counts = new
            {
                automationFlows = automationFlows.Count,
                smsFlows = smsFlows.Count,
                automationPublished,
                automationActive
            },
            automationFlows = automationFlows.Take(50),
            smsFlows = smsFlows.Take(50),
            checkedAtUtc = DateTime.UtcNow
        });
    }

    [HttpPost("flows")]
    public async Task<IActionResult> CreateFlow([FromBody] CreateAutomationFlowRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Flow name is required.");
        var currentFlows = await db.AutomationFlows.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
        var flowLimit = await billingGuard.CheckLimitAsync(tenancy.TenantId, "flows", currentFlows + 1, ct);
        if (!flowLimit.Allowed) return BadRequest(flowLimit.Message);

        var flow = new AutomationFlow
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            Name = req.Name.Trim(),
            Description = req.Description.Trim(),
            Channel = NormalizeChannel(req.Channel),
            TriggerType = NormalizeTrigger(req.TriggerType),
            TriggerConfigJson = NormalizeJson(req.TriggerConfigJson, "{}"),
            LifecycleStatus = "draft",
            IsActive = true
        };

        var version = new AutomationFlowVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            FlowId = flow.Id,
            VersionNumber = 1,
            Status = "draft",
            DefinitionJson = NormalizeJson(req.DefinitionJson, BuildDefaultDefinition(flow.TriggerType)),
            ChangeNote = "Initial draft"
        };
        flow.CurrentVersionId = version.Id;

        db.AutomationFlows.Add(flow);
        db.AutomationFlowVersions.Add(version);
        await db.SaveChangesAsync(ct);
        await SyncAutomationUsageAsync(ct);
        return Ok(new { flow, version });
    }

    [HttpGet("trigger-audit")]
    public async Task<IActionResult> TriggerAudit(
        [FromQuery] Guid? flowId,
        [FromQuery] string? inboundMessageId,
        [FromQuery] bool? matched,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();

        var safeTake = Math.Clamp(take, 1, 500);
        var q = db.TriggerEvaluationAudit
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId);

        if (flowId.HasValue) q = q.Where(x => x.FlowId == flowId.Value);
        if (!string.IsNullOrWhiteSpace(inboundMessageId)) q = q.Where(x => x.InboundMessageId == inboundMessageId);
        if (matched.HasValue) q = q.Where(x => x.IsMatch == matched.Value);

        var rows = await q
            .OrderByDescending(x => x.EvaluatedAtUtc)
            .Take(safeTake)
            .Select(x => new
            {
                x.Id,
                x.FlowId,
                x.InboundMessageId,
                x.MessageText,
                x.TriggerType,
                x.IsMatch,
                x.MatchScore,
                x.Reason,
                x.EvaluatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("trigger-audit/summary")]
    public async Task<IActionResult> TriggerAuditSummary([FromQuery] int days = 7, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        var safeDays = Math.Clamp(days, 1, 30);
        var fromUtc = DateTime.UtcNow.AddDays(-safeDays);

        var rows = await db.TriggerEvaluationAudit
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.EvaluatedAtUtc >= fromUtc)
            .GroupBy(x => new { x.IsMatch, x.Reason })
            .Select(g => new
            {
                g.Key.IsMatch,
                g.Key.Reason,
                count = g.Count()
            })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var total = rows.Sum(x => x.count);
        var matched = rows.Where(x => x.IsMatch).Sum(x => x.count);
        var unmatched = total - matched;
        var rate = total == 0 ? 0d : Math.Round((double)matched * 100d / total, 2);

        return Ok(new
        {
            windowDays = safeDays,
            total,
            matched,
            unmatched,
            matchRate = rate,
            reasons = rows
        });
    }

    [HttpGet("flows")]
    public async Task<IActionResult> ListFlows(CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        var schemaReady = TryEnsureAutomationSchema(out var schemaError);
        if (!schemaReady)
        {
            logger.LogWarning(
                "Automation schema ensure failed for tenant {TenantId}; continuing with best-effort flow read. Error={Error}",
                tenancy.TenantId,
                schemaError?.ToString() ?? "unknown");
        }

        List<AutomationFlow> flows;
        try
        {
            flows = await db.AutomationFlows
                .AsNoTracking()
                .Where(x => x.TenantId == tenancy.TenantId)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToListAsync(ct);

            if (flows.Count == 0)
            {
                var repaired = await TryRebindLegacyAutomationRowsAsync(ct);
                if (repaired > 0)
                {
                    flows = await db.AutomationFlows
                        .AsNoTracking()
                        .Where(x => x.TenantId == tenancy.TenantId)
                        .OrderByDescending(x => x.UpdatedAtUtc)
                        .ToListAsync(ct);
                }

                if (flows.Count == 0)
                {
                    var imported = await TryImportLegacySmsFlowsAsync(ct);
                    if (imported > 0)
                    {
                        flows = await db.AutomationFlows
                            .AsNoTracking()
                            .Where(x => x.TenantId == tenancy.TenantId)
                            .OrderByDescending(x => x.UpdatedAtUtc)
                            .ToListAsync(ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Automation flows primary query failed for tenant {TenantId}: {Error}", tenancy.TenantId, redactor.RedactText(ex.Message));
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "automation_flows_query_failed",
                message = "Unable to load automation flows."
            });
        }

        var flowIds = flows.Select(x => x.Id).ToList();

        var versions = new Dictionary<Guid, (int totalVersions, int latestVersion)>();
        try
        {
            var versionRows = await db.AutomationFlowVersions
                .AsNoTracking()
                .Where(x => x.TenantId == tenancy.TenantId && flowIds.Contains(x.FlowId))
                .GroupBy(x => x.FlowId)
                .Select(g => new
                {
                    flowId = g.Key,
                    totalVersions = g.Count(),
                    latestVersion = g.Max(x => x.VersionNumber)
                })
                .ToListAsync(ct);
            versions = versionRows.ToDictionary(x => x.flowId, x => (x.totalVersions, x.latestVersion));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Automation flow versions query failed for tenant {TenantId}: {Error}", tenancy.TenantId, redactor.RedactText(ex.Message));
        }

        var runs = new Dictionary<Guid, (int runs, int failed, DateTime? lastRunAtUtc)>();
        try
        {
            var runRows = await db.AutomationRuns
                .AsNoTracking()
                .Where(x => x.TenantId == tenancy.TenantId && flowIds.Contains(x.FlowId))
                .GroupBy(x => x.FlowId)
                .Select(g => new
                {
                    flowId = g.Key,
                    runs = g.Count(),
                    failed = g.Count(x => x.Status == "failed"),
                    lastRunAtUtc = g.Max(x => x.StartedAtUtc)
                })
                .ToListAsync(ct);
            runs = runRows.ToDictionary(x => x.flowId, x => (x.runs, x.failed, (DateTime?)x.lastRunAtUtc));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Automation flow runs query failed for tenant {TenantId}: {Error}", tenancy.TenantId, redactor.RedactText(ex.Message));
        }

        return Ok(flows.Select(flow =>
        {
            versions.TryGetValue(flow.Id, out var v);
            runs.TryGetValue(flow.Id, out var r);
            var runsCount = r.runs;
            var failedCount = r.failed;
            var successRate = runsCount == 0 ? 100 : Math.Round(((double)(runsCount - failedCount) / runsCount) * 100, 2);
            return new
            {
                flow.Id,
                flow.Name,
                flow.Description,
                flow.Channel,
                flow.TriggerType,
                flow.IsActive,
                flow.LifecycleStatus,
                flow.CurrentVersionId,
                flow.PublishedVersionId,
                flow.LastPublishedAtUtc,
                flow.UpdatedAtUtc,
                flow.CreatedAtUtc,
                versionCount = v.totalVersions,
                latestVersion = v.latestVersion,
                runs = runsCount,
                successRate,
                lastRunAtUtc = r.lastRunAtUtc
            };
        }));
    }

    private async Task<int> TryRebindLegacyAutomationRowsAsync(CancellationToken ct)
    {
        // Backward-compatibility repair:
        // some legacy rows were inserted with TenantId=Guid.Empty and became invisible to tenant-scoped queries.
        var legacyFlowCount = await db.AutomationFlows.CountAsync(x => x.TenantId == Guid.Empty, ct);
        if (legacyFlowCount == 0) return 0;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var totalUpdated = 0;
        totalUpdated += await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AutomationFlows" SET "TenantId" = {tenancy.TenantId} WHERE "TenantId" = {Guid.Empty};""", ct);
        totalUpdated += await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AutomationFlowVersions" SET "TenantId" = {tenancy.TenantId} WHERE "TenantId" = {Guid.Empty};""", ct);
        totalUpdated += await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AutomationNodes" SET "TenantId" = {tenancy.TenantId} WHERE "TenantId" = {Guid.Empty};""", ct);
        totalUpdated += await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AutomationRuns" SET "TenantId" = {tenancy.TenantId} WHERE "TenantId" = {Guid.Empty};""", ct);
        totalUpdated += await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "AutomationApprovals" SET "TenantId" = {tenancy.TenantId} WHERE "TenantId" = {Guid.Empty};""", ct);
        await tx.CommitAsync(ct);

        try
        {
            await SyncAutomationUsageAsync(ct);
        }
        catch
        {
            // usage sync is best-effort; flow list should still work.
        }

        logger.LogWarning(
            "Rebound {LegacyFlowCount} legacy automation flows (updated rows: {TotalUpdated}) to tenant {TenantId}",
            legacyFlowCount,
            totalUpdated,
            tenancy.TenantId);

        return legacyFlowCount;
    }

    private async Task<int> TryImportLegacySmsFlowsAsync(CancellationToken ct)
    {
        var legacyRows = await db.SmsFlows
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        if (legacyRows.Count == 0) return 0;

        var existingNames = await db.AutomationFlows
            .AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId)
            .Select(x => x.Name)
            .ToListAsync(ct);
        var existingNameSet = new HashSet<string>(existingNames.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var importedCount = 0;
        foreach (var legacy in legacyRows)
        {
            var flowName = string.IsNullOrWhiteSpace(legacy.Name) ? $"Imported Flow {legacy.Id.ToString()[..8]}" : legacy.Name.Trim();
            if (existingNameSet.Contains(flowName)) continue;

            var legacyActive = string.Equals(legacy.Status, "Active", StringComparison.OrdinalIgnoreCase);
            var lifecycleStatus = legacyActive ? "published" : "draft";

            var flow = new AutomationFlow
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                Name = flowName,
                Description = "Imported from legacy SMS flow",
                Channel = "waba",
                TriggerType = "keyword",
                TriggerConfigJson = "{}",
                IsActive = legacyActive,
                LifecycleStatus = lifecycleStatus,
                CreatedAtUtc = legacy.CreatedAtUtc == default ? now : legacy.CreatedAtUtc,
                UpdatedAtUtc = now
            };

            var version = new AutomationFlowVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                FlowId = flow.Id,
                VersionNumber = 1,
                Status = lifecycleStatus,
                DefinitionJson = BuildDefaultDefinition(flow.TriggerType),
                ChangeNote = "Imported from legacy SMS flow",
                CreatedAtUtc = now,
                PublishedAtUtc = legacyActive ? now : null
            };

            flow.CurrentVersionId = version.Id;
            if (legacyActive)
            {
                flow.PublishedVersionId = version.Id;
                flow.LastPublishedAtUtc = now;
            }

            db.AutomationFlows.Add(flow);
            db.AutomationFlowVersions.Add(version);
            existingNameSet.Add(flowName);
            importedCount++;
        }

        if (importedCount > 0)
        {
            await db.SaveChangesAsync(ct);
            try
            {
                await SyncAutomationUsageAsync(ct);
            }
            catch
            {
                // Best-effort sync. Listing is already fixed by imported rows.
            }
        }

        return importedCount;
    }

    [HttpGet("flows/{flowId:guid}")]
    public async Task<IActionResult> GetFlow(Guid flowId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        var schemaReady = TryEnsureAutomationSchema(out var schemaError);
        if (!schemaReady)
        {
            logger.LogWarning(
                "Automation schema ensure failed on GetFlow for tenant {TenantId}; continuing with best-effort read. Error={Error}",
                tenancy.TenantId,
                schemaError?.ToString() ?? "unknown");
        }
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        var versions = await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(ct);

        return Ok(new { flow, versions });
    }

    [HttpPut("flows/{flowId:guid}")]
    public async Task<IActionResult> UpdateFlow(Guid flowId, [FromBody] UpdateAutomationFlowRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        flow.Name = string.IsNullOrWhiteSpace(req.Name) ? flow.Name : req.Name.Trim();
        flow.Description = req.Description?.Trim() ?? string.Empty;
        flow.Channel = NormalizeChannel(req.Channel);
        flow.TriggerType = NormalizeTrigger(req.TriggerType);
        flow.TriggerConfigJson = NormalizeJson(req.TriggerConfigJson, "{}");
        flow.IsActive = req.IsActive;
        flow.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(flow);
    }

    [HttpPost("flows/{flowId:guid}/unpublish")]
    public async Task<IActionResult> UnpublishFlow(Guid flowId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        var published = await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Status == "published")
            .ToListAsync(ct);
        foreach (var item in published) item.Status = "archived";

        flow.PublishedVersionId = null;
        flow.LifecycleStatus = "draft";
        flow.IsActive = false;
        flow.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await SyncAutomationUsageAsync(ct);
        return Ok(new { flowId, status = "unpublished" });
    }

    [HttpDelete("flows/{flowId:guid}")]
    public async Task<IActionResult> DeleteFlow(Guid flowId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NoContent();

        var nodes = await db.AutomationNodes.Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId).ToListAsync(ct);
        var versions = await db.AutomationFlowVersions.Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId).ToListAsync(ct);
        var runs = await db.AutomationRuns.Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId).ToListAsync(ct);
        var approvals = await db.AutomationApprovals.Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId).ToListAsync(ct);

        if (nodes.Count > 0) db.AutomationNodes.RemoveRange(nodes);
        if (versions.Count > 0) db.AutomationFlowVersions.RemoveRange(versions);
        if (runs.Count > 0) db.AutomationRuns.RemoveRange(runs);
        if (approvals.Count > 0) db.AutomationApprovals.RemoveRange(approvals);
        db.AutomationFlows.Remove(flow);

        await db.SaveChangesAsync(ct);
        await SyncAutomationUsageAsync(ct);
        return NoContent();
    }

    [HttpGet("flows/{flowId:guid}/versions")]
    public async Task<IActionResult> ListVersions(Guid flowId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        var schemaReady = TryEnsureAutomationSchema(out var schemaError);
        if (!schemaReady)
        {
            logger.LogWarning(
                "Automation schema ensure failed on ListVersions for tenant {TenantId}; continuing with best-effort read. Error={Error}",
                tenancy.TenantId,
                schemaError?.ToString() ?? "unknown");
        }
        var versions = await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(ct);
        return Ok(versions);
    }

    [HttpGet("flow-json-schema")]
    public IActionResult FlowJsonSchema()
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        return Ok(new
        {
            schemaVersion = 1,
            required = new[] { "version", "screens", "routing" },
            model = new
            {
                version = "1.0.0",
                screens = new object[]
                {
                    new
                    {
                        id = "welcome",
                        title = "Welcome",
                        components = new object[]
                        {
                            new { id = "name", type = "input", label = "Name", required = true },
                            new { id = "next", type = "button", text = "Continue", nextScreen = "summary" }
                        }
                    }
                },
                routing = new
                {
                    startScreen = "welcome",
                    edges = new object[]
                    {
                        new { from = "welcome", to = "summary", when = "name != ''" }
                    }
                },
                validations = new object[]
                {
                    new { field = "name", rule = "required", message = "Name is required." }
                },
                dynamicDataSources = new
                {
                    customer_lookup = new { url = "https://api.example.com/customer/lookup", method = "POST", headers = new { } }
                }
            }
        });
    }

    [HttpPost("flows/validate-definition")]
    public IActionResult ValidateDefinition([FromBody] ValidateFlowDefinitionRequest req)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var report = ValidateDefinitionInternal(req.DefinitionJson);
        return Ok(report);
    }

    [HttpPost("flows/{flowId:guid}/versions/{versionId:guid}/validate")]
    public async Task<IActionResult> ValidateVersion(Guid flowId, Guid versionId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        var version = await db.AutomationFlowVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Id == versionId, ct);
        if (version is null) return NotFound();
        var report = ValidateDefinitionInternal(version.DefinitionJson);
        return Ok(report);
    }

    [HttpGet("meta/flows")]
    public async Task<IActionResult> ListMetaFlows(CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        try
        {
            var raw = await whatsapp.ListMetaFlowsAsync(ct);
            var list = new List<object>();
            if (raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    list.Add(new
                    {
                        id = item.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        name = item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        status = item.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                        categories = item.TryGetProperty("categories", out var categories) ? categories : default,
                        jsonVersion = item.TryGetProperty("json_version", out var jsonVersion) ? jsonVersion.ToString() : string.Empty,
                        dataApiVersion = item.TryGetProperty("data_api_version", out var dataApiVersion) ? dataApiVersion.ToString() : string.Empty,
                        updatedTime = item.TryGetProperty("updated_time", out var updatedTime) ? updatedTime.ToString() : string.Empty
                    });
                }
            }
            return Ok(new
            {
                ok = true,
                count = list.Count,
                data = list,
                raw
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_list_failed", message = ex.Message });
        }
    }

    [HttpPost("meta/flows")]
    public async Task<IActionResult> CreateMetaFlow([FromBody] CreateMetaFlowRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        try
        {
            var outJson = await whatsapp.CreateMetaFlowAsync(
                req.Name,
                req.CategoriesJson,
                req.EndpointUri,
                req.DataApiVersion,
                req.JsonVersion,
                req.FlowJson,
                ct);
            return Ok(new { ok = true, result = outJson });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_create_failed", message = ex.Message });
        }
    }

    [HttpGet("meta/flows/{metaFlowId}")]
    public async Task<IActionResult> GetMetaFlow(string metaFlowId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        try
        {
            var raw = await whatsapp.GetMetaFlowAsync(metaFlowId, ct);
            var flowSchema = TryExtractMetaFlowSchema(raw);
            var screensCount = flowSchema.HasValue && flowSchema.Value.TryGetProperty("screens", out var screens) && screens.ValueKind == JsonValueKind.Array
                ? screens.GetArrayLength()
                : 0;
            return Ok(new
            {
                ok = true,
                id = raw.TryGetProperty("id", out var id) ? id.GetString() : string.Empty,
                name = raw.TryGetProperty("name", out var name) ? name.GetString() : string.Empty,
                status = raw.TryGetProperty("status", out var status) ? status.GetString() : string.Empty,
                screensCount,
                flowSchema = flowSchema,
                raw
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_fetch_failed", message = ex.Message });
        }
    }

    [HttpPut("meta/flows/{metaFlowId}")]
    public async Task<IActionResult> UpdateMetaFlow(string metaFlowId, [FromBody] UpdateMetaFlowRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(req.Name)) fields["name"] = req.Name.Trim();
        if (!string.IsNullOrWhiteSpace(req.EndpointUri)) fields["endpoint_uri"] = req.EndpointUri.Trim();
        if (!string.IsNullOrWhiteSpace(req.CategoriesJson)) fields["categories"] = req.CategoriesJson.Trim();
        if (!string.IsNullOrWhiteSpace(req.DataApiVersion)) fields["data_api_version"] = req.DataApiVersion.Trim();
        if (!string.IsNullOrWhiteSpace(req.JsonVersion)) fields["json_version"] = req.JsonVersion.Trim();
        if (!string.IsNullOrWhiteSpace(req.FlowJson)) fields["flow_json"] = req.FlowJson.Trim();
        if (!string.IsNullOrWhiteSpace(req.Status)) fields["status"] = req.Status.Trim();

        if (fields.Count == 0)
            return BadRequest("At least one update field is required.");

        try
        {
            var outJson = await whatsapp.UpdateMetaFlowAsync(metaFlowId, fields, ct);
            return Ok(new
            {
                ok = true,
                metaFlowId,
                updatedFields = fields.Keys.ToArray(),
                result = outJson
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_update_failed", message = ex.Message });
        }
    }

    [HttpDelete("meta/flows/{metaFlowId}")]
    public async Task<IActionResult> DeleteMetaFlow(string metaFlowId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        try
        {
            var outJson = await whatsapp.DeleteMetaFlowAsync(metaFlowId, ct);
            return Ok(new
            {
                ok = true,
                metaFlowId,
                result = outJson
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_delete_failed", message = ex.Message });
        }
    }

    [HttpPost("meta/flows/{metaFlowId}/publish")]
    public async Task<IActionResult> PublishMetaFlow(string metaFlowId, [FromBody] PublishMetaFlowRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        try
        {
            var outJson = await whatsapp.PublishMetaFlowAsync(metaFlowId, req.FlowJson ?? string.Empty, ct);
            return Ok(new
            {
                ok = true,
                metaFlowId,
                result = outJson
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_publish_failed", message = ex.Message });
        }
    }

    [HttpPost("flows/{flowId:guid}/import-meta")]
    public async Task<IActionResult> ImportMetaFlow(Guid flowId, [FromBody] ImportMetaFlowRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        if (string.IsNullOrWhiteSpace(req.MetaFlowId)) return BadRequest("MetaFlowId is required.");

        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        JsonElement raw;
        try
        {
            raw = await whatsapp.GetMetaFlowAsync(req.MetaFlowId, ct);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "meta_flow_fetch_failed", message = ex.Message });
        }

        if (!TryBuildDefinitionFromMetaFlow(raw, out var definitionJson, out var importedMetaName, out var importWarning))
            return BadRequest(new { error = "meta_flow_schema_not_supported", message = "Unable to parse flow schema/screens from Meta payload." });

        AutomationFlowVersion? updatedVersion;
        if (req.CreateNewVersion)
        {
            var latest = await db.AutomationFlowVersions
                .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId)
                .OrderByDescending(x => x.VersionNumber)
                .FirstOrDefaultAsync(ct);

            updatedVersion = new AutomationFlowVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenancy.TenantId,
                FlowId = flowId,
                VersionNumber = (latest?.VersionNumber ?? 0) + 1,
                Status = "draft",
                DefinitionJson = definitionJson,
                ChangeNote = string.IsNullOrWhiteSpace(req.ChangeNote)
                    ? $"Imported from Meta Flow {req.MetaFlowId}"
                    : req.ChangeNote.Trim()
            };
            db.AutomationFlowVersions.Add(updatedVersion);
            flow.CurrentVersionId = updatedVersion.Id;
        }
        else
        {
            updatedVersion = await ResolveVersion(flow, null, ct);
            if (updatedVersion is null)
            {
                updatedVersion = new AutomationFlowVersion
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenancy.TenantId,
                    FlowId = flowId,
                    VersionNumber = 1,
                    Status = "draft",
                    DefinitionJson = definitionJson,
                    ChangeNote = "Imported from Meta Flow"
                };
                db.AutomationFlowVersions.Add(updatedVersion);
                flow.CurrentVersionId = updatedVersion.Id;
            }
            else
            {
                updatedVersion.DefinitionJson = definitionJson;
                updatedVersion.ChangeNote = string.IsNullOrWhiteSpace(req.ChangeNote)
                    ? $"Updated from Meta Flow {req.MetaFlowId}"
                    : req.ChangeNote.Trim();
                updatedVersion.Status = "draft";
            }
        }

        if (!string.IsNullOrWhiteSpace(importedMetaName))
            flow.Name = flow.Name.Trim().Length == 0 ? importedMetaName : flow.Name;
        flow.UpdatedAtUtc = DateTime.UtcNow;
        flow.LifecycleStatus = "draft";
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            ok = true,
            flowId = flowId,
            versionId = updatedVersion?.Id,
            metaFlowId = req.MetaFlowId,
            metaFlowName = importedMetaName,
            warning = importWarning
        });
    }

    [HttpPost("flows/{flowId:guid}/versions")]
    public async Task<IActionResult> CreateVersion(Guid flowId, [FromBody] CreateFlowVersionRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        var latest = await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var definition = req.DefinitionJson;
        if (string.IsNullOrWhiteSpace(definition))
        {
            definition = latest?.DefinitionJson;
            if (string.IsNullOrWhiteSpace(definition))
            {
                var nodes = await db.AutomationNodes
                    .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId)
                    .OrderBy(x => x.Sequence)
                    .Select(x => new { id = string.IsNullOrWhiteSpace(x.NodeKey) ? x.Id.ToString() : x.NodeKey, type = x.NodeType, name = x.Name, config = x.ConfigJson, x.Sequence, edges = x.EdgesJson })
                    .ToListAsync(ct);
                definition = JsonSerializer.Serialize(new { nodes, edges = Array.Empty<object>() }, JsonOptions);
            }
        }

        var version = new AutomationFlowVersion
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            FlowId = flowId,
            VersionNumber = (latest?.VersionNumber ?? 0) + 1,
            Status = "draft",
            DefinitionJson = NormalizeJson(definition!, BuildDefaultDefinition(flow.TriggerType)),
            ChangeNote = req.ChangeNote?.Trim() ?? string.Empty,
            IsStagedRelease = req.IsStagedRelease
        };

        flow.CurrentVersionId = version.Id;
        flow.LifecycleStatus = "draft";
        flow.UpdatedAtUtc = DateTime.UtcNow;
        db.AutomationFlowVersions.Add(version);
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    [HttpPost("flows/{flowId:guid}/versions/{versionId:guid}/publish")]
    public async Task<IActionResult> PublishVersion(Guid flowId, Guid versionId, [FromBody] PublishFlowVersionRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();
        var version = await db.AutomationFlowVersions.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Id == versionId, ct);
        if (version is null) return NotFound();

        var validation = ValidateDefinitionInternal(version.DefinitionJson);
        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                error = "flow_definition_invalid",
                message = "Flow definition failed schema validation.",
                details = validation
            });
        }

        var unsupported = GetUnsupportedNodeTypes(version.DefinitionJson);
        if (unsupported.Count > 0)
        {
            return BadRequest(new
            {
                error = "unsupported_node_types",
                message = "Flow contains node types that are not executable in runtime.",
                unsupportedNodeTypes = unsupported
            });
        }

        if (req.RequireApproval && !rbac.HasAnyRole("owner", "admin", "super_admin"))
        {
            return BadRequest("Approval required from owner/admin/super_admin.");
        }

        var activeBots = await db.AutomationFlows.CountAsync(x =>
            x.TenantId == tenancy.TenantId && x.PublishedVersionId != null && x.Id != flowId, ct);
        var chatbotLimit = await billingGuard.CheckLimitAsync(tenancy.TenantId, "chatbots", activeBots + 1, ct);
        if (!chatbotLimit.Allowed) return BadRequest(chatbotLimit.Message);

        var oldPublished = await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Status == "published")
            .ToListAsync(ct);
        foreach (var item in oldPublished) item.Status = "archived";

        version.Status = "published";
        version.PublishedAtUtc = DateTime.UtcNow;
        flow.CurrentVersionId = version.Id;
        flow.PublishedVersionId = version.Id;
        flow.LifecycleStatus = "published";
        flow.LastPublishedAtUtc = DateTime.UtcNow;
        flow.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await SyncAutomationUsageAsync(ct);
        return Ok(new { flowId, versionId, status = "published" });
    }

    [HttpPost("flows/{flowId:guid}/send-flow")]
    public async Task<IActionResult> SendFlowToUser(Guid flowId, [FromBody] SendFlowToUserRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();

        var flow = await db.AutomationFlows
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        var version = await ResolveVersion(flow, req.VersionId, ct);
        if (version is null) return BadRequest("Flow version not found.");

        var validation = ValidateDefinitionInternal(version.DefinitionJson);
        if (!validation.IsValid)
            return BadRequest(new { error = "flow_definition_invalid", details = validation });

        var flowRef = string.IsNullOrWhiteSpace(req.FlowId) ? flowId.ToString("N") : req.FlowId.Trim();
        var idempotency = $"flow-send:{flowId}:{Guid.NewGuid():N}";
        try
        {
            var message = await messaging.EnqueueAsync(new SendMessageRequest
            {
                IdempotencyKey = idempotency,
                Channel = ChannelType.WhatsApp,
                Recipient = req.Recipient,
                Body = string.IsNullOrWhiteSpace(req.Body) ? "Please continue to the next step." : req.Body.Trim(),
                IsInteractive = true,
                InteractiveType = "flow",
                InteractiveFlowId = flowRef,
                InteractiveFlowCta = string.IsNullOrWhiteSpace(req.FlowCta) ? "Open" : req.FlowCta.Trim(),
                InteractiveFlowToken = req.FlowToken?.Trim() ?? string.Empty,
                InteractiveFlowAction = string.IsNullOrWhiteSpace(req.FlowAction) ? "navigate" : req.FlowAction.Trim().ToLowerInvariant(),
                InteractiveFlowScreen = req.FlowScreen?.Trim() ?? string.Empty,
                InteractiveFlowDataJson = string.IsNullOrWhiteSpace(req.FlowDataJson) ? "{}" : req.FlowDataJson,
                InteractiveFlowMessageVersion = req.FlowMessageVersion
            }, ct);

            return Ok(new
            {
                ok = true,
                messageId = message.Id,
                messageStatus = message.Status,
                flowId = flowId,
                flowReference = flowRef
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("flows/{flowId:guid}/data-exchange")]
    public async Task<IActionResult> FlowDataExchange(Guid flowId, [FromBody] FlowDataExchangeRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();

        var flow = await db.AutomationFlows
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        var version = await ResolveVersion(flow, req.VersionId, ct);
        if (version is null) return BadRequest("Flow version not found.");

        var validation = ValidateDefinitionInternal(version.DefinitionJson);
        if (!validation.IsValid)
            return BadRequest(new { error = "flow_definition_invalid", details = validation });

        if (string.IsNullOrWhiteSpace(req.Action))
            return BadRequest("Action is required.");

        if (!TryResolveDynamicDataSource(version.DefinitionJson, req.ScreenId, req.Action, out var endpoint, out var method, out var headers))
            return BadRequest("Dynamic data source not found for requested action.");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Dynamic endpoint must be absolute HTTPS.");
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return BadRequest("Dynamic endpoint host is not allowed.");
        if (IPAddress.TryParse(uri.Host, out var ip) && IsPrivateOrLoopback(ip))
            return BadRequest("Dynamic endpoint private/loopback IP is not allowed.");
        await EnforceDynamicHostAllowListAsync(uri.Host, ct);

        var payloadJson = string.IsNullOrWhiteSpace(req.PayloadJson) ? "{}" : req.PayloadJson;
        try { using var _ = JsonDocument.Parse(payloadJson); } catch { return BadRequest("PayloadJson must be valid JSON."); }
        var bodyJson = JsonSerializer.Serialize(new
        {
            tenantId = tenancy.TenantId,
            flowId,
            versionId = version.Id,
            screenId = req.ScreenId,
            action = req.Action,
            payload = JsonSerializer.Deserialize<object>(payloadJson)
        }, JsonOptions);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var reqMsg = new HttpRequestMessage(new HttpMethod(string.IsNullOrWhiteSpace(method) ? "POST" : method.ToUpperInvariant()), uri);
        reqMsg.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        reqMsg.Headers.TryAddWithoutValidation("X-Textzy-FlowId", flowId.ToString());
        reqMsg.Headers.TryAddWithoutValidation("X-Textzy-TenantId", tenancy.TenantId.ToString());
        foreach (var h in headers)
        {
            if (!reqMsg.Headers.TryAddWithoutValidation(h.Key, h.Value))
                reqMsg.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var res = await client.SendAsync(reqMsg, ct);
        var responseText = await res.Content.ReadAsStringAsync(ct);
        sw.Stop();
        db.FlowRuntimeEvents.Add(new FlowRuntimeEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            FlowId = flowId,
            EventType = "endpoint.exchange",
            EventSource = "automation_api",
            Success = res.IsSuccessStatusCode,
            StatusCode = (int)res.StatusCode,
            DurationMs = (int)sw.ElapsedMilliseconds,
            ScreenId = req.ScreenId ?? string.Empty,
            ActionName = req.Action ?? string.Empty,
            PayloadJson = payloadJson,
            ErrorDetail = res.IsSuccessStatusCode ? string.Empty : responseText,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            ok = res.IsSuccessStatusCode,
            statusCode = (int)res.StatusCode,
            endpoint = uri.ToString(),
            action = req.Action,
            response = TryParseJsonOrText(responseText)
        });
    }

    [HttpGet("metrics/flows")]
    public async Task<IActionResult> FlowMetrics([FromQuery] int days = 7, [FromQuery] string metaFlowId = "", CancellationToken ct = default)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        var safeDays = Math.Clamp(days, 1, 90);
        var fromUtc = DateTime.UtcNow.AddDays(-safeDays);

        var q = db.FlowRuntimeEvents.AsNoTracking()
            .Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= fromUtc);
        if (!string.IsNullOrWhiteSpace(metaFlowId))
            q = q.Where(x => x.MetaFlowId == metaFlowId.Trim());

        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).Take(2000).ToListAsync(ct);
        var total = rows.Count;
        var opened = rows.Count(x => x.EventType == "flow.open");
        var submitted = rows.Count(x => x.EventType == "flow.submission");
        var completed = rows.Count(x => x.EventType == "flow.completion");
        var errors = rows.Count(x => x.EventType == "flow.error" || !x.Success);
        var exchange = rows.Where(x => x.EventType == "endpoint.exchange").ToList();
        var avgLatencyMs = exchange.Count == 0 ? 0 : (int)Math.Round(exchange.Average(x => x.DurationMs));
        var p95LatencyMs = exchange.Count == 0 ? 0 : exchange.OrderBy(x => x.DurationMs).ElementAt((int)Math.Floor(exchange.Count * 0.95)).DurationMs;

        var byFlow = rows
            .GroupBy(x => x.MetaFlowId ?? string.Empty)
            .Select(g => new
            {
                metaFlowId = g.Key,
                total = g.Count(),
                opened = g.Count(x => x.EventType == "flow.open"),
                submitted = g.Count(x => x.EventType == "flow.submission"),
                completed = g.Count(x => x.EventType == "flow.completion"),
                errors = g.Count(x => x.EventType == "flow.error" || !x.Success)
            })
            .OrderByDescending(x => x.total)
            .Take(50)
            .ToList();

        return Ok(new
        {
            windowDays = safeDays,
            total,
            opened,
            submitted,
            completed,
            errors,
            completionRate = opened == 0 ? 0d : Math.Round((double)completed * 100d / opened, 2),
            submitRate = opened == 0 ? 0d : Math.Round((double)submitted * 100d / opened, 2),
            avgEndpointLatencyMs = avgLatencyMs,
            p95EndpointLatencyMs = p95LatencyMs,
            byFlow
        });
    }

    [HttpGet("metrics/flows/events")]
    public async Task<IActionResult> FlowMetricEvents(
        [FromQuery] int take = 100,
        [FromQuery] int days = 7,
        [FromQuery] string metaFlowId = "",
        [FromQuery] string eventType = "",
        CancellationToken ct = default)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        var safeTake = Math.Clamp(take, 1, 500);
        var safeDays = Math.Clamp(days, 1, 90);
        var fromUtc = DateTime.UtcNow.AddDays(-safeDays);
        var q = db.FlowRuntimeEvents.AsNoTracking().Where(x => x.TenantId == tenancy.TenantId && x.CreatedAtUtc >= fromUtc);
        if (!string.IsNullOrWhiteSpace(metaFlowId)) q = q.Where(x => x.MetaFlowId == metaFlowId.Trim());
        if (!string.IsNullOrWhiteSpace(eventType)) q = q.Where(x => x.EventType == eventType.Trim());

        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).Take(safeTake).Select(x => new
        {
            x.Id,
            x.FlowId,
            x.MetaFlowId,
            x.EventType,
            x.EventSource,
            x.Success,
            x.StatusCode,
            x.DurationMs,
            x.ScreenId,
            x.ActionName,
            x.CustomerPhone,
            x.ConversationExternalId,
            x.ErrorDetail,
            x.CreatedAtUtc
        }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("flows/{flowId:guid}/versions/{versionId:guid}/rollback")]
    public async Task<IActionResult> Rollback(Guid flowId, Guid versionId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        if (!rbac.HasAnyRole("owner", "admin", "super_admin")) return Forbid();

        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();
        var version = await db.AutomationFlowVersions.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Id == versionId, ct);
        if (version is null) return NotFound();

        var oldPublished = await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Status == "published")
            .ToListAsync(ct);
        foreach (var item in oldPublished) item.Status = "archived";

        version.Status = "published";
        version.PublishedAtUtc = DateTime.UtcNow;
        flow.CurrentVersionId = versionId;
        flow.PublishedVersionId = versionId;
        flow.LifecycleStatus = "published";
        flow.LastPublishedAtUtc = DateTime.UtcNow;
        flow.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await SyncAutomationUsageAsync(ct);
        return Ok(new { status = "rolled_back", versionId });
    }

    [HttpPost("flows/{flowId:guid}/approvals/request")]
    public async Task<IActionResult> RequestApproval(Guid flowId, [FromBody] RequestFlowApprovalRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var version = await db.AutomationFlowVersions
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Id == req.VersionId, ct);
        if (version is null) return NotFound();

        var pendingExists = await db.AutomationApprovals.AnyAsync(x =>
            x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.VersionId == req.VersionId && x.Status == "pending", ct);
        if (pendingExists) return Ok(new { status = "already_pending" });

        var approval = new AutomationApproval
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            FlowId = flowId,
            VersionId = req.VersionId,
            RequestedBy = auth.Email,
            RequestedByRole = auth.Role
        };
        db.AutomationApprovals.Add(approval);
        await db.SaveChangesAsync(ct);
        return Ok(approval);
    }

    [HttpPost("flows/{flowId:guid}/approvals/{approvalId:guid}/decide")]
    public async Task<IActionResult> DecideApproval(Guid flowId, Guid approvalId, [FromBody] DecideFlowApprovalRequest req, CancellationToken ct)
    {
        if (!rbac.HasAnyRole("owner", "admin", "super_admin")) return Forbid();
        EnsureAutomationSchema();
        var approval = await db.AutomationApprovals
            .FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.Id == approvalId, ct);
        if (approval is null) return NotFound();
        if (approval.Status != "pending") return BadRequest("Approval already decided.");

        var decision = (req.Decision ?? string.Empty).Trim().ToLowerInvariant();
        if (decision is not ("approved" or "rejected")) return BadRequest("Decision must be approved or rejected.");

        approval.Status = decision;
        approval.DecisionComment = req.Comment?.Trim() ?? string.Empty;
        approval.DecidedBy = auth.Email;
        approval.DecidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(approval);
    }

    [HttpPost("flows/{flowId:guid}/nodes")]
    public async Task<IActionResult> AddNode(Guid flowId, [FromBody] UpsertAutomationNodeRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct);
        if (flow is null) return NotFound();

        var node = new AutomationNode
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            FlowId = flowId,
            VersionId = req.VersionId,
            NodeKey = string.IsNullOrWhiteSpace(req.NodeKey) ? Guid.NewGuid().ToString("N") : req.NodeKey.Trim(),
            NodeType = req.NodeType.Trim().ToLowerInvariant(),
            Name = req.Name?.Trim() ?? string.Empty,
            ConfigJson = NormalizeJson(req.ConfigJson, "{}"),
            EdgesJson = NormalizeJson(req.EdgesJson, "[]"),
            Sequence = req.Sequence,
            IsReusable = req.IsReusable
        };
        db.AutomationNodes.Add(node);
        flow.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(node);
    }

    [HttpPost("flows/{flowId:guid}/simulate")]
    public async Task<IActionResult> Simulate(Guid flowId, [FromBody] SimulateAutomationRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var run = await Execute(flowId, req.VersionId, req.TriggerType, req.TriggerPayloadJson, "simulate", $"sim-{Guid.NewGuid():N}", false, ct);
        return Ok(run);
    }

    [HttpPost("flows/{flowId:guid}/run")]
    public async Task<IActionResult> Run(Guid flowId, [FromBody] RunAutomationRequest req, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationWrite)) return Forbid();
        EnsureAutomationSchema();
        var idempotency = (req.IdempotencyKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(idempotency)) idempotency = $"run-{Guid.NewGuid():N}";

        var limitCheck = CheckRunLimits();
        if (limitCheck is not null) return StatusCode(429, limitCheck);

        var run = await Execute(flowId, req.VersionId, req.TriggerType, req.TriggerPayloadJson, "live", idempotency, req.IsRetry, ct);
        return Ok(run);
    }

    [HttpGet("runs")]
    public async Task<IActionResult> ListRuns([FromQuery] Guid? flowId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        limit = Math.Clamp(limit, 1, 200);

        var q = db.AutomationRuns.Where(x => x.TenantId == tenancy.TenantId);
        if (flowId.HasValue) q = q.Where(x => x.FlowId == flowId.Value);
        var items = await q.OrderByDescending(x => x.StartedAtUtc).Take(limit).ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runId, CancellationToken ct)
    {
        if (!rbac.HasPermission(AutomationRead)) return Forbid();
        EnsureAutomationSchema();
        var item = await db.AutomationRuns.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == runId, ct);
        return item is null ? NotFound() : Ok(item);
    }

    private async Task<AutomationRun> Execute(
        Guid flowId,
        Guid? versionId,
        string triggerType,
        string triggerPayloadJson,
        string mode,
        string idempotencyKey,
        bool isRetry,
        CancellationToken ct)
    {
        var flow = await db.AutomationFlows.FirstOrDefaultAsync(x => x.TenantId == tenancy.TenantId && x.Id == flowId, ct)
            ?? throw new InvalidOperationException("Flow not found.");

        var existing = await db.AutomationRuns
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flowId && x.IdempotencyKey == idempotencyKey)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (existing is not null && !isRetry) return existing;

        var version = await ResolveVersion(flow, versionId, ct)
            ?? throw new InvalidOperationException("Flow version not found.");
        var definitionJson = string.IsNullOrWhiteSpace(version.DefinitionJson)
            ? BuildDefaultDefinition(flow.TriggerType)
            : version.DefinitionJson;

        var run = new AutomationRun
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            FlowId = flow.Id,
            VersionId = version.Id,
            Mode = mode,
            TriggerType = string.IsNullOrWhiteSpace(triggerType) ? flow.TriggerType : triggerType.Trim().ToLowerInvariant(),
            IdempotencyKey = idempotencyKey,
            TriggerPayloadJson = NormalizeJson(triggerPayloadJson, "{}"),
            Status = "running",
            RetryCount = isRetry ? 1 : 0
        };
        db.AutomationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var payload = ParseObject(run.TriggerPayloadJson);
        if (!payload.ContainsKey("faq_answer") || string.IsNullOrWhiteSpace(payload["faq_answer"]?.ToString()))
        {
            var inboundText = payload.TryGetValue("message", out var msgObj) ? msgObj?.ToString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(inboundText))
            {
                payload["faq_answer"] = await FindFaqAnswer(inboundText, ct);
            }
        }

        var inboundRecipient = TryGetPayloadValue(payload, "recipient", "to", "phone", "mobile", "customerPhone");
        var inboundMessageText = TryGetPayloadValue(payload, "message", "text", "body");
        var inboundMatchKey = TryGetPayloadValue(payload, "message_key", "matchKey");
        var inboundMessageId = TryGetPayloadValue(payload, "inbound_message_id", "messageId");
        var phoneNumberId = TryGetPayloadValue(payload, "phoneNumberId", "phone_number_id");

        if (string.IsNullOrWhiteSpace(inboundMessageId))
            inboundMessageId = $"{mode}-{Guid.NewGuid():N}";

        try
        {
            await workflowExecutionEngine.ExecuteAsync(new WorkflowExecutionEngine.ExecuteRequest
            {
                TenantId = tenancy.TenantId,
                FlowId = flow.Id,
                PhoneNumberId = phoneNumberId,
                InboundMessageId = inboundMessageId,
                InboundRecipient = inboundRecipient,
                InboundMessageText = inboundMessageText,
                InboundMatchKey = inboundMatchKey,
                DefinitionJson = definitionJson,
                Run = run,
                Payload = payload,
                IsInteractiveResume = false,
                ResumeSourceNodeId = string.Empty,
                StartNodeId = string.Empty
            }, ct);
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.FailureReason = ex.Message;
            run.Log = string.IsNullOrWhiteSpace(run.Log)
                ? $"[{DateTime.UtcNow:O}] ERROR: {ex.Message}"
                : $"{run.Log}\n[{DateTime.UtcNow:O}] ERROR: {ex.Message}";
            run.CompletedAtUtc = DateTime.UtcNow;
        }

        UpsertUsageCounter(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    private static string TryGetPayloadValue(Dictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw?.ToString()))
                return raw!.ToString()!;
        }
        return string.Empty;
    }

    private async Task<string> ExecuteNode(AutomationFlow flow, FlowNode node, Dictionary<string, object?> payload, string mode, CancellationToken ct)
    {
        var config = node.config ?? new Dictionary<string, object?>();
        var nodeType = (node.type ?? string.Empty).Trim().ToLowerInvariant();

        string resolveReplyText()
        {
            if (nodeType == "bot_reply")
            {
                var replyMode = ResolveValue(config, payload, "replyMode");
                if (string.Equals(replyMode, "media", StringComparison.OrdinalIgnoreCase))
                {
                    var mediaText = ResolveValue(config, payload, "mediaText", "body", "message");
                    if (!string.IsNullOrWhiteSpace(mediaText)) return mediaText;
                }
                var simpleText = ResolveValue(config, payload, "simpleText", "body", "message", "question", "prompt");
                if (!string.IsNullOrWhiteSpace(simpleText)) return simpleText;
            }
            if (nodeType is "buttons" or "list" or "cta_url" or "media")
            {
                var uiBody = ResolveValue(config, payload, "body", "message", "question", "prompt");
                if (!string.IsNullOrWhiteSpace(uiBody)) return uiBody;
            }
            return ResolveValue(config, payload, "body", "message", "question", "prompt");
        }

        if (nodeType is "text" or "send_text" or "bot_reply" or "buttons" or "list" or "cta_url" or "media")
        {
            if (mode == "live")
            {
                var recipient = ResolveValue(config, payload, "recipient", "recipient");
                var body = resolveReplyText();
                if (!string.IsNullOrWhiteSpace(recipient) && !string.IsNullOrWhiteSpace(body))
                {
                    await messaging.EnqueueAsync(new SendMessageRequest
                    {
                        Recipient = recipient,
                        Body = Interpolate(body, payload),
                        Channel = flow.Channel == "sms" ? ChannelType.Sms : ChannelType.WhatsApp
                    }, ct);
                }
            }
            return node.next ?? node.onSuccess;
        }

        if (nodeType == "template")
        {
            if (mode == "live")
            {
                var recipient = ResolveValue(config, payload, "recipient", "recipient");
                var templateName = ResolveValue(config, payload, "templateName", "template_name");
                var language = ResolveValue(config, payload, "languageCode", "language");
                if (!string.IsNullOrWhiteSpace(recipient) && !string.IsNullOrWhiteSpace(templateName))
                {
                    var paramValues = new List<string>();
                    if (config.TryGetValue("parameters", out var p) && p is JsonElement pElem && pElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in pElem.EnumerateArray()) paramValues.Add(Interpolate(item.ToString(), payload));
                    }
                    await messaging.EnqueueAsync(new SendMessageRequest
                    {
                        Recipient = recipient,
                        Body = ResolveValue(config, payload, "body", "message"),
                        Channel = ChannelType.WhatsApp,
                        UseTemplate = true,
                        TemplateName = templateName,
                        TemplateLanguageCode = string.IsNullOrWhiteSpace(language) ? "en" : language,
                        TemplateParameters = paramValues
                    }, ct);
                }
            }
            return node.next ?? node.onSuccess;
        }

        if (nodeType is "delay" or "wait")
        {
            var ms = 0;
            if (config.TryGetValue("milliseconds", out var v) && int.TryParse(v?.ToString(), out var parsedMs)) ms = parsedMs;
            else if (config.TryGetValue("seconds", out var s) && int.TryParse(s?.ToString(), out var sec)) ms = sec * 1000;
            if (mode == "live" && ms > 0) await Task.Delay(Math.Min(ms, 120_000), ct);
            return node.next ?? node.onSuccess;
        }

        if (nodeType is "condition" or "split")
        {
            var ok = EvaluateCondition(config, payload);
            return ok ? (node.onTrue ?? node.onSuccess ?? node.next) : (node.onFalse ?? node.onFailure ?? node.next);
        }

        if (nodeType == "subflow")
        {
            var raw = ResolveValue(config, payload, "flowId", "subflowId");
            if (Guid.TryParse(raw, out var subFlowId))
            {
                await Execute(subFlowId, null, "subflow", JsonSerializer.Serialize(payload, JsonOptions), mode, $"sub-{Guid.NewGuid():N}", false, ct);
            }
            return node.next ?? node.onSuccess;
        }

        if (nodeType is "handoff" or "api_call" or "db_query" or "function" or "webhook" or "media" or "buttons" or "list")
        {
            return node.next ?? node.onSuccess;
        }

        return node.next ?? node.onSuccess;
    }

    private AutomationFlowVersion? ResolvePublished(AutomationFlow flow)
    {
        if (!flow.PublishedVersionId.HasValue) return null;
        return db.AutomationFlowVersions.FirstOrDefault(x =>
            x.TenantId == tenancy.TenantId && x.FlowId == flow.Id && x.Id == flow.PublishedVersionId.Value);
    }

    private async Task<AutomationFlowVersion?> ResolveVersion(AutomationFlow flow, Guid? versionId, CancellationToken ct)
    {
        if (versionId.HasValue)
        {
            return await db.AutomationFlowVersions.FirstOrDefaultAsync(x =>
                x.TenantId == tenancy.TenantId && x.FlowId == flow.Id && x.Id == versionId.Value, ct);
        }

        if (flow.PublishedVersionId.HasValue)
        {
            var published = await db.AutomationFlowVersions.FirstOrDefaultAsync(x =>
                x.TenantId == tenancy.TenantId && x.FlowId == flow.Id && x.Id == flow.PublishedVersionId.Value, ct);
            if (published is not null) return published;
        }

        if (flow.CurrentVersionId.HasValue)
        {
            var current = await db.AutomationFlowVersions.FirstOrDefaultAsync(x =>
                x.TenantId == tenancy.TenantId && x.FlowId == flow.Id && x.Id == flow.CurrentVersionId.Value, ct);
            if (current is not null) return current;
        }

        return await db.AutomationFlowVersions
            .Where(x => x.TenantId == tenancy.TenantId && x.FlowId == flow.Id)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
    }

    private object? CheckRunLimits()
    {
        var limits = GetLimits();
        var usage = GetOrCreateUsageCounter();
        if (usage.RunCount >= limits.runsPerDay)
        {
            return new
            {
                error = "daily_run_limit_reached",
                usage = usage.RunCount,
                limit = limits.runsPerDay
            };
        }
        if (usage.ApiCallCount >= limits.apiCallsPerDay)
        {
            return new
            {
                error = "daily_api_call_limit_reached",
                usage = usage.ApiCallCount,
                limit = limits.apiCallsPerDay
            };
        }
        return null;
    }

    private (int activeFlows, int runsPerDay, int apiCallsPerDay, int maxNodesPerFlow) GetLimits()
    {
        return (activeFlows: 50, runsPerDay: 25000, apiCallsPerDay: 50000, maxNodesPerFlow: 300);
    }

    private AutomationUsageCounter GetOrCreateUsageCounter()
    {
        var today = DateTime.UtcNow.Date;
        var usage = db.AutomationUsageCounters.FirstOrDefault(x => x.TenantId == tenancy.TenantId && x.BucketDateUtc == today);
        if (usage is not null) return usage;
        usage = new AutomationUsageCounter
        {
            Id = Guid.NewGuid(),
            TenantId = tenancy.TenantId,
            BucketDateUtc = today
        };
        db.AutomationUsageCounters.Add(usage);
        return usage;
    }

    private void UpsertUsageCounter(AutomationRun run)
    {
        var usage = GetOrCreateUsageCounter();
        usage.RunCount += 1;
        if (run.Log.Contains("api_call", StringComparison.OrdinalIgnoreCase)) usage.ApiCallCount += 1;
        usage.ActiveFlowCount = db.AutomationFlows.Count(x => x.TenantId == tenancy.TenantId && x.IsActive);
        usage.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task SyncAutomationUsageAsync(CancellationToken ct)
    {
        var flows = await db.AutomationFlows.CountAsync(x => x.TenantId == tenancy.TenantId, ct);
        var activeBots = await db.AutomationFlows.CountAsync(x =>
            x.TenantId == tenancy.TenantId && x.PublishedVersionId != null, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "flows", flows, ct);
        await billingGuard.SetAbsoluteUsageAsync(tenancy.TenantId, "chatbots", activeBots, ct);
    }

    private static string NormalizeTrigger(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "keyword" or "intent" or "webhook" or "schedule" or "tag" or "user_event" => v,
            _ => "keyword"
        };
    }

    private static string NormalizeChannel(string value)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return v switch
        {
            "waba" or "sms" => v,
            _ => "waba"
        };
    }

    private static string BuildDefaultDefinition(string triggerType)
    {
        var model = new
        {
            trigger = new { type = NormalizeTrigger(triggerType) },
            startNodeId = "start_1",
            nodes = new object[]
            {
                new { id = "start_1", type = "start", name = "Start", config = new { }, next = "text_1" },
                new { id = "text_1", type = "text", name = "Welcome Message", config = new { body = "Hello {{name}}", recipient = "{{recipient}}" }, next = "end_1" },
                new { id = "end_1", type = "end", name = "End", config = new { }, next = "" }
            },
            edges = Array.Empty<object>()
        };
        return JsonSerializer.Serialize(model, JsonOptions);
    }

    private static string NormalizeJson(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return value;
        }
        catch
        {
            return fallback;
        }
    }

    private FlowValidationReport ValidateDefinitionInternal(string definitionJson)
    {
        var report = new FlowValidationReport();
        var raw = string.IsNullOrWhiteSpace(definitionJson) ? "{}" : definitionJson;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            report.Version = root.TryGetProperty("version", out var versionNode)
                ? (versionNode.GetString() ?? string.Empty)
                : string.Empty;

            if (root.TryGetProperty("screens", out var screensNode) && screensNode.ValueKind == JsonValueKind.Array)
            {
                ValidateScreenFlow(root, screensNode, report);
            }
            else
            {
                ValidateNodeFlow(root, report);
            }
        }
        catch (JsonException ex)
        {
            report.Errors.Add($"Invalid JSON: {ex.Message}");
        }

        report.IsValid = report.Errors.Count == 0;
        return report;
    }

    private static void ValidateNodeFlow(JsonElement root, FlowValidationReport report)
    {
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            report.Errors.Add("Definition must include a nodes array or screens array.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.EnumerateArray())
        {
            report.NodeCount += 1;
            var id = node.TryGetProperty("id", out var idNode) ? (idNode.GetString() ?? string.Empty).Trim() : string.Empty;
            var type = node.TryGetProperty("type", out var typeNode) ? (typeNode.GetString() ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(id)) report.Errors.Add("Each node must have id.");
            else if (!ids.Add(id)) report.Errors.Add($"Duplicate node id '{id}'.");
            if (string.IsNullOrWhiteSpace(type)) report.Errors.Add($"Node '{id}' type is required.");
        }
        if (report.NodeCount == 0) report.Errors.Add("Flow has no nodes.");
        var hasStart = nodes.EnumerateArray().Any(x => x.TryGetProperty("type", out var t) && string.Equals(t.GetString(), "start", StringComparison.OrdinalIgnoreCase));
        var hasEnd = nodes.EnumerateArray().Any(x => x.TryGetProperty("type", out var t) && string.Equals(t.GetString(), "end", StringComparison.OrdinalIgnoreCase));
        if (!hasStart) report.Warnings.Add("No start node found.");
        if (!hasEnd) report.Warnings.Add("No end node found.");
    }

    private static void ValidateScreenFlow(JsonElement root, JsonElement screensNode, FlowValidationReport report)
    {
        if (string.IsNullOrWhiteSpace(report.Version))
            report.Errors.Add("Screen flow must include version.");

        var screenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var screen in screensNode.EnumerateArray())
        {
            report.ScreenCount += 1;
            var id = screen.TryGetProperty("id", out var idNode) ? (idNode.GetString() ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                report.Errors.Add("Each screen must have id.");
                continue;
            }
            if (!screenIds.Add(id)) report.Errors.Add($"Duplicate screen id '{id}'.");

            if (!screen.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array)
            {
                report.Errors.Add($"Screen '{id}' must define components array.");
                continue;
            }

            var compIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var comp in components.EnumerateArray())
            {
                report.ComponentCount += 1;
                var cid = comp.TryGetProperty("id", out var cIdNode) ? (cIdNode.GetString() ?? string.Empty).Trim() : string.Empty;
                var cType = comp.TryGetProperty("type", out var cTypeNode) ? (cTypeNode.GetString() ?? string.Empty).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(cid)) report.Errors.Add($"Screen '{id}' component missing id.");
                else if (!compIds.Add(cid)) report.Errors.Add($"Screen '{id}' duplicate component id '{cid}'.");
                if (string.IsNullOrWhiteSpace(cType)) report.Errors.Add($"Screen '{id}' component '{cid}' missing type.");
                if (comp.TryGetProperty("nextScreen", out var nextScreen) && !string.IsNullOrWhiteSpace(nextScreen.GetString()))
                    report.RouteCount += 1;
            }
        }

        if (!root.TryGetProperty("routing", out var routing) || routing.ValueKind != JsonValueKind.Object)
        {
            report.Errors.Add("Screen flow must include routing object.");
            return;
        }
        var start = routing.TryGetProperty("startScreen", out var startScreen) ? (startScreen.GetString() ?? string.Empty).Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(start)) report.Errors.Add("routing.startScreen is required.");
        else if (!screenIds.Contains(start)) report.Errors.Add($"routing.startScreen '{start}' does not exist.");
        if (routing.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in edges.EnumerateArray())
            {
                report.RouteCount += 1;
                var from = edge.TryGetProperty("from", out var fromNode) ? (fromNode.GetString() ?? string.Empty).Trim() : string.Empty;
                var to = edge.TryGetProperty("to", out var toNode) ? (toNode.GetString() ?? string.Empty).Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                {
                    report.Errors.Add("Each routing edge must have from and to.");
                    continue;
                }
                if (!screenIds.Contains(from)) report.Errors.Add($"Routing edge from '{from}' does not exist.");
                if (!screenIds.Contains(to)) report.Errors.Add($"Routing edge to '{to}' does not exist.");
            }
        }
    }

    private static object TryParseJsonOrText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.Clone();
        }
        catch
        {
            return value;
        }
    }

    private static bool TryResolveDynamicDataSource(
        string definitionJson,
        string? screenId,
        string action,
        out string endpoint,
        out string method,
        out Dictionary<string, string> headers)
    {
        endpoint = string.Empty;
        method = "POST";
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(definitionJson) || string.IsNullOrWhiteSpace(action)) return false;
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
            var root = doc.RootElement;
            var normalizedAction = action.Trim();

            if (root.TryGetProperty("dynamicDataSources", out var sources) &&
                sources.ValueKind == JsonValueKind.Object &&
                sources.TryGetProperty(normalizedAction, out var sourceObj) &&
                sourceObj.ValueKind == JsonValueKind.Object)
            {
                endpoint = sourceObj.TryGetProperty("url", out var u) ? (u.GetString() ?? string.Empty).Trim() : string.Empty;
                method = sourceObj.TryGetProperty("method", out var m) ? (m.GetString() ?? "POST").Trim() : "POST";
                if (sourceObj.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
                {
                    foreach (var h in hs.EnumerateObject())
                    {
                        var v = h.Value.ToString();
                        if (!string.IsNullOrWhiteSpace(h.Name) && !string.IsNullOrWhiteSpace(v))
                            headers[h.Name] = v;
                    }
                }
                if (!string.IsNullOrWhiteSpace(endpoint)) return true;
            }

            if (!string.IsNullOrWhiteSpace(screenId) &&
                root.TryGetProperty("screens", out var screens) &&
                screens.ValueKind == JsonValueKind.Array)
            {
                foreach (var screen in screens.EnumerateArray())
                {
                    var sid = screen.TryGetProperty("id", out var sidNode) ? (sidNode.GetString() ?? string.Empty).Trim() : string.Empty;
                    if (!string.Equals(sid, screenId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!screen.TryGetProperty("dataSources", out var ds) || ds.ValueKind != JsonValueKind.Object) continue;
                    if (!ds.TryGetProperty(normalizedAction, out var dsObj) || dsObj.ValueKind != JsonValueKind.Object) continue;
                    endpoint = dsObj.TryGetProperty("url", out var u) ? (u.GetString() ?? string.Empty).Trim() : string.Empty;
                    method = dsObj.TryGetProperty("method", out var m) ? (m.GetString() ?? "POST").Trim() : "POST";
                    if (dsObj.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var h in hs.EnumerateObject())
                        {
                            var v = h.Value.ToString();
                            if (!string.IsNullOrWhiteSpace(h.Name) && !string.IsNullOrWhiteSpace(v))
                                headers[h.Name] = v;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(endpoint)) return true;
                }
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private async Task EnforceDynamicHostAllowListAsync(string host, CancellationToken ct)
    {
        var entry = await controlDb.PlatformSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Scope == "mobile-app" && x.Key == "webhookAllowedHosts", ct);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ValueEncrypted)) return;

        var raw = crypto.Decrypt(entry.ValueEncrypted);
        if (string.IsNullOrWhiteSpace(raw)) return;

        var allowed = ParseStringArray(raw);
        if (allowed.Count == 0) return;

        var normalizedHost = host.Trim().ToLowerInvariant();
        var matched = allowed.Any(a =>
        {
            var x = (a ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(x)) return false;
            return normalizedHost == x || normalizedHost.EndsWith("." + x, StringComparison.OrdinalIgnoreCase);
        });

        if (!matched)
            throw new InvalidOperationException($"Dynamic data host '{host}' is not in allow-list.");
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254);
        }
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast;
        }
        return false;
    }

    private static List<string> ParseStringArray(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Select(x => x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
            }
        }
        catch
        {
            // fall through
        }
        return raw
            .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static JsonElement? TryExtractMetaFlowSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("flow_json", out var flowJson) && flowJson.ValueKind == JsonValueKind.Object)
            return flowJson.Clone();

        if (root.TryGetProperty("json", out var json))
        {
            if (json.ValueKind == JsonValueKind.Object) return json.Clone();
            if (json.ValueKind == JsonValueKind.String)
            {
                var payload = json.GetString();
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            return doc.RootElement.Clone();
                    }
                    catch
                    {
                        // ignore invalid embedded JSON
                    }
                }
            }
        }

        if (root.TryGetProperty("screens", out var screens) && screens.ValueKind == JsonValueKind.Array)
            return root.Clone();

        return null;
    }

    private static bool TryBuildDefinitionFromMetaFlow(
        JsonElement metaRoot,
        out string definitionJson,
        out string metaFlowName,
        out string warning)
    {
        definitionJson = "{}";
        metaFlowName = metaRoot.TryGetProperty("name", out var name) ? (name.GetString() ?? string.Empty) : string.Empty;
        warning = string.Empty;

        var maybeSchema = TryExtractMetaFlowSchema(metaRoot);
        if (!maybeSchema.HasValue) return false;

        var schema = maybeSchema.Value;
        if (!schema.TryGetProperty("screens", out var screens) || screens.ValueKind != JsonValueKind.Array || screens.GetArrayLength() == 0)
            return false;

        var nodes = new List<object>();
        var screenNodeById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var screenOrder = new List<string>();
        var routeHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var screen in screens.EnumerateArray())
        {
            if (screen.ValueKind != JsonValueKind.Object) continue;
            var screenId = screen.TryGetProperty("id", out var sid) ? (sid.GetString() ?? string.Empty) : string.Empty;
            if (string.IsNullOrWhiteSpace(screenId)) screenId = $"screen_{screenOrder.Count + 1}";
            var title = screen.TryGetProperty("title", out var st) ? (st.GetString() ?? screenId) : screenId;

            var components = CollectMetaComponents(screen);

            var formFields = new List<object>();
            foreach (var comp in components)
            {
                if (comp.ValueKind != JsonValueKind.Object) continue;
                var compType = comp.TryGetProperty("type", out var ctype) ? (ctype.GetString() ?? string.Empty).ToLowerInvariant() : string.Empty;
                if (compType is "input" or "text_input" or "dropdown" or "select" or "radio" or "checkbox" or "date" or "phone")
                {
                    var fieldKey = comp.TryGetProperty("id", out var cid) ? (cid.GetString() ?? $"field_{formFields.Count + 1}") : $"field_{formFields.Count + 1}";
                    var fieldLabel = comp.TryGetProperty("label", out var cl) ? (cl.GetString() ?? fieldKey) : fieldKey;
                    var required = comp.TryGetProperty("required", out var cr) && cr.ValueKind == JsonValueKind.True;
                    formFields.Add(new
                    {
                        key = fieldKey,
                        label = fieldLabel,
                        type = string.IsNullOrWhiteSpace(compType) ? "text" : compType,
                        required
                    });
                }

                if (comp.TryGetProperty("nextScreen", out var nextScreenProp))
                {
                    var nextScreen = nextScreenProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(nextScreen) && !routeHints.ContainsKey(screenId))
                        routeHints[screenId] = nextScreen;
                }
            }

            var nodeType = formFields.Count > 0 ? "form" : "text";
            var nodeId = $"meta_{screenId}";
            screenNodeById[screenId] = nodeId;
            screenOrder.Add(screenId);
            nodes.Add(new
            {
                id = nodeId,
                type = nodeType,
                name = title,
                x = 360 + (screenOrder.Count - 1) * 260,
                y = 220,
                next = "",
                onTrue = "",
                onFalse = "",
                config = nodeType == "form"
                    ? new { title = title, fields = formFields }
                    : (object)new { body = title }
            });
        }

        var startNodeId = "meta_start";
        var endNodeId = "meta_end";
        nodes.Insert(0, new
        {
            id = startNodeId,
            type = "start",
            name = "Start",
            x = 120,
            y = 220,
            next = screenOrder.Count > 0 ? screenNodeById[screenOrder[0]] : endNodeId,
            onTrue = "",
            onFalse = "",
            config = new { source = "meta_import" }
        });
        nodes.Add(new
        {
            id = endNodeId,
            type = "end",
            name = "End",
            x = 360 + Math.Max(1, screenOrder.Count) * 260,
            y = 220,
            next = "",
            onTrue = "",
            onFalse = "",
            config = new { }
        });

        var defaultNextByScreen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var routeStartScreenId = string.Empty;
        if (routeHints.Count > 0)
        {
            foreach (var pair in routeHints) defaultNextByScreen[pair.Key] = pair.Value;
        }
        if (schema.TryGetProperty("routing", out var routing) && routing.ValueKind == JsonValueKind.Object &&
            routing.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
        {
            foreach (var edge in edges.EnumerateArray())
            {
                if (edge.ValueKind != JsonValueKind.Object) continue;
                var from = edge.TryGetProperty("from", out var fromProp) ? (fromProp.GetString() ?? string.Empty) : string.Empty;
                var to = edge.TryGetProperty("to", out var toProp) ? (toProp.GetString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
                if (!defaultNextByScreen.ContainsKey(from))
                    defaultNextByScreen[from] = to;
            }

            if (routing.TryGetProperty("startScreen", out var ss) && ss.ValueKind == JsonValueKind.String)
            {
                var s = ss.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(s) && screenNodeById.ContainsKey(s))
                    routeStartScreenId = s;
            }
            else if (routing.TryGetProperty("start_screen", out var ss2) && ss2.ValueKind == JsonValueKind.String)
            {
                var s = ss2.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(s) && screenNodeById.ContainsKey(s))
                    routeStartScreenId = s;
            }
        }

        if (defaultNextByScreen.Count == 0)
        {
            for (var i = 0; i < screenOrder.Count - 1; i++)
                defaultNextByScreen[screenOrder[i]] = screenOrder[i + 1];
        }

        var materialized = nodes
            .Select(x => JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(x), JsonOptions)!)
            .ToList();
        foreach (var node in materialized)
        {
            var nodeId = node.TryGetValue("id", out var idObj) ? idObj?.ToString() ?? string.Empty : string.Empty;
            if (nodeId == startNodeId)
            {
                if (!string.IsNullOrWhiteSpace(routeStartScreenId) && screenNodeById.TryGetValue(routeStartScreenId, out var routedStartNode))
                    node["next"] = routedStartNode;
                continue;
            }
            if (!nodeId.StartsWith("meta_", StringComparison.OrdinalIgnoreCase) || nodeId is "meta_start" or "meta_end")
                continue;
            var screenId = nodeId["meta_".Length..];
            if (defaultNextByScreen.TryGetValue(screenId, out var nextScreen) && screenNodeById.TryGetValue(nextScreen, out var nextNodeId))
                node["next"] = nextNodeId;
            else
                node["next"] = endNodeId;
        }

        definitionJson = JsonSerializer.Serialize(new
        {
            trigger = new { type = "keyword", keywords = new[] { "hi", "hello" } },
            source = "meta",
            meta = new
            {
                flowId = metaRoot.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                flowName = metaFlowName,
                importedAtUtc = DateTime.UtcNow,
                schema
            },
            startNodeId = startNodeId,
            nodes = materialized
        }, JsonOptions);

        if (screenOrder.Count < 2) warning = "Imported flow has single screen; routing is minimal.";
        return true;
    }

    private static List<JsonElement> CollectMetaComponents(JsonElement screen)
    {
        var result = new List<JsonElement>();

        void Traverse(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("type", out _)) result.Add(node.Clone());
            if (node.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in components.EnumerateArray()) Traverse(c);
            }
            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in children.EnumerateArray()) Traverse(c);
            }
            if (node.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.Object)
            {
                Traverse(layout);
            }
        }

        Traverse(screen);
        return result;
    }

    private static List<string> GetUnsupportedNodeTypes(string definitionJson)
    {
        var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(definitionJson) ? "{}" : definitionJson);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                return [];

            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("type", out var typeNode)) continue;
                var type = (typeNode.ToString() ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
                if (string.IsNullOrWhiteSpace(type)) continue;
                if (!SupportedPublishNodeTypes.Contains(type))
                    unsupported.Add(type);
            }
        }
        catch
        {
            // keep current behavior for malformed JSON; other validators will handle parse failures.
        }
        return unsupported.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, object?> ParseObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Dictionary<string, object?>();
            var output = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateObject()) output[item.Name] = item.Value.ToString();
            return output;
        }
        catch
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ResolveValue(Dictionary<string, object?> config, Dictionary<string, object?> payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw?.ToString()))
                return Interpolate(raw!.ToString()!, payload);
            if (payload.TryGetValue(key, out var fromPayload) && !string.IsNullOrWhiteSpace(fromPayload?.ToString()))
                return fromPayload!.ToString()!;
        }
        return string.Empty;
    }

    private static bool EvaluateCondition(Dictionary<string, object?> config, Dictionary<string, object?> payload)
    {
        var field = config.TryGetValue("field", out var f) ? f?.ToString() ?? string.Empty : string.Empty;
        var @operator = config.TryGetValue("operator", out var op) ? op?.ToString()?.ToLowerInvariant() ?? "equals" : "equals";
        var expected = config.TryGetValue("value", out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        var actual = payload.TryGetValue(field, out var a) ? a?.ToString() ?? string.Empty : string.Empty;

        return @operator switch
        {
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "starts_with" => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            "ends_with" => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            "not_equals" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            "regex" => System.Text.RegularExpressions.Regex.IsMatch(actual, expected),
            _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string Interpolate(string text, Dictionary<string, object?> payload)
    {
        var output = text;
        foreach (var pair in payload)
        {
            output = output.Replace($"{{{{{pair.Key}}}}}", pair.Value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        return output;
    }

    private async Task<string> FindFaqAnswer(string inboundText, CancellationToken ct)
    {
        var text = (inboundText ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var inboundNormalized = (inboundText ?? string.Empty).Trim();

        var items = await db.FaqKnowledgeItems
            .Where(x => x.TenantId == tenancy.TenantId && x.IsActive)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(500)
            .ToListAsync(ct);
        if (items.Count == 0) return string.Empty;

        var exact = items.FirstOrDefault(x => string.Equals((x.Question ?? string.Empty).Trim(), inboundNormalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Answer;

        foreach (var item in items)
        {
            var q = (item.Question ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(q)) continue;
            if (text.Contains(q) || q.Contains(text)) return item.Answer;
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .Distinct()
            .ToArray();
        if (tokens.Length == 0) return string.Empty;

        var best = items
            .Select(x => new
            {
                item = x,
                score = tokens.Count(t => (x.Question ?? string.Empty).Contains(t, StringComparison.OrdinalIgnoreCase))
            })
            .OrderByDescending(x => x.score)
            .FirstOrDefault();

        return best is not null && best.score >= 2 ? best.item.Answer : string.Empty;
    }

    private static (Dictionary<string, FlowNode> nodes, string startNodeId) ParseFlowDefinition(string definitionJson)
    {
        var nodes = new Dictionary<string, FlowNode>(StringComparer.OrdinalIgnoreCase);
        var startNodeId = string.Empty;

        using var doc = JsonDocument.Parse(definitionJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("startNodeId", out var start)) startNodeId = start.ToString();
        if (root.TryGetProperty("nodes", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var node = new FlowNode
                {
                    id = item.TryGetProperty("id", out var id) ? id.ToString() : Guid.NewGuid().ToString("N"),
                    type = item.TryGetProperty("type", out var type) ? type.ToString() : "text",
                    name = item.TryGetProperty("name", out var name) ? name.ToString() : string.Empty,
                    next = item.TryGetProperty("next", out var next) ? next.ToString() : string.Empty,
                    onTrue = item.TryGetProperty("onTrue", out var onTrue) ? onTrue.ToString() : string.Empty,
                    onFalse = item.TryGetProperty("onFalse", out var onFalse) ? onFalse.ToString() : string.Empty,
                    onSuccess = item.TryGetProperty("onSuccess", out var onSuccess) ? onSuccess.ToString() : string.Empty,
                    onFailure = item.TryGetProperty("onFailure", out var onFailure) ? onFailure.ToString() : string.Empty,
                    config = item.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object
                        ? cfg.EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                };
                nodes[node.id] = node;
                if (string.IsNullOrWhiteSpace(startNodeId) && node.type.Equals("start", StringComparison.OrdinalIgnoreCase))
                    startNodeId = node.id;
            }
        }

        if (string.IsNullOrWhiteSpace(startNodeId) && nodes.Count > 0) startNodeId = nodes.Keys.First();
        return (nodes, startNodeId);
    }

    private sealed class FlowNode
    {
        public string id { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string next { get; set; } = string.Empty;
        public string onTrue { get; set; } = string.Empty;
        public string onFalse { get; set; } = string.Empty;
        public string onSuccess { get; set; } = string.Empty;
        public string onFailure { get; set; } = string.Empty;
        public Dictionary<string, object?> config { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FlowValidationReport
    {
        public bool IsValid { get; set; }
        public string Version { get; set; } = string.Empty;
        public int NodeCount { get; set; }
        public int ScreenCount { get; set; }
        public int ComponentCount { get; set; }
        public int RouteCount { get; set; }
        public List<string> Errors { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }
}
