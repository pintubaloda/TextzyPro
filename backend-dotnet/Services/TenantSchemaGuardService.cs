using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Textzy.Api.Data;

namespace Textzy.Api.Services;

public class TenantSchemaGuardService(IMemoryCache cache, ILogger<TenantSchemaGuardService> logger)
{
    private static string CacheKey(Guid tenantId) => $"tenant_schema_guard:{tenantId:N}";

    public async Task EnsureContactEncryptionColumnsAsync(Guid tenantId, string tenantConnectionString, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(tenantConnectionString)) return;
        if (cache.TryGetValue(CacheKey(tenantId), out _)) return;

        using var db = SeedData.CreateTenantDbContext(tenantConnectionString);
        // Contact/PII compatibility
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "SegmentId" uuid NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "Email" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "TagsCsv" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "NameEncrypted" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "EmailEncrypted" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "PhoneEncrypted" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "PhoneHash" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Contacts_Tenant_PhoneHash" ON "Contacts" ("TenantId","PhoneHash");""", ct);

        // WABA config compatibility for older tenant DBs
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "TenantWabaConfigs" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "WabaId" text NOT NULL,
                "PhoneNumberId" text NOT NULL,
                "DisplayPhoneNumber" text NOT NULL,
                "BusinessAccountName" text NOT NULL,
                "AccessToken" text NOT NULL,
                "ConnectedAtUtc" timestamp with time zone NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "IsActive" boolean NOT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "OnboardingState" text NOT NULL DEFAULT 'requested';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "OnboardingStartedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "CodeReceivedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "ExchangedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "AssetsLinkedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "WebhookSubscribedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "WebhookVerifiedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "LastError" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "LastGraphError" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "BusinessVerificationStatus" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PhoneQualityRating" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PhoneNameStatus" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "MessagingLimitTier" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "AccountHealth" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PermissionAuditPassed" boolean NOT NULL DEFAULT false;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "BusinessManagerId" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "SystemUserId" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "SystemUserName" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "SystemUserCreatedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "AssetsAssignedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PermanentTokenIssuedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "PermanentTokenExpiresAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TokenSource" text NOT NULL DEFAULT 'embedded_exchange';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TemplatesSyncedAtUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TemplatesSyncStatus" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "TenantWabaConfigs" ADD COLUMN IF NOT EXISTS "TemplatesSyncFailCount" integer NOT NULL DEFAULT 0;""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_TenantWabaConfigs_TenantId" ON "TenantWabaConfigs" ("TenantId");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "UX_TenantWabaConfigs_Active_PhoneNumberId" ON "TenantWabaConfigs" ("PhoneNumberId") WHERE "IsActive" = true AND "PhoneNumberId" <> '';""", ct);

        // Templates compatibility for older tenant DBs
        await db.Database.ExecuteSqlRawAsync("""CREATE TABLE IF NOT EXISTS "Templates" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Name" text NOT NULL DEFAULT '', "Channel" integer NOT NULL DEFAULT 0, "Category" text NOT NULL DEFAULT 'UTILITY', "Language" text NOT NULL DEFAULT 'en', "Body" text NOT NULL DEFAULT '', "LifecycleStatus" text NOT NULL DEFAULT 'draft', "Version" integer NOT NULL DEFAULT 1, "VariantGroup" text NOT NULL DEFAULT '', "Status" text NOT NULL DEFAULT 'Approved', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "DltEntityId" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "DltTemplateId" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "SmsSenderId" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderType" text NOT NULL DEFAULT 'none';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderText" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderMediaId" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "HeaderMediaName" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "FooterText" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "ButtonsJson" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "RejectionReason" text NOT NULL DEFAULT '';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "SmsOperator" text NOT NULL DEFAULT 'all';""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "EffectiveFromUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "EffectiveToUtc" timestamp with time zone NULL;""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_Templates_Tenant_Channel" ON "Templates" ("TenantId","Channel");""", ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SmsOptOuts" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Phone" text NOT NULL DEFAULT '',
                "Reason" text NOT NULL DEFAULT '',
                "Source" text NOT NULL DEFAULT 'manual',
                "OptedOutAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
                "IsActive" boolean NOT NULL DEFAULT true,
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SmsOptOuts_Tenant_Phone" ON "SmsOptOuts" ("TenantId","Phone");""", ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "SmsBillingLedgers" (
                "Id" uuid PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "MessageId" uuid NOT NULL,
                "Recipient" text NOT NULL DEFAULT '',
                "ProviderMessageId" text NOT NULL DEFAULT '',
                "Currency" text NOT NULL DEFAULT 'INR',
                "UnitPrice" numeric(18,4) NOT NULL DEFAULT 0,
                "Segments" integer NOT NULL DEFAULT 1,
                "TotalAmount" numeric(18,4) NOT NULL DEFAULT 0,
                "BillingState" text NOT NULL DEFAULT 'charged',
                "DeliveryState" text NOT NULL DEFAULT 'submitted',
                "Notes" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAtUtc" timestamp with time zone NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SmsBillingLedgers_Tenant_CreatedAtUtc" ON "SmsBillingLedgers" ("TenantId","CreatedAtUtc");""", ct);
        await db.Database.ExecuteSqlRawAsync("""CREATE INDEX IF NOT EXISTS "IX_SmsBillingLedgers_MessageId" ON "SmsBillingLedgers" ("MessageId");""", ct);

        cache.Set(CacheKey(tenantId), true, TimeSpan.FromMinutes(30));
        logger.LogInformation("Tenant schema guard applied for tenantId={TenantId}", tenantId);
    }
}
