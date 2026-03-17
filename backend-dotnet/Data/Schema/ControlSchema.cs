using Microsoft.EntityFrameworkCore;

namespace Textzy.Api.Data.Schema;

public static class ControlSchema
{
    public static void EnsureControlAuthSchema(ControlDbContext db)
    {
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Tenants" (
                "Id" uuid PRIMARY KEY,
                "Name" text NOT NULL,
                "Slug" text NOT NULL,
                "OwnerGroupId" uuid NULL,
                "DataConnectionString" text NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);

        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tenants_Slug" ON "Tenants" ("Slug");""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "OwnerGroupId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Tenants_OwnerGroupId" ON "Tenants" ("OwnerGroupId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantOwnerGroups" (
                "Id" uuid PRIMARY KEY,
                "OwnerUserId" uuid NOT NULL,
                "Name" text NOT NULL,
                "SmsProviderRoute" text NOT NULL DEFAULT 'tata',
                "IsActive" boolean NOT NULL DEFAULT true,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantOwnerGroups_OwnerUserId" ON "TenantOwnerGroups" ("OwnerUserId");""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantOwnerGroups" ADD COLUMN IF NOT EXISTS "SmsProviderRoute" text NOT NULL DEFAULT 'tata';""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantCompanyProfiles" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "OwnerGroupId" uuid NULL,
                "CompanyName" text NOT NULL DEFAULT '',
                "LegalName" text NOT NULL DEFAULT '',
                "Industry" text NOT NULL DEFAULT '',
                "Website" text NOT NULL DEFAULT '',
                "CompanySize" text NOT NULL DEFAULT '',
                "Gstin" text NOT NULL DEFAULT '',
                "Pan" text NOT NULL DEFAULT '',
                "Address" text NOT NULL DEFAULT '',
                "BillingEmail" text NOT NULL DEFAULT '',
                "BillingPhone" text NOT NULL DEFAULT '',
                "IsActive" boolean NOT NULL DEFAULT true,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantCompanyProfiles_TenantId" ON "TenantCompanyProfiles" ("TenantId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantCompanyProfiles_OwnerGroupId" ON "TenantCompanyProfiles" ("OwnerGroupId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Users" (
                "Id" uuid PRIMARY KEY,
                "Email" text NOT NULL,
                "PasswordHash" text NOT NULL,
                "FullName" text NOT NULL DEFAULT '',
                "Phone" text NOT NULL DEFAULT '',
                "IsActive" boolean NOT NULL DEFAULT true,
                "IsSuperAdmin" boolean NOT NULL DEFAULT false,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "IsSuperAdmin" boolean NOT NULL DEFAULT false;""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantUsers" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "Role" text NOT NULL DEFAULT 'owner',
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantUsers_TenantId_UserId" ON "TenantUsers" ("TenantId", "UserId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantUsers_UserId" ON "TenantUsers" ("UserId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "SessionTokens" (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "Token" text NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "ExpiresAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SessionTokens_UserId" ON "SessionTokens" ("UserId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TwoFactorChallenges" (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL,
                "Type" text NOT NULL DEFAULT 'totp',
                "Challenge" text NOT NULL DEFAULT '',
                "ExpiresAtUtc" timestamp with time zone NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TwoFactorChallenges_UserId" ON "TwoFactorChallenges" ("UserId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "PlatformSettings" (
                "Id" uuid PRIMARY KEY,
                "Scope" text NOT NULL DEFAULT '',
                "Key" text NOT NULL DEFAULT '',
                "ValueEncrypted" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlatformSettings_Scope_Key" ON "PlatformSettings" ("Scope","Key");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NULL,
                "ActorUserId" uuid NULL,
                "Action" text NOT NULL DEFAULT '',
                "Details" text NOT NULL DEFAULT '',
                "IpAddress" text NOT NULL DEFAULT '',
                "UserAgent" text NOT NULL DEFAULT '',
                "DeviceLabel" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AuditLogs_TenantId_CreatedAtUtc" ON "AuditLogs" ("TenantId","CreatedAtUtc");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TeamInvitations" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Email" text NOT NULL DEFAULT '',
                "Role" text NOT NULL DEFAULT 'member',
                "InviteToken" text NOT NULL DEFAULT '',
                "ExpiresAtUtc" timestamp with time zone NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TeamInvitations_TenantId_Email" ON "TeamInvitations" ("TenantId","Email");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantUserPermissionOverrides" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "PermissionKey" text NOT NULL DEFAULT '',
                "IsAllowed" boolean NOT NULL DEFAULT true,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantUserPermissionOverrides_TenantUser" ON "TenantUserPermissionOverrides" ("TenantId","UserId");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "BillingPlans" (
                "Id" uuid PRIMARY KEY,
                "Code" text NOT NULL DEFAULT '',
                "Name" text NOT NULL DEFAULT '',
                "PricingModel" text NOT NULL DEFAULT '',
                "Currency" text NOT NULL DEFAULT 'INR',
                "PriceMonthly" numeric(18,2) NOT NULL DEFAULT 0,
                "PriceYearly" numeric(18,2) NOT NULL DEFAULT 0,
                "TaxMode" text NOT NULL DEFAULT 'exclusive',
                "Description" text NOT NULL DEFAULT '',
                "FeaturesJson" text NOT NULL DEFAULT '[]',
                "LimitsJson" text NOT NULL DEFAULT '{}',
                "SortOrder" integer NOT NULL DEFAULT 0,
                "IsActive" boolean NOT NULL DEFAULT true,
                "IsPublic" boolean NOT NULL DEFAULT false,
                "UsageUnitName" text NOT NULL DEFAULT '',
                "IncludedQuantity" integer NOT NULL DEFAULT 0,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillingPlans_Code" ON "BillingPlans" ("Code");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantSubscriptions" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "PlanId" uuid NOT NULL,
                "Status" text NOT NULL DEFAULT 'active',
                "BillingCycle" text NOT NULL DEFAULT 'monthly',
                "StartedAtUtc" timestamp with time zone NOT NULL,
                "RenewAtUtc" timestamp with time zone NOT NULL,
                "CancelledAtUtc" timestamp with time zone NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantSubscriptions_TenantId_CreatedAtUtc" ON "TenantSubscriptions" ("TenantId","CreatedAtUtc");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantUsages" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "MetricKey" text NOT NULL DEFAULT '',
                "BucketDateUtc" timestamp with time zone NOT NULL,
                "Units" integer NOT NULL DEFAULT 0,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantUsages_Tenant_Metric_Bucket" ON "TenantUsages" ("TenantId","MetricKey","BucketDateUtc");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "BillingInvoices" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "InvoiceNo" text NOT NULL DEFAULT '',
                "InvoiceKind" text NOT NULL DEFAULT 'tax_invoice',
                "BillingCycle" text NOT NULL DEFAULT 'monthly',
                "TaxMode" text NOT NULL DEFAULT 'exclusive',
                "ReferenceNo" text NOT NULL DEFAULT '',
                "Description" text NOT NULL DEFAULT '',
                "PeriodStartUtc" timestamp with time zone NOT NULL,
                "PeriodEndUtc" timestamp with time zone NOT NULL,
                "Subtotal" numeric(18,2) NOT NULL DEFAULT 0,
                "TaxAmount" numeric(18,2) NOT NULL DEFAULT 0,
                "Total" numeric(18,2) NOT NULL DEFAULT 0,
                "Status" text NOT NULL DEFAULT 'issued',
                "PaidAtUtc" timestamp with time zone NULL,
                "PdfUrl" text NOT NULL DEFAULT '',
                "IntegrityHash" text NOT NULL DEFAULT '',
                "IntegrityAlgo" text NOT NULL DEFAULT 'SHA256',
                "IssuedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_BillingInvoices_TenantId_CreatedAtUtc" ON "BillingInvoices" ("TenantId","CreatedAtUtc");""");

        db.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "BillingPaymentAttempts" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "OrderId" text NOT NULL,
                "PaymentId" text NOT NULL,
                "Amount" numeric(18,2) NOT NULL,
                "Currency" text NOT NULL,
                "Status" text NOT NULL,
                "NotesJson" text NOT NULL,
                "RawResponse" text NOT NULL,
                "LastError" text NOT NULL,
                "PaidAtUtc" timestamp with time zone NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "UpdatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillingPaymentAttempts_OrderId" ON "BillingPaymentAttempts" ("OrderId");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_BillingPaymentAttempts_TenantId_CreatedAtUtc" ON "BillingPaymentAttempts" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_BillingPaymentAttempts_Tenant_Order_PaidAtUtc" ON "BillingPaymentAttempts" ("TenantId","OrderId","PaidAtUtc" DESC,"UpdatedAtUtc" DESC);""");
    }
}
