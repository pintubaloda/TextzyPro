-- Add tenant credit ledger table for debit/refund/credit tracking.
-- Safe to run multiple times.

CREATE TABLE IF NOT EXISTS "TenantCreditTransactions" (
    "Id" uuid PRIMARY KEY,
    "TenantId" uuid NOT NULL,
    "MetricKey" text NOT NULL DEFAULT '',
    "TransactionType" text NOT NULL DEFAULT '',
    "Units" integer NOT NULL DEFAULT 0,
    "Source" text NOT NULL DEFAULT '',
    "Service" text NOT NULL DEFAULT '',
    "ReferenceId" text NOT NULL DEFAULT '',
    "Status" text NOT NULL DEFAULT 'applied',
    "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_TenantCreditTransactions_TenantId"
ON "TenantCreditTransactions" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_TenantCreditTransactions_MetricKey"
ON "TenantCreditTransactions" ("MetricKey");

CREATE INDEX IF NOT EXISTS "IX_TenantCreditTransactions_TransactionType"
ON "TenantCreditTransactions" ("TransactionType");

CREATE INDEX IF NOT EXISTS "IX_TenantCreditTransactions_ReferenceId"
ON "TenantCreditTransactions" ("ReferenceId");

CREATE INDEX IF NOT EXISTS "IX_TenantCreditTransactions_CreatedAtUtc"
ON "TenantCreditTransactions" ("CreatedAtUtc");
