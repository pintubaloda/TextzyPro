-- Phase 2 additive runtime hardening (safe to run multiple times)

-- Prevent cross-tenant active phone-number mapping collisions.
CREATE UNIQUE INDEX IF NOT EXISTS "UX_TenantWabaConfigs_Active_PhoneNumberId"
ON "TenantWabaConfigs" ("PhoneNumberId")
WHERE "IsActive" = true AND "PhoneNumberId" <> '';

-- Keep trigger debug queries fast.
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_FlowId" ON "TriggerEvaluationAudit" ("FlowId");
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_InboundMessageId" ON "TriggerEvaluationAudit" ("InboundMessageId");
CREATE INDEX IF NOT EXISTS "IX_TriggerEvaluationAudit_IsMatch_EvaluatedAtUtc" ON "TriggerEvaluationAudit" ("IsMatch", "EvaluatedAtUtc");

-- Keep scheduled delay resume scans fast.
CREATE INDEX IF NOT EXISTS "IX_ScheduledMessages_Status_ScheduledForUtc" ON "ScheduledMessages" ("Status", "ScheduledForUtc");
