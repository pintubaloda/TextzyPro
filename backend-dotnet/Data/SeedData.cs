using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Textzy.Api.Models;
using Textzy.Api.Services;

namespace Textzy.Api.Data;

public static class SeedData
{
    public static void InitializeControl(ControlDbContext db, string defaultTenantConnection)
    {
        var tenantIds = db.Tenants
            .OrderBy(t => t.CreatedAtUtc)
            .Select(t => t.Id)
            .Take(2)
            .ToList();

        if (tenantIds.Count >= 2)
            EnsureBillingSeeds(db, tenantIds[0], tenantIds[1]);

        EnsureWabaErrorPolicies(db);
        db.SaveChanges();
    }

    private static void EnsureBillingSeeds(ControlDbContext db, Guid tenantAId, Guid tenantBId)
    {
        var starter = db.BillingPlans.FirstOrDefault(x => x.Code == "starter");
        if (starter is null)
        {
            starter = new BillingPlan
            {
                Id = Guid.NewGuid(),
                Code = "starter",
                Name = "Starter",
                PriceMonthly = 2999,
                PriceYearly = 29990,
                Currency = "INR",
                IsActive = true,
                SortOrder = 1,
                FeaturesJson = JsonSerializer.Serialize(new[] { "1,000 WhatsApp messages/month", "5,000 SMS credits", "2 Team members", "Basic analytics" }),
                LimitsJson = JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    ["whatsappMessages"] = 1000,
                    ["smsCredits"] = 5000,
                    ["contacts"] = 5000,
                    ["teamMembers"] = 2,
                    ["chatbots"] = 1,
                    ["flows"] = 3
                })
            };
            db.BillingPlans.Add(starter);
        }

        var growth = db.BillingPlans.FirstOrDefault(x => x.Code == "growth");
        if (growth is null)
        {
            growth = new BillingPlan
            {
                Id = Guid.NewGuid(),
                Code = "growth",
                Name = "Growth",
                PriceMonthly = 9999,
                PriceYearly = 99990,
                Currency = "INR",
                IsActive = true,
                SortOrder = 2,
                FeaturesJson = JsonSerializer.Serialize(new[] { "10,000 WhatsApp messages/month", "50,000 SMS credits", "10 Team members", "Automation builder" }),
                LimitsJson = JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    ["whatsappMessages"] = 10000,
                    ["smsCredits"] = 50000,
                    ["contacts"] = 50000,
                    ["teamMembers"] = 10,
                    ["chatbots"] = 5,
                    ["flows"] = 50
                })
            };
            db.BillingPlans.Add(growth);
        }

        var enterprise = db.BillingPlans.FirstOrDefault(x => x.Code == "enterprise");
        if (enterprise is null)
        {
            enterprise = new BillingPlan
            {
                Id = Guid.NewGuid(),
                Code = "enterprise",
                Name = "Enterprise",
                PriceMonthly = 49999,
                PriceYearly = 499990,
                Currency = "INR",
                IsActive = true,
                SortOrder = 3,
                FeaturesJson = JsonSerializer.Serialize(new[] { "Unlimited messages", "Unlimited team members", "Dedicated support", "Custom integrations" }),
                LimitsJson = JsonSerializer.Serialize(new Dictionary<string, int>
                {
                    ["whatsappMessages"] = 99999999,
                    ["smsCredits"] = 99999999,
                    ["contacts"] = 99999999,
                    ["teamMembers"] = 9999,
                    ["chatbots"] = 999,
                    ["flows"] = 9999
                })
            };
            db.BillingPlans.Add(enterprise);
        }

        var monthKey = DateTime.UtcNow.ToString("yyyy-MM");
        EnsureTenantBilling(db, tenantAId, growth.Id, monthKey);
        EnsureTenantBilling(db, tenantBId, starter.Id, monthKey);
    }

    private static void EnsureTenantBilling(ControlDbContext db, Guid tenantId, Guid planId, string monthKey)
    {
        if (!db.TenantSubscriptions.Any(x => x.TenantId == tenantId))
        {
            db.TenantSubscriptions.Add(new TenantSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                Status = "active",
                BillingCycle = "monthly",
                StartedAtUtc = DateTime.UtcNow.AddMonths(-2),
                RenewAtUtc = DateTime.UtcNow.AddMonths(1)
            });
        }

        if (!db.TenantUsages.Any(x => x.TenantId == tenantId && x.MonthKey == monthKey))
        {
            db.TenantUsages.Add(new TenantUsage
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                MonthKey = monthKey,
                WhatsappMessagesUsed = 7234,
                SmsCreditsUsed = 32100,
                ContactsUsed = 8456,
                TeamMembersUsed = 6,
                ChatbotsUsed = 2,
                FlowsUsed = 12,
                ApiCallsUsed = 15500
            });
        }

        if (!db.BillingInvoices.Any(x => x.TenantId == tenantId))
        {
            var monthStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            db.BillingInvoices.Add(new BillingInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNo = $"INV-{DateTime.UtcNow:yyyy}-001",
                TenantId = tenantId,
                PeriodStartUtc = monthStartUtc.AddMonths(-1),
                PeriodEndUtc = monthStartUtc.AddDays(-1),
                Subtotal = 9999,
                TaxAmount = 1800,
                Total = 11799,
                Status = "paid",
                PaidAtUtc = DateTime.UtcNow.AddDays(-10),
                PdfUrl = string.Empty
            });
        }
    }

    private static void EnsureWabaErrorPolicies(ControlDbContext db)
    {
        var defaults = new (string Code, string Classification, string Description)[]
        {
            ("2", "retryable", "Service temporarily unavailable"),
            ("4", "retryable", "Rate limit hit"),
            ("80007", "retryable", "Rate limited by platform"),
            ("131016", "retryable", "Transient delivery failure"),
            ("190", "permanent", "Invalid or expired OAuth token"),
            ("200", "permanent", "Permission denied"),
            ("10", "permanent", "Permission denied"),
            ("131026", "permanent", "Message undeliverable / invalid recipient"),
            ("132000", "permanent", "Template parameter invalid"),
            ("132001", "permanent", "Template does not exist or not approved")
        };

        foreach (var row in defaults)
        {
            var existing = db.WabaErrorPolicies.FirstOrDefault(x => x.Code == row.Code);
            if (existing is null)
            {
                db.WabaErrorPolicies.Add(new WabaErrorPolicy
                {
                    Id = Guid.NewGuid(),
                    Code = row.Code,
                    Classification = row.Classification,
                    Description = row.Description,
                    IsActive = true,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                continue;
            }

            existing.Classification = row.Classification;
            existing.Description = row.Description;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public static void InitializeTenant(TenantDbContext db, Guid tenantId)
    {
        // Ensure WABA config table exists for older/shared tenant DBs before seeding.
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
        db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "UX_TenantWabaConfigs_Active_PhoneNumberId" ON "TenantWabaConfigs" ("PhoneNumberId") WHERE "IsActive" = true AND "PhoneNumberId" <> '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "IdempotencyKey" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "NextRetryAtUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "LastError" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "QueueProvider" text NOT NULL DEFAULT 'memory';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "SmsOperator" text NOT NULL DEFAULT 'all';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "EffectiveFromUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Templates" ADD COLUMN IF NOT EXISTS "EffectiveToUtc" timestamp with time zone NULL;""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Messages_Tenant_IdempotencyKey" ON "Messages" ("TenantId","IdempotencyKey");""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "SegmentId" uuid NULL;""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "Email" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "TagsCsv" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "NameEncrypted" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "EmailEncrypted" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "PhoneEncrypted" text NOT NULL DEFAULT '';""");
        db.Database.ExecuteSqlRaw("""ALTER TABLE "Contacts" ADD COLUMN IF NOT EXISTS "PhoneHash" text NOT NULL DEFAULT '';""");
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
                "PayloadJson" text NOT NULL DEFAULT '{}',
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
            );
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_OutboundDeadLetters_Tenant_CreatedAtUtc" ON "OutboundDeadLetters" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""
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
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsOptOuts_Tenant_Phone" ON "SmsOptOuts" ("TenantId","Phone");""");
        db.Database.ExecuteSqlRaw("""
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
            """);
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsBillingLedgers_Tenant_CreatedAtUtc" ON "SmsBillingLedgers" ("TenantId","CreatedAtUtc");""");
        db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsBillingLedgers_MessageId" ON "SmsBillingLedgers" ("MessageId");""");

        if (db.Campaigns.Any(c => c.TenantId == tenantId)) return;

        db.Campaigns.Add(new Campaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Welcome WhatsApp Flow",
            Channel = ChannelType.WhatsApp,
            TemplateText = "Hi {{name}}, welcome to Demo Retail"
        });

        db.Templates.AddRange(
            new Template { Id = Guid.NewGuid(), TenantId = tenantId, Name = "welcome_customer", Channel = ChannelType.WhatsApp, Category = "UTILITY", Language = "en", Body = "Welcome to Textzy" },
            new Template { Id = Guid.NewGuid(), TenantId = tenantId, Name = "payment_reminder", Channel = ChannelType.Sms, Category = "MARKETING", Language = "en", Body = "Payment reminder" }
        );

        var g1 = new ContactGroup { Id = Guid.NewGuid(), TenantId = tenantId, Name = "High Intent Leads" };
        var g2 = new ContactGroup { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Returning Customers" };
        db.ContactGroups.AddRange(g1, g2);

        db.Contacts.AddRange(
            new Contact { Id = Guid.NewGuid(), TenantId = tenantId, GroupId = g1.Id, Name = "Aarav Singh", Phone = "+91 9876543210" },
            new Contact { Id = Guid.NewGuid(), TenantId = tenantId, GroupId = g2.Id, Name = "Ira Mehta", Phone = "+91 9988776655" }
        );

        db.ChatbotConfigs.Add(new ChatbotConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Greeting = "Hi! Welcome to Moneyart. How can we help?",
            Fallback = "Our agent will connect with you shortly.",
            HandoffEnabled = true
        });

        db.SmsFlows.AddRange(
            new SmsFlow { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Welcome Series", Status = "Active", SentCount = 1230 },
            new SmsFlow { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Abandoned Cart", Status = "Active", SentCount = 876 }
        );

        db.SmsInputFields.AddRange(
            new SmsInputField { Id = Guid.NewGuid(), TenantId = tenantId, Name = "first_name", Type = "text" },
            new SmsInputField { Id = Guid.NewGuid(), TenantId = tenantId, Name = "due_amount", Type = "number" }
        );

        db.TenantWabaConfigs.Add(new TenantWabaConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            IsActive = false,
            OnboardingState = "requested",
            OnboardingStartedAtUtc = DateTime.UtcNow,
            BusinessAccountName = "Pending",
            DisplayPhoneNumber = "Pending"
        });

        db.SaveChanges();
    }

    public static TenantDbContext CreateTenantDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new TenantDbContext(options);
    }
}
