-- Phase 1 additive workflow schema
-- Safe to run on production: creates new tables/indexes only.

CREATE TABLE IF NOT EXISTS "WorkflowExecutionStates" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "FlowId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "CurrentNodeId" text NOT NULL DEFAULT '',
    "ExecutionData" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "Status" text NOT NULL DEFAULT 'running',
    "StartedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "LastUpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "CompletedAtUtc" timestamp with time zone NULL,
    "ExecutionTrace" jsonb NOT NULL DEFAULT '[]'::jsonb,
    "ErrorMessage" text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_TenantId" ON "WorkflowExecutionStates" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_FlowId" ON "WorkflowExecutionStates" ("FlowId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_ConversationId" ON "WorkflowExecutionStates" ("ConversationId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionStates_Status" ON "WorkflowExecutionStates" ("Status");

CREATE TABLE IF NOT EXISTS "WorkflowExecutionLogs" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "ExecutionStateId" uuid NOT NULL,
    "NodeId" text NOT NULL DEFAULT '',
    "NodeType" text NOT NULL DEFAULT '',
    "NodeName" text NOT NULL DEFAULT '',
    "ExecutedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "Status" text NOT NULL DEFAULT '',
    "DurationMs" integer NOT NULL DEFAULT 0,
    "InputData" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "OutputData" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "ErrorMessage" text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionLogs_TenantId" ON "WorkflowExecutionLogs" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionLogs_ExecutionStateId" ON "WorkflowExecutionLogs" ("ExecutionStateId");
CREATE INDEX IF NOT EXISTS "IX_WorkflowExecutionLogs_ExecutedAtUtc" ON "WorkflowExecutionLogs" ("ExecutedAtUtc");

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
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_TenantId" ON "TriggerEvaluationAudit" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_IsMatch" ON "TriggerEvaluationAudit" ("IsMatch");
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_EvaluatedAtUtc" ON "TriggerEvaluationAudit" ("EvaluatedAtUtc");

CREATE TABLE IF NOT EXISTS "AgentAvailability" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Status" text NOT NULL DEFAULT 'online',
    "QueueCount" integer NOT NULL DEFAULT 0,
    "LastHeartbeat" timestamp with time zone NOT NULL DEFAULT now(),
    "MaxQueue" integer NOT NULL DEFAULT 10,
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT "UQ_AgentAvailability_TenantUser" UNIQUE ("TenantId", "UserId")
);
CREATE INDEX IF NOT EXISTS "IX_AgentAvailability_TenantId" ON "AgentAvailability" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_AgentAvailability_Status" ON "AgentAvailability" ("Status");
CREATE INDEX IF NOT EXISTS "IX_AgentAvailability_LastHeartbeat" ON "AgentAvailability" ("LastHeartbeat");

CREATE TABLE IF NOT EXISTS "ConversationQueue" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "AssignedToAgentId" uuid NULL,
    "QueuedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "AssignedAtUtc" timestamp with time zone NULL,
    "ClosedAtUtc" timestamp with time zone NULL,
    "Priority" integer NOT NULL DEFAULT 0,
    "SlaMinutesToRespond" integer NOT NULL DEFAULT 5,
    "Status" text NOT NULL DEFAULT 'queued',
    "Notes" text NOT NULL DEFAULT '',
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS "IX_ConversationQueue_TenantId" ON "ConversationQueue" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_ConversationQueue_Status" ON "ConversationQueue" ("Status");
CREATE INDEX IF NOT EXISTS "IX_ConversationQueue_QueuedAtUtc" ON "ConversationQueue" ("QueuedAtUtc");
CREATE INDEX IF NOT EXISTS "IX_ConversationQueue_AssignedToAgentId" ON "ConversationQueue" ("AssignedToAgentId");

CREATE TABLE IF NOT EXISTS "ScheduledMessages" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "FlowId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "NodeId" text NOT NULL DEFAULT '',
    "ScheduledForUtc" timestamp with time zone NOT NULL,
    "MessageContent" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "Status" text NOT NULL DEFAULT 'pending',
    "RetryCount" integer NOT NULL DEFAULT 0,
    "MaxRetries" integer NOT NULL DEFAULT 3,
    "NextRetryAtUtc" timestamp with time zone NULL,
    "SentAtUtc" timestamp with time zone NULL,
    "FailureReason" text NOT NULL DEFAULT '',
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_ScheduledForUtc" ON "ScheduledMessages" ("ScheduledForUtc");
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_Status" ON "ScheduledMessages" ("Status");
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_TenantId" ON "ScheduledMessages" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_NextRetryAtUtc" ON "ScheduledMessages" ("NextRetryAtUtc");

CREATE TABLE IF NOT EXISTS "AgentActivityLog" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "AgentId" uuid NOT NULL,
    "ConversationId" uuid NOT NULL,
    "ActivityType" text NOT NULL DEFAULT '',
    "StartedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
    "EndedAtUtc" timestamp with time zone NULL,
    "DurationSeconds" integer NULL,
    "Notes" text NOT NULL DEFAULT '',
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS "IX_AgentActivityLog_TenantId" ON "AgentActivityLog" ("TenantId");
CREATE INDEX IF NOT EXISTS "IX_AgentActivityLog_AgentId" ON "AgentActivityLog" ("AgentId");
CREATE INDEX IF NOT EXISTS "IX_AgentActivityLog_StartedAtUtc" ON "AgentActivityLog" ("StartedAtUtc");

