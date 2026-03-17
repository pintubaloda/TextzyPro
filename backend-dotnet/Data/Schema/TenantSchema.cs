using Microsoft.EntityFrameworkCore;

namespace Textzy.Api.Data.Schema;

public static class TenantSchema
{
    public static void EnsureTenantWabaSchema(TenantDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantWabaConfigs" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "WabaId" text NOT NULL DEFAULT '',
            "PhoneNumberId" text NOT NULL DEFAULT '',
            "BusinessAccountName" text NOT NULL DEFAULT '',
            "DisplayPhoneNumber" text NOT NULL DEFAULT '',
            "AccessToken" text NOT NULL DEFAULT '',
            "IsActive" boolean NOT NULL DEFAULT false,
            "ConnectedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
        );
        """);

        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "OnboardingState" text NOT NULL DEFAULT 'requested';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "OnboardingStartedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "CodeReceivedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "ExchangedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "AssetsLinkedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "WebhookSubscribedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "WebhookVerifiedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "LastError" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "LastGraphError" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "BusinessVerificationStatus" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PhoneQualityRating" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PhoneNameStatus" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "MessagingLimitTier" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "AccountHealth" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PermissionAuditPassed" boolean NOT NULL DEFAULT false;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "BusinessManagerId" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "SystemUserId" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "SystemUserName" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "SystemUserCreatedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "AssetsAssignedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PermanentTokenIssuedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PermanentTokenExpiresAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TokenSource" text NOT NULL DEFAULT 'embedded_exchange';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TemplatesSyncedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TemplatesSyncStatus" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TemplatesSyncFailCount" integer NOT NULL DEFAULT 0;""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantWabaConfigs_TenantId" ON "TenantWabaConfigs" ("TenantId");""");
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "UX_TenantWabaConfigs_Active_PhoneNumberId" ON "TenantWabaConfigs" ("PhoneNumberId") WHERE "IsActive" = true AND "PhoneNumberId" <> '';""");
    }

    public static void EnsureTenantCoreSchema(TenantDbContext db)
    {
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "Campaigns" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Channel" integer NOT NULL DEFAULT 0, "TemplateText" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "Messages" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "CampaignId" uuid NULL, "Channel" integer NOT NULL DEFAULT 0, "Recipient" text NOT NULL DEFAULT '', "Body" text NOT NULL DEFAULT '', "MessageType" text NOT NULL DEFAULT 'session', "DeliveredAtUtc" timestamp with time zone NULL, "ReadAtUtc" timestamp with time zone NULL, "ProviderMessageId" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'Queued', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "IdempotencyKey" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "NextRetryAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "LastError" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "QueueProvider" text NOT NULL DEFAULT 'memory';""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Messages_Tenant_IdempotencyKey" ON "Messages" ("TenantId","IdempotencyKey");""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "IdempotencyKeys" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Key" text NOT NULL DEFAULT '', "MessageId" uuid NULL, "Status" text NOT NULL DEFAULT 'reserved', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "ExpiresAtUtc" timestamp with time zone NOT NULL DEFAULT (now() + interval '24 hour'));""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "IdempotencyKeys" ADD COLUMN IF NOT EXISTS "ExpiresAtUtc" timestamp with time zone NOT NULL DEFAULT (now() + interval '24 hour');""");
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_IdempotencyKeys_Tenant_Key" ON "IdempotencyKeys" ("TenantId","Key");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_IdempotencyKeys_MessageId" ON "IdempotencyKeys" ("MessageId");""");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "MessageEvents" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "MessageId" uuid NULL,
                "ProviderMessageId" text NOT NULL DEFAULT '',
                "Direction" text NOT NULL DEFAULT 'outbound',
                "EventType" text NOT NULL DEFAULT '',
                "State" text NOT NULL DEFAULT '',
                "StatePriority" integer NOT NULL DEFAULT 0,
                "EventTimestampUtc" timestamp with time zone NULL,
                "RecipientId" text NOT NULL DEFAULT '',
                "CustomerPhone" text NOT NULL DEFAULT '',
                "ConversationId" text NOT NULL DEFAULT '',
                "ConversationOriginType" text NOT NULL DEFAULT '',
                "ConversationExpirationUtc" timestamp with time zone NULL,
                "PricingBillable" boolean NULL,
                "PricingCategory" text NOT NULL DEFAULT '',
                "MessageType" text NOT NULL DEFAULT '',
                "MediaId" text NOT NULL DEFAULT '',
                "MediaMimeType" text NOT NULL DEFAULT '',
                "MediaSha256" text NOT NULL DEFAULT '',
                "ButtonPayload" text NOT NULL DEFAULT '',
                "ButtonText" text NOT NULL DEFAULT '',
                "InteractiveType" text NOT NULL DEFAULT '',
                "ListReplyId" text NOT NULL DEFAULT '',
                "ListReplyTitle" text NOT NULL DEFAULT '',
                "RawPayloadJson" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MessageEvents_Tenant_CreatedAtUtc" ON "MessageEvents" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MessageEvents_MessageId" ON "MessageEvents" ("MessageId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MessageEvents_ProviderMessageId" ON "MessageEvents" ("ProviderMessageId");""");
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "OutboundDeadLetters" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "MessageId" uuid NOT NULL,
                "IdempotencyKey" text NOT NULL DEFAULT '',
                "AttemptCount" integer NOT NULL DEFAULT 0,
                "Classification" text NOT NULL DEFAULT '',
                "ErrorCode" text NOT NULL DEFAULT '',
                "ErrorTitle" text NOT NULL DEFAULT '',
                "ErrorDetail" text NOT NULL DEFAULT '',
                "PayloadJson" text NOT NULL DEFAULT '{{}}',
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_OutboundDeadLetters_Tenant_CreatedAtUtc" ON "OutboundDeadLetters" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "Templates" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Channel" integer NOT NULL DEFAULT 0, "Category" text NOT NULL DEFAULT 'UTILITY', "Language" text NOT NULL DEFAULT 'en', "Body" text NOT NULL DEFAULT '', "LifecycleStatus" text NOT NULL DEFAULT 'draft', "Version" integer NOT NULL DEFAULT 1, "VariantGroup" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'Approved', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "DltEntityId" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "DltTemplateId" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "SmsSenderId" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderType" text NOT NULL DEFAULT 'none';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderText" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderMediaId" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderMediaName" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "FooterText" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "ButtonsJson" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "RejectionReason" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "ContactGroups" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "Contacts" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "GroupId" uuid NULL, "SegmentId" uuid NULL, "Name" text NOT NULL DEFAULT '', "Email" text NOT NULL DEFAULT '', "TagsCsv" text NOT NULL DEFAULT '', "Phone" text NOT NULL DEFAULT '', "OptInStatus" text NOT NULL DEFAULT 'unknown', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "SegmentId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "Email" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "TagsCsv" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "NameEncrypted" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "EmailEncrypted" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "PhoneEncrypted" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "PhoneHash" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Contacts_Tenant_PhoneHash" ON "Contacts" ("TenantId","PhoneHash");""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "ChatbotConfigs" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Greeting" text NOT NULL DEFAULT 'Hi! Welcome.', "Fallback" text NOT NULL DEFAULT 'Agent will join shortly.', "HandoffEnabled" boolean NOT NULL DEFAULT true, "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "SmsFlows" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'Active', "SentCount" integer NOT NULL DEFAULT 0, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "SmsInputFields" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Type" text NOT NULL DEFAULT 'text', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "SmsSenders" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "SenderId" text NOT NULL DEFAULT '', "EntityId" text NOT NULL DEFAULT '', "IsActive" boolean NOT NULL DEFAULT true, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "SmsSenders" ADD COLUMN IF NOT EXISTS "RouteType" text NOT NULL DEFAULT 'service_explicit';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "SmsSenders" ADD COLUMN IF NOT EXISTS "Purpose" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "SmsSenders" ADD COLUMN IF NOT EXISTS "Description" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "SmsSenders" ADD COLUMN IF NOT EXISTS "IsVerified" boolean NOT NULL DEFAULT false;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "SmsSenders" ADD COLUMN IF NOT EXISTS "VerifiedAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "ConversationWindows" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Recipient" text NOT NULL DEFAULT '', "LastInboundAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "Conversations" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "CustomerPhone" text NOT NULL DEFAULT '', "CustomerName" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'Open', "AssignedUserId" text NOT NULL DEFAULT '', "AssignedUserName" text NOT NULL DEFAULT '', "LabelsCsv" text NOT NULL DEFAULT '', "LastMessageAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "ConversationNotes" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ConversationId" uuid NOT NULL, "Body" text NOT NULL DEFAULT '', "CreatedByUserId" uuid NOT NULL, "CreatedByName" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "ContactCustomFields" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "ContactId" uuid NOT NULL, "FieldKey" text NOT NULL DEFAULT '', "FieldValue" text NOT NULL DEFAULT '', "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "ContactSegments" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "RuleJson" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "BroadcastJobs" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Channel" integer NOT NULL DEFAULT 0, "MessageBody" text NOT NULL DEFAULT '', "RecipientCsv" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'Queued', "RetryCount" integer NOT NULL DEFAULT 0, "MaxRetries" integer NOT NULL DEFAULT 3, "SentCount" integer NOT NULL DEFAULT 0, "FailedCount" integer NOT NULL DEFAULT 0, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "StartedAtUtc" timestamp with time zone NULL, "CompletedAtUtc" timestamp with time zone NULL);""");
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
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FlowRuntimeEvents" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NULL, "MetaFlowId" text NOT NULL DEFAULT '', "ConversationExternalId" text NOT NULL DEFAULT '', "CustomerPhone" text NOT NULL DEFAULT '', "EventType" text NOT NULL DEFAULT '', "EventSource" text NOT NULL DEFAULT '', "Success" boolean NOT NULL DEFAULT true, "StatusCode" integer NOT NULL DEFAULT 0, "DurationMs" integer NOT NULL DEFAULT 0, "ScreenId" text NOT NULL DEFAULT '', "ActionName" text NOT NULL DEFAULT '', "PayloadJson" text NOT NULL DEFAULT '{{}}', "ErrorDetail" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FlowRuntimeEvents_Tenant_CreatedAt" ON "FlowRuntimeEvents" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FlowRuntimeEvents_MetaFlowId" ON "FlowRuntimeEvents" ("MetaFlowId");""");
        db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FaqKnowledgeItems" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Question" text NOT NULL DEFAULT '', "Answer" text NOT NULL DEFAULT '', "Category" text NOT NULL DEFAULT '', "IsActive" boolean NOT NULL DEFAULT true, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FaqKnowledgeItems_Tenant_Active" ON "FaqKnowledgeItems" ("TenantId","IsActive");""");
    }
}
