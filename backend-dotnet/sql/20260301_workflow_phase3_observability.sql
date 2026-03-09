-- Phase 3 additive workflow observability + safety
-- Safe to run multiple times.

-- 1) Hard-stop cross-tenant active mapping collisions.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TenantWabaConfigs_Active_PhoneNumberId"
ON "TenantWabaConfigs" ("PhoneNumberId")
WHERE "IsActive" = true AND "PhoneNumberId" <> '';

-- 2) Trigger evaluation audit performance.
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_FlowId"
ON "TriggerEvaluationAudit" ("FlowId");
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_InboundMessageId"
ON "TriggerEvaluationAudit" ("InboundMessageId");
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_IsMatch_EvaluatedAtUtc"
ON "TriggerEvaluationAudit" ("IsMatch", "EvaluatedAtUtc");

-- 3) Ensure execution state/log tables exist for per-node diagnostics.
CREATE TABLE IF NOT EXISTS "WorkflowExecutionStates" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "FlowId" uuid NOT NULL,
    "ConversationId" uuid NULL,
    "CurrentNodeId" text NOT NULL DEFAULT '',
    "ExecutionData" text NOT NULL DEFAULT '{}',
    "Status" text NOT NULL DEFAULT 'running',
    "StartedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "LastUpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "CompletedAtUtc" timestamp with time zone NULL,
    "ExecutionTrace" text NOT NULL DEFAULT '[]',
    "ErrorMessage" text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_TenantId"
ON "WorkflowExecutionStates" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_FlowId"
ON "WorkflowExecutionStates" ("FlowId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_Status"
ON "WorkflowExecutionStates" ("Status");

CREATE TABLE IF NOT EXISTS "WorkflowExecutionLogs" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "ExecutionStateId" uuid NOT NULL,
    "NodeId" text NOT NULL DEFAULT '',
    "NodeType" text NOT NULL DEFAULT '',
    "NodeName" text NOT NULL DEFAULT '',
    "ExecutedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "Status" text NOT NULL DEFAULT 'completed',
    "DurationMs" integer NOT NULL DEFAULT 0,
    "InputData" text NOT NULL DEFAULT '{}',
    "OutputData" text NOT NULL DEFAULT '{}',
    "ErrorMessage" text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionLogs_TenantId"
ON "WorkflowExecutionLogs" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionLogs_ExecutionStateId"
ON "WorkflowExecutionLogs" ("ExecutionStateId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionLogs_ExecutedAtUtc"
ON "WorkflowExecutionLogs" ("ExecutedAtUtc");

-- 4) Delay-resume scheduling performance.
CREATE TABLE IF NOT EXISTS "ScheduledMessages" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "FlowId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "NodeId" text NOT NULL DEFAULT '',
    "ScheduledForUtc" timestamp with time zone NOT NULL,
    "MessageContent" text NOT NULL DEFAULT '{}',
    "Status" text NOT NULL DEFAULT 'pending',
    "RetryCount" integer NOT NULL DEFAULT 0,
    "MaxRetries" integer NOT NULL DEFAULT 3,
    "NextRetryAtUtc" timestamp with time zone NULL,
    "FailureReason" text NOT NULL DEFAULT '',
    "SentAtUtc" timestamp with time zone NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_Status_ScheduledForUtc"
ON "ScheduledMessages" ("Status", "ScheduledForUtc");
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_NextRetryAtUtc"
ON "ScheduledMessages" ("NextRetryAtUtc");
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_TenantId"
ON "ScheduledMessages" ("TenantId");
