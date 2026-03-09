using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Npgsql;
using System.Threading.RateLimiting;
using System.Text.RegularExpressions;
using Textzy.Api.Data;
using Textzy.Api.Middleware;
using Textzy.Api.Providers;
using Textzy.Api.Services;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddFilter("Default", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Error);
    builder.Logging.AddFilter("System", LogLevel.Warning);
}

builder.Services.AddControllers(options =>
{
    options.Filters.Add<BodyInputGuardFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "/";
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var bucket = path switch
        {
            var p when p.StartsWith("/api/auth/login") => "auth-login",
            var p when p.StartsWith("/api/auth/refresh") => "auth-refresh",
            var p when p.StartsWith("/api/waba/webhook") => "waba-webhook",
            _ => "default"
        };

        var permitLimit = bucket switch
        {
            "auth-login" => 20,
            "auth-refresh" => 60,
            "waba-webhook" => 1200,
            _ => 600
        };

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{bucket}:{ip}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var allowedOrigins = ParseAllowedOrigins(builder.Configuration).ToArray();
if (builder.Environment.IsProduction() && allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("AllowedOrigins is required in production. Set AllowedOrigins with full origin(s).");
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        if (allowedOrigins.Length > 0) policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("Authorization", "X-Access-Token", "X-CSRF-Token");
        else policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var rawControlConnection = (builder.Environment.IsProduction()
        ? FirstNonEmpty(
            builder.Configuration["DATABASE_URL"],
            builder.Configuration["DATABASE_PUBLIC_URL"],
            builder.Configuration["POSTGRES_URL"],
            builder.Configuration.GetConnectionString("Default"))
        : FirstNonEmpty(
            builder.Configuration.GetConnectionString("Default"),
            builder.Configuration["DATABASE_URL"],
            builder.Configuration["DATABASE_PUBLIC_URL"],
            builder.Configuration["POSTGRES_URL"]));

string controlConnection;
if (string.IsNullOrWhiteSpace(rawControlConnection))
{
    controlConnection = BuildFromPgEnvironment()
        ?? throw new InvalidOperationException("Connection string is missing. Set ConnectionStrings__Default, DATABASE_URL, or PG* variables.");
}
else
{
    try
    {
        controlConnection = NormalizeConnectionString(rawControlConnection);
    }
    catch when (builder.Environment.IsProduction())
    {
        controlConnection = BuildFromPgEnvironment()
            ?? throw new InvalidOperationException("Invalid Postgres URL in ConnectionStrings__Default/DATABASE_URL and PG* fallback values are missing or invalid.");
    }
}

if (builder.Environment.IsProduction() &&
    (controlConnection.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase) ||
     controlConnection.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
{
    var pgFallback = BuildFromPgEnvironment();
    if (!string.IsNullOrWhiteSpace(pgFallback))
    {
        controlConnection = pgFallback;
    }
    else
    {
        throw new InvalidOperationException("Production DB connection is pointing to localhost. Set ConnectionStrings__Default or DATABASE_URL to external Postgres.");
    }
}

builder.Services.AddDbContext<ControlDbContext>(opt => opt.UseNpgsql(controlConnection));
builder.Services.AddDbContext<TenantDbContext>((sp, opt) =>
{
    var tenancy = sp.GetRequiredService<TenancyContext>();
    var tenantConnection = string.IsNullOrWhiteSpace(tenancy.DataConnectionString)
        ? controlConnection
        : tenancy.DataConnectionString;
    opt.UseNpgsql(tenantConnection);
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
var redisCacheConn = FirstNonEmpty(
    builder.Configuration["Redis__ConnectionString"],
    builder.Configuration["REDIS_CONNECTION_STRING"],
    builder.Configuration["REDIS_URL"]);
if (!string.IsNullOrWhiteSpace(redisCacheConn))
{
    builder.Services.AddStackExchangeRedisCache(options => { options.Configuration = redisCacheConn; });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddScoped<TenancyContext>();
builder.Services.AddScoped<AuthContext>();
builder.Services.AddScoped<PasswordHasher>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<AuthenticatorTotpService>();
builder.Services.AddScoped<RbacService>();
builder.Services.AddScoped<SecretCryptoService>();
builder.Services.AddScoped<SensitiveDataRedactor>();
builder.Services.AddScoped<AuthCookieService>();
builder.Services.AddScoped<ContactPiiService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<BillingGuardService>();
builder.Services.AddScoped<SecurityControlService>();
builder.Services.AddScoped<IMessageProvider, TataSmsMessageProvider>();
builder.Services.AddScoped<MessagingService>();
builder.Services.AddScoped<TriggerEvaluationService>();
builder.Services.AddScoped<WorkflowExecutionEngine>();
builder.Services.AddScoped<TemplateVariableResolverService>();
builder.Services.AddScoped<TemplateSyncOrchestrator>();
builder.Services.Configure<WorkflowRuntimeOptions>(builder.Configuration.GetSection("Workflow"));
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient();
builder.Services.AddSignalR();
builder.Services.AddScoped<WhatsAppCloudService>();
builder.Services.AddScoped<WabaTenantResolver>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddSingleton<TenantSchemaGuardService>();
builder.Services.AddSingleton<UserPresenceService>();
builder.Services.AddSingleton<BroadcastQueueService>();
builder.Services.AddSingleton<OutboundMessageQueueService>();
builder.Services.AddSingleton<WabaWebhookQueueService>();
builder.Services.AddHostedService<BroadcastWorker>();
builder.Services.AddHostedService<OutboundMessageWorker>();
builder.Services.AddHostedService<WabaWebhookWorker>();
builder.Services.AddHostedService<WabaOnboardingHealthWorker>();
builder.Services.AddHostedService<SecurityMonitoringWorker>();
builder.Services.AddHostedService<TemplateStatusSyncWorker>();
builder.Services.AddHostedService<WorkflowDelayResumeWorker>();
builder.Services.AddHostedService<BillingLifecycleWorker>();

var app = builder.Build();

using (var queueScope = app.Services.CreateScope())
{
    var outboundQueue = queueScope.ServiceProvider.GetRequiredService<OutboundMessageQueueService>();
    var webhookQueue = queueScope.ServiceProvider.GetRequiredService<WabaWebhookQueueService>();
    EnsureQueueProviderExpectations(app, outboundQueue, webhookQueue);
}

var workflowRuntimeSection = app.Configuration.GetSection("Workflow");
var workflowMode = (workflowRuntimeSection["EngineMode"] ?? "legacy").Trim().ToLowerInvariant();
var workflowShadowOnly = bool.TryParse(workflowRuntimeSection["ShadowLogOnly"], out var shadowOnly) && shadowOnly;
var workflowStateEnabled = bool.TryParse(workflowRuntimeSection["EnableExecutionState"], out var stateEnabled) && stateEnabled;
app.Logger.LogInformation(
    "Workflow runtime mode: mode={Mode}, shadowLogOnly={ShadowOnly}, executionState={ExecutionState}",
    workflowMode,
    workflowShadowOnly,
    workflowStateEnabled);

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

using (var scope = app.Services.CreateScope())
{
    var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var seedEnabled = app.Configuration.GetValue<bool?>("SeedData:Enabled") ?? !app.Environment.IsProduction();

    EnsureControlAuthSchema(controlDb);
    controlDb.Database.EnsureCreated();
    if (seedEnabled)
        SeedData.InitializeControl(controlDb, controlConnection);

    var tenants = controlDb.Tenants.ToList();
    foreach (var tenant in tenants)
    {
        try
        {
            var tenantConn = string.IsNullOrWhiteSpace(tenant.DataConnectionString) ? controlConnection : tenant.DataConnectionString;
            using var tenantDb = SeedData.CreateTenantDbContext(tenantConn);
            tenantDb.Database.EnsureCreated();
            EnsureTenantCoreSchema(tenantDb);
            EnsureTenantWabaSchema(tenantDb);
            EnsureTenantWorkflowPhase1PatchOnce(tenantDb);
            if (seedEnabled)
                SeedData.InitializeTenant(tenantDb, tenant.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Skipping tenant seed for {TenantSlug} due to DB connectivity/config issue. errorType={ErrorType}", tenant.Slug, ex.GetType().Name);
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
app.UseResponseCompression();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseMiddleware<PlatformRequestLoggingMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<AuthMiddleware>();
app.MapControllers();
app.MapHub<Textzy.Api.Services.InboxHub>("/hubs/inbox").RequireCors("frontend");
app.Run();

static void EnsureQueueProviderExpectations(WebApplication app, OutboundMessageQueueService outboundQueue, WabaWebhookQueueService webhookQueue)
{
    var strictInProd = app.Configuration.GetValue<bool?>("QueueProviders:StrictInProduction") ?? true;
    if (!app.Environment.IsProduction() || !strictInProd) return;

    var outboundConfigured = (app.Configuration["OutboundQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();
    var webhookConfigured = (app.Configuration["WebhookQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();
    var outboundActive = (outboundQueue.ActiveProvider ?? "memory").Trim().ToLowerInvariant();
    var webhookActive = (webhookQueue.ActiveProvider ?? "memory").Trim().ToLowerInvariant();

    if (outboundConfigured != "memory" && outboundActive == "memory")
        throw new InvalidOperationException($"Outbound queue configured as '{outboundConfigured}' but active provider resolved to memory.");

    if (webhookConfigured != "memory" && webhookActive == "memory")
        throw new InvalidOperationException($"Webhook queue configured as '{webhookConfigured}' but active provider resolved to memory.");

    if (outboundConfigured == "memory" || webhookConfigured == "memory")
    {
        app.Logger.LogWarning(
            "Production is using memory queue provider(s). outboundConfigured={OutboundConfigured}, webhookConfigured={WebhookConfigured}",
            outboundConfigured,
            webhookConfigured);
    }
}

static void EnsureControlAuthSchema(ControlDbContext db)
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
            "IsActive" boolean NOT NULL DEFAULT true,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantOwnerGroups_OwnerUserId" ON "TenantOwnerGroups" ("OwnerUserId");""");

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
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "TaxRatePercent" numeric(5,2) NOT NULL DEFAULT 18;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "IsTaxExempt" boolean NOT NULL DEFAULT false;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "IsReverseCharge" boolean NOT NULL DEFAULT false;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "PublicApiEnabled" boolean NOT NULL DEFAULT false;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "ApiUsername" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "ApiPasswordEncrypted" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "ApiKeyEncrypted" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TenantCompanyProfiles" ADD COLUMN IF NOT EXISTS "ApiIpWhitelist" text NOT NULL DEFAULT '';""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "Users" (
            "Id" uuid PRIMARY KEY,
            "Email" text NOT NULL,
            "FullName" text NOT NULL,
            "PasswordHash" text NOT NULL,
            "PasswordSalt" text NOT NULL,
            "IsActive" boolean NOT NULL,
            "IsSuperAdmin" boolean NOT NULL,
            "EmailVerifiedAtUtc" timestamp with time zone NULL,
            "AuthenticatorSecretEncrypted" text NOT NULL DEFAULT '',
            "AuthenticatorProvider" text NOT NULL DEFAULT '',
            "AuthenticatorEnabledAtUtc" timestamp with time zone NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "EmailVerifiedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "AuthenticatorSecretEncrypted" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "AuthenticatorProvider" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "AuthenticatorEnabledAtUtc" timestamp with time zone NULL;""");

    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantUsers" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "Role" text NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL
        );
        """);

    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantUsers_UserId" ON "TenantUsers" ("UserId");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantUsers_TenantId" ON "TenantUsers" ("TenantId");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SessionTokens" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "TokenHash" text NOT NULL,
            "ExpiresAtUtc" timestamp with time zone NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "RevokedAtUtc" timestamp with time zone NULL
        );
        """);

    // Backward-compatible guard for older control-plane schemas where SessionTokens columns may be missing.
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "UserId" uuid;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "TenantId" uuid;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "TokenHash" text;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "ExpiresAtUtc" timestamp with time zone;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now();""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "CreatedIpAddress" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "LastSeenIpAddress" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "UserAgent" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "DeviceLabel" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "LastSeenAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "RevokedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "TwoFactorVerifiedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "SessionTokens" ADD COLUMN IF NOT EXISTS "StepUpVerifiedAtUtc" timestamp with time zone NULL;""");

    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SessionTokens_TokenHash" ON "SessionTokens" ("TokenHash");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TwoFactorChallenges" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "SessionTokenId" uuid NULL,
            "Purpose" text NOT NULL,
            "Provider" text NOT NULL,
            "ActionCode" text NOT NULL DEFAULT '',
            "ChallengeTokenHash" text NOT NULL,
            "ExpiresAtUtc" timestamp with time zone NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
            "VerifiedAtUtc" timestamp with time zone NULL,
            "ConsumedAtUtc" timestamp with time zone NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "UserId" uuid;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "TenantId" uuid;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "SessionTokenId" uuid NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "Purpose" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "Provider" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "ActionCode" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "ChallengeTokenHash" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "ExpiresAtUtc" timestamp with time zone NOT NULL DEFAULT now();""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now();""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "VerifiedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "TwoFactorChallenges" ADD COLUMN IF NOT EXISTS "ConsumedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TwoFactorChallenges_ChallengeTokenHash" ON "TwoFactorChallenges" ("ChallengeTokenHash");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TwoFactorChallenges_UserId_Purpose" ON "TwoFactorChallenges" ("UserId","Purpose");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "PlatformSettings" (
            "Id" uuid PRIMARY KEY,
            "Scope" text NOT NULL,
            "Key" text NOT NULL,
            "ValueEncrypted" text NOT NULL,
            "UpdatedByUserId" uuid NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_PlatformSettings_Scope_Key" ON "PlatformSettings" ("Scope","Key");""");
    db.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "TemplateLibraryItems";""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "AuditLogs" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NULL,
            "ActorUserId" uuid NOT NULL,
            "Action" text NOT NULL,
            "Details" text NOT NULL,
            "IpAddress" text NOT NULL DEFAULT '',
            "UserAgent" text NOT NULL DEFAULT '',
            "DeviceLabel" text NOT NULL DEFAULT '',
            "CreatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""ALTER TABLE "AuditLogs" ADD COLUMN IF NOT EXISTS "IpAddress" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "AuditLogs" ADD COLUMN IF NOT EXISTS "UserAgent" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "AuditLogs" ADD COLUMN IF NOT EXISTS "DeviceLabel" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_AuditLogs_CreatedAtUtc" ON "AuditLogs" ("CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "WebhookEvents" (
            "Id" uuid PRIMARY KEY,
            "Provider" text NOT NULL,
            "EventKey" text NOT NULL,
            "TenantId" uuid NULL,
            "PhoneNumberId" text NOT NULL,
            "PayloadJson" text NOT NULL,
            "Status" text NOT NULL,
            "RetryCount" integer NOT NULL DEFAULT 0,
            "MaxRetries" integer NOT NULL DEFAULT 3,
            "LastError" text NOT NULL DEFAULT '',
            "ReceivedAtUtc" timestamp with time zone NOT NULL,
            "ProcessedAtUtc" timestamp with time zone NULL,
            "DeadLetteredAtUtc" timestamp with time zone NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_WebhookEvents_Provider_EventKey" ON "WebhookEvents" ("Provider","EventKey");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_WebhookEvents_Status_ReceivedAtUtc" ON "WebhookEvents" ("Status","ReceivedAtUtc");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "WabaErrorPolicies" (
            "Id" uuid PRIMARY KEY,
            "Code" text NOT NULL,
            "Classification" text NOT NULL,
            "Description" text NOT NULL,
            "IsActive" boolean NOT NULL DEFAULT true,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_WabaErrorPolicies_Code" ON "WabaErrorPolicies" ("Code");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "PlatformRequestLogs" (
            "Id" uuid PRIMARY KEY,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "RequestId" text NOT NULL,
            "Method" text NOT NULL,
            "Path" text NOT NULL,
            "QueryString" text NOT NULL,
            "StatusCode" integer NOT NULL,
            "DurationMs" integer NOT NULL,
            "TenantId" uuid NULL,
            "UserId" uuid NULL,
            "ClientIp" text NOT NULL,
            "UserAgent" text NOT NULL,
            "RequestBody" text NOT NULL,
            "ResponseBody" text NOT NULL,
            "Error" text NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PlatformRequestLogs_CreatedAtUtc" ON "PlatformRequestLogs" ("CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PlatformRequestLogs_Path_CreatedAtUtc" ON "PlatformRequestLogs" ("Path","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PlatformRequestLogs_StatusCode_CreatedAtUtc" ON "PlatformRequestLogs" ("StatusCode","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_PlatformRequestLogs_TenantId_CreatedAtUtc" ON "PlatformRequestLogs" ("TenantId","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SmsGatewayRequestLogs" (
            "Id" uuid PRIMARY KEY,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "Provider" text NOT NULL,
            "TenantId" uuid NULL,
            "Recipient" text NOT NULL,
            "Sender" text NOT NULL,
            "PeId" text NOT NULL,
            "TemplateId" text NOT NULL,
            "HttpMethod" text NOT NULL,
            "RequestUrlMasked" text NOT NULL,
            "RequestPayloadMasked" text NOT NULL,
            "HttpStatusCode" integer NOT NULL,
            "ResponseBody" text NOT NULL,
            "IsSuccess" boolean NOT NULL,
            "Error" text NOT NULL,
            "DurationMs" integer NOT NULL,
            "ProviderMessageId" text NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsGatewayRequestLogs_CreatedAtUtc" ON "SmsGatewayRequestLogs" ("CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsGatewayRequestLogs_Provider_CreatedAtUtc" ON "SmsGatewayRequestLogs" ("Provider","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsGatewayRequestLogs_TenantId_CreatedAtUtc" ON "SmsGatewayRequestLogs" ("TenantId","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SmsGatewayRequestLogs_IsSuccess_CreatedAtUtc" ON "SmsGatewayRequestLogs" ("IsSuccess","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "WebhookReplayGuards" (
            "Id" uuid PRIMARY KEY,
            "Provider" text NOT NULL,
            "ReplayKey" text NOT NULL,
            "FirstSeenAtUtc" timestamp with time zone NOT NULL,
            "ExpiresAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_WebhookReplayGuards_Provider_ReplayKey" ON "WebhookReplayGuards" ("Provider","ReplayKey");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_WebhookReplayGuards_ExpiresAtUtc" ON "WebhookReplayGuards" ("ExpiresAtUtc");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "SecuritySignals" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NULL,
            "SignalType" text NOT NULL,
            "Severity" text NOT NULL,
            "Status" text NOT NULL,
            "CountValue" integer NOT NULL DEFAULT 0,
            "Details" text NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "ResolvedAtUtc" timestamp with time zone NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SecuritySignals_Status_CreatedAtUtc" ON "SecuritySignals" ("Status","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_SecuritySignals_Tenant_CreatedAtUtc" ON "SecuritySignals" ("TenantId","CreatedAtUtc");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantSecurityControls" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "CircuitBreakerEnabled" boolean NOT NULL DEFAULT false,
            "RatePerMinuteOverride" integer NOT NULL DEFAULT 0,
            "Reason" text NOT NULL DEFAULT '',
            "UpdatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedByUserId" uuid NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantSecurityControls_TenantId" ON "TenantSecurityControls" ("TenantId");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantFeatureFlags" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "FeatureKey" text NOT NULL,
            "IsEnabled" boolean NOT NULL DEFAULT false,
            "UpdatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedByUserId" uuid NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantFeatureFlags_Tenant_Feature" ON "TenantFeatureFlags" ("TenantId","FeatureKey");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantFeatureFlags_Feature_Enabled" ON "TenantFeatureFlags" ("FeatureKey","IsEnabled");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "UserPushSubscriptions" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "Endpoint" text NOT NULL,
            "Provider" text NOT NULL DEFAULT 'webpush',
            "P256dh" text NOT NULL DEFAULT '',
            "Auth" text NOT NULL DEFAULT '',
            "UserAgent" text NOT NULL DEFAULT '',
            "IsActive" boolean NOT NULL DEFAULT true,
            "LastSeenAtUtc" timestamp with time zone NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""ALTER TABLE "UserPushSubscriptions" ADD COLUMN IF NOT EXISTS "Provider" text NOT NULL DEFAULT 'webpush';""");
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserPushSubscriptions_Tenant_User_Endpoint" ON "UserPushSubscriptions" ("TenantId","UserId","Endpoint");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_UserPushSubscriptions_Tenant_User_Active" ON "UserPushSubscriptions" ("TenantId","UserId","IsActive");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_UserPushSubscriptions_Tenant_User_Provider_Active" ON "UserPushSubscriptions" ("TenantId","UserId","Provider","IsActive");""");
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "UserNotificationPreferences" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "DesktopEnabled" boolean NOT NULL DEFAULT true,
            "SoundEnabled" boolean NOT NULL DEFAULT true,
            "SoundStyle" text NOT NULL DEFAULT 'whatsapp',
            "SoundVolume" numeric(4,2) NOT NULL DEFAULT 1.0,
            "InAppNewMessages" boolean NOT NULL DEFAULT true,
            "InAppSystemAlerts" boolean NOT NULL DEFAULT true,
            "DndUntilUtc" timestamp with time zone NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserNotificationPreferences_UserId" ON "UserNotificationPreferences" ("UserId");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "EmailOtpVerifications" (
            "Id" uuid PRIMARY KEY,
            "Email" text NOT NULL,
            "Purpose" text NOT NULL DEFAULT 'login',
            "Status" text NOT NULL DEFAULT 'email_sent',
            "OtpHash" text NOT NULL,
            "OtpDisplayEncrypted" text NOT NULL DEFAULT '',
            "VerificationCode" text NOT NULL DEFAULT '',
            "LinkTokenHash" text NOT NULL DEFAULT '',
            "IsVerified" boolean NOT NULL DEFAULT false,
            "AttemptCount" integer NOT NULL DEFAULT 0,
            "MaxAttempts" integer NOT NULL DEFAULT 5,
            "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
            "ExpiresAtUtc" timestamp with time zone NOT NULL,
            "OtpExpiresAtUtc" timestamp with time zone NULL,
            "LinkOpenedAtUtc" timestamp with time zone NULL,
            "OtpIssuedAtUtc" timestamp with time zone NULL,
            "VerifiedAtUtc" timestamp with time zone NULL,
            "ConsumedAtUtc" timestamp with time zone NULL,
            "LastSentAtUtc" timestamp with time zone NOT NULL DEFAULT now()
        );
        """);
    db.Database.ExecuteSqlRaw("""ALTER TABLE "EmailOtpVerifications" ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'email_sent';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "EmailOtpVerifications" ADD COLUMN IF NOT EXISTS "OtpDisplayEncrypted" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "EmailOtpVerifications" ADD COLUMN IF NOT EXISTS "LinkTokenHash" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "EmailOtpVerifications" ADD COLUMN IF NOT EXISTS "OtpExpiresAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "EmailOtpVerifications" ADD COLUMN IF NOT EXISTS "LinkOpenedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "EmailOtpVerifications" ADD COLUMN IF NOT EXISTS "OtpIssuedAtUtc" timestamp with time zone NULL;""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_EmailOtpVerifications_Email_Purpose_CreatedAtUtc" ON "EmailOtpVerifications" ("Email","Purpose","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_EmailOtpVerifications_ExpiresAtUtc" ON "EmailOtpVerifications" ("ExpiresAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_EmailOtpVerifications_LinkTokenHash" ON "EmailOtpVerifications" ("LinkTokenHash");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "UserMobileDevices" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "DeviceName" text NOT NULL DEFAULT '',
            "Platform" text NOT NULL DEFAULT '',
            "AppVersion" text NOT NULL DEFAULT '',
            "InstallIdHash" text NOT NULL DEFAULT '',
            "MetadataJson" text NOT NULL DEFAULT '{{}}',
            "LastSeenAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
            "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
            "RevokedAtUtc" timestamp with time zone NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_UserMobileDevices_User_Tenant_Revoked" ON "UserMobileDevices" ("UserId","TenantId","RevokedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_UserMobileDevices_InstallHash" ON "UserMobileDevices" ("InstallIdHash");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "MobilePairingRequests" (
            "Id" uuid PRIMARY KEY,
            "UserId" uuid NOT NULL,
            "TenantId" uuid NOT NULL,
            "PairingTokenHash" text NOT NULL,
            "PairingPayloadJson" text NOT NULL DEFAULT '{{}}',
            "ExpiresAtUtc" timestamp with time zone NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
            "ConsumedAtUtc" timestamp with time zone NULL,
            "ConsumedDeviceId" uuid NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MobilePairingRequests_TokenHash" ON "MobilePairingRequests" ("PairingTokenHash");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MobilePairingRequests_Expiry" ON "MobilePairingRequests" ("ExpiresAtUtc");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "MobileTelemetryEvents" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "DeviceId" uuid NULL,
            "EventType" text NOT NULL DEFAULT '',
            "DataJson" text NOT NULL DEFAULT '{{}}',
            "EventAtUtc" timestamp with time zone NOT NULL DEFAULT now(),
            "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MobileTelemetryEvents_Tenant_EventAt" ON "MobileTelemetryEvents" ("TenantId","EventAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_MobileTelemetryEvents_User_EventAt" ON "MobileTelemetryEvents" ("UserId","EventAtUtc");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TeamInvitations" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "Email" text NOT NULL,
            "Name" text NOT NULL,
            "Role" text NOT NULL,
            "TokenHash" text NOT NULL,
            "Status" text NOT NULL,
            "SendCount" integer NOT NULL,
            "SentAtUtc" timestamp with time zone NOT NULL,
            "ExpiresAtUtc" timestamp with time zone NOT NULL,
            "AcceptedAtUtc" timestamp with time zone NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "CreatedByUserId" uuid NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TeamInvitations_Tenant_Email_Status" ON "TeamInvitations" ("TenantId","Email","Status");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TeamInvitations_TokenHash" ON "TeamInvitations" ("TokenHash");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantUserPermissionOverrides" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "UserId" uuid NOT NULL,
            "Permission" text NOT NULL,
            "IsAllowed" boolean NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantUserPermissionOverrides_Tenant_User_Permission" ON "TenantUserPermissionOverrides" ("TenantId","UserId","Permission");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "BillingPlans" (
            "Id" uuid PRIMARY KEY,
            "Code" text NOT NULL,
            "Name" text NOT NULL,
            "PricingModel" text NOT NULL DEFAULT 'subscription',
            "PriceMonthly" numeric(18,2) NOT NULL,
            "PriceYearly" numeric(18,2) NOT NULL,
            "TaxMode" text NOT NULL DEFAULT 'exclusive',
            "UsageUnitName" text NOT NULL DEFAULT '',
            "IncludedQuantity" integer NOT NULL DEFAULT 0,
            "Currency" text NOT NULL,
            "IsActive" boolean NOT NULL,
            "SortOrder" integer NOT NULL,
            "FeaturesJson" text NOT NULL,
            "LimitsJson" text NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillingPlans_Code" ON "BillingPlans" ("Code");""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingPlans" ADD COLUMN IF NOT EXISTS "PricingModel" text NOT NULL DEFAULT 'subscription';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingPlans" ADD COLUMN IF NOT EXISTS "TaxMode" text NOT NULL DEFAULT 'exclusive';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingPlans" ADD COLUMN IF NOT EXISTS "UsageUnitName" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingPlans" ADD COLUMN IF NOT EXISTS "IncludedQuantity" integer NOT NULL DEFAULT 0;""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantSubscriptions" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "PlanId" uuid NOT NULL,
            "Status" text NOT NULL,
            "BillingCycle" text NOT NULL,
            "StartedAtUtc" timestamp with time zone NOT NULL,
            "RenewAtUtc" timestamp with time zone NOT NULL,
            "CancelledAtUtc" timestamp with time zone NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_TenantSubscriptions_TenantId" ON "TenantSubscriptions" ("TenantId");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantUsages" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "MonthKey" text NOT NULL,
            "WhatsappMessagesUsed" integer NOT NULL,
            "SmsCreditsUsed" integer NOT NULL,
            "ContactsUsed" integer NOT NULL,
            "TeamMembersUsed" integer NOT NULL,
            "ChatbotsUsed" integer NOT NULL,
            "FlowsUsed" integer NOT NULL,
            "ApiCallsUsed" integer NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantUsages_Tenant_Month" ON "TenantUsages" ("TenantId","MonthKey");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "TenantUsageCreditBalances" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "MetricKey" text NOT NULL,
            "UnitsRemaining" integer NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantUsageCreditBalances_Tenant_Metric" ON "TenantUsageCreditBalances" ("TenantId","MetricKey");""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "BillingInvoices" (
            "Id" uuid PRIMARY KEY,
            "InvoiceNo" text NOT NULL,
            "TenantId" uuid NOT NULL,
            "InvoiceKind" text NOT NULL DEFAULT 'tax_invoice',
            "BillingCycle" text NOT NULL DEFAULT 'monthly',
            "TaxMode" text NOT NULL DEFAULT 'exclusive',
            "ReferenceNo" text NOT NULL DEFAULT '',
            "PeriodStartUtc" timestamp with time zone NOT NULL,
            "PeriodEndUtc" timestamp with time zone NOT NULL,
            "Subtotal" numeric(18,2) NOT NULL,
            "TaxAmount" numeric(18,2) NOT NULL,
            "Total" numeric(18,2) NOT NULL,
            "Status" text NOT NULL,
            "PaidAtUtc" timestamp with time zone NULL,
            "PdfUrl" text NOT NULL,
            "CreatedAtUtc" timestamp with time zone NOT NULL
        );
        """);
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_BillingInvoices_TenantId" ON "BillingInvoices" ("TenantId");""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "InvoiceKind" text NOT NULL DEFAULT 'tax_invoice';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "BillingCycle" text NOT NULL DEFAULT 'monthly';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "TaxMode" text NOT NULL DEFAULT 'exclusive';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "ReferenceNo" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "IntegrityAlgo" text NOT NULL DEFAULT 'SHA256';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "IntegrityHash" text NOT NULL DEFAULT '';""");
    db.Database.ExecuteSqlRaw("""ALTER TABLE "BillingInvoices" ADD COLUMN IF NOT EXISTS "IssuedAtUtc" timestamp with time zone NOT NULL DEFAULT now();""");

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "BillingPaymentAttempts" (
            "Id" uuid PRIMARY KEY,
            "TenantId" uuid NOT NULL,
            "PlanId" uuid NOT NULL,
            "BillingCycle" text NOT NULL,
            "Provider" text NOT NULL,
            "OrderId" text NOT NULL,
            "PaymentId" text NOT NULL,
            "Signature" text NOT NULL,
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
}

static IEnumerable<string> ParseAllowedOrigins(IConfiguration config)
{
    var raw = config["AllowedOrigins"] ?? string.Empty;
    var parsed = raw
        .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(o => (o ?? string.Empty).Trim().TrimEnd('/'))
        .Where(o => !string.IsNullOrWhiteSpace(o))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    // Production safety fallback to known Textzy frontend origins.
    if (parsed.Count == 0)
    {
        parsed.Add("https://textzy-frontend-production.up.railway.app");
        parsed.Add("https://textzy-backend-production.up.railway.app");
        parsed.Add("http://localhost:3000");
        parsed.Add("http://localhost:5173");
    }

    return parsed;
}

static string NormalizeConnectionString(string raw)
{
    var value = (raw ?? string.Empty).Trim().Trim('"', '\'');
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException("Database connection string is empty.");

    // Defensive cleanup in case env was pasted as a labeled line.
    if (value.StartsWith("External Database URL", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Internal Database URL", StringComparison.OrdinalIgnoreCase))
    {
        var idx = value.IndexOf("://", StringComparison.Ordinal);
        if (idx > 0)
        {
            var schemeStart = value.LastIndexOf(' ', idx);
            value = value[(schemeStart >= 0 ? schemeStart + 1 : 0)..].Trim();
        }
    }

    // Remove hidden whitespace/newlines that can appear in env UI paste for URLs.
    var noWhitespace = Regex.Replace(value, @"\s+", string.Empty).Trim();

    // If the value has extra text, extract the first postgres URL segment.
    var urlMatch = Regex.Match(noWhitespace, @"(postgres(?:ql)?://\S+)", RegexOptions.IgnoreCase);
    if (urlMatch.Success)
    {
        value = urlMatch.Groups[1].Value.Trim().Trim('"', '\'');
    }
    else
    {
        value = value.Trim();
    }

    if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        if (TryBuildFromUrl(value, out var urlConn))
        {
            return urlConn;
        }
        try
        {
            return new NpgsqlConnectionStringBuilder(value).ConnectionString;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid Postgres URL in ConnectionStrings__Default/DATABASE_URL.", ex);
        }
    }

    try
    {
        return new NpgsqlConnectionStringBuilder(value).ConnectionString;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException("Invalid key/value Postgres connection string in ConnectionStrings__Default/DATABASE_URL.", ex);
    }
}

static string? BuildFromPgEnvironment()
{
    var host = FirstNonEmpty(
        Environment.GetEnvironmentVariable("PGHOST"),
        Environment.GetEnvironmentVariable("POSTGRES_HOST"),
        Environment.GetEnvironmentVariable("DB_HOST"));
    var port = FirstNonEmpty(
        Environment.GetEnvironmentVariable("PGPORT"),
        Environment.GetEnvironmentVariable("POSTGRES_PORT"),
        Environment.GetEnvironmentVariable("DB_PORT"));
    var user = FirstNonEmpty(
        Environment.GetEnvironmentVariable("PGUSER"),
        Environment.GetEnvironmentVariable("POSTGRES_USER"),
        Environment.GetEnvironmentVariable("DB_USER"));
    var pass = FirstNonEmpty(
        Environment.GetEnvironmentVariable("PGPASSWORD"),
        Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"),
        Environment.GetEnvironmentVariable("DB_PASSWORD"));
    var db = FirstNonEmpty(
        Environment.GetEnvironmentVariable("PGDATABASE"),
        Environment.GetEnvironmentVariable("POSTGRES_DB"),
        Environment.GetEnvironmentVariable("DB_NAME"));
    var sslMode = FirstNonEmpty(
        Environment.GetEnvironmentVariable("PGSSLMODE"),
        Environment.GetEnvironmentVariable("POSTGRES_SSLMODE"),
        Environment.GetEnvironmentVariable("DB_SSLMODE"));

    if (string.IsNullOrWhiteSpace(host) ||
        string.IsNullOrWhiteSpace(port) ||
        string.IsNullOrWhiteSpace(user) ||
        string.IsNullOrWhiteSpace(pass) ||
        string.IsNullOrWhiteSpace(db))
    {
        return null;
    }

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = host.Trim(),
        Port = int.TryParse(port, out var p) ? p : 5432,
        Username = user.Trim(),
        Password = pass,
        Database = db.Trim()
    };

    var ssl = (sslMode ?? string.Empty).Trim().ToLowerInvariant();
    if (ssl is "require" or "prefer" or "verify-ca" or "verify-full")
    {
        builder.SslMode = ssl switch
        {
            "require" => SslMode.Require,
            "prefer" => SslMode.Prefer,
            "verify-ca" => SslMode.VerifyCA,
            "verify-full" => SslMode.VerifyFull,
            _ => SslMode.Prefer
        };
    }
    else
    {
        builder.SslMode = SslMode.Require;
    }

    return builder.ConnectionString;
}

static bool TryBuildFromUrl(string url, out string connectionString)
{
    connectionString = string.Empty;
    var cleaned = (url ?? string.Empty).Trim().Trim('"', '\'');
    if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (!uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase) &&
        !uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var userInfo = uri.UserInfo.Split(':', 2);
    var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
    var database = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
    var sslMode = query.TryGetValue("sslmode", out var ssl) ? ssl.ToString() : "require";

    var csb = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = username,
        Password = password,
        Database = database
    };

    csb.SslMode = sslMode.ToLowerInvariant() switch
    {
        "disable" => SslMode.Disable,
        "allow" => SslMode.Allow,
        "prefer" => SslMode.Prefer,
        "verify-ca" => SslMode.VerifyCA,
        "verify-full" => SslMode.VerifyFull,
        _ => SslMode.Require
    };
    connectionString = csb.ConnectionString;
    return true;
}

static string? FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static void EnsureTenantWabaSchema(TenantDbContext db)
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

static void EnsureTenantCoreSchema(TenantDbContext db)
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
    db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FlowRuntimeEvents" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "FlowId" uuid NULL, "MetaFlowId" text NOT NULL DEFAULT '', "ConversationExternalId" text NOT NULL DEFAULT '', "CustomerPhone" text NOT NULL DEFAULT '', "EventType" text NOT NULL DEFAULT '', "EventSource" text NOT NULL DEFAULT '', "Success" boolean NOT NULL DEFAULT true, "StatusCode" integer NOT NULL DEFAULT 0, "DurationMs" integer NOT NULL DEFAULT 0, "ScreenId" text NOT NULL DEFAULT '', "ActionName" text NOT NULL DEFAULT '', "PayloadJson" text NOT NULL DEFAULT '{}', "ErrorDetail" text NOT NULL DEFAULT '', "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FlowRuntimeEvents_Tenant_CreatedAt" ON "FlowRuntimeEvents" ("TenantId","CreatedAtUtc");""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FlowRuntimeEvents_MetaFlowId" ON "FlowRuntimeEvents" ("MetaFlowId");""");

    db.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "FaqKnowledgeItems" ("Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "Question" text NOT NULL DEFAULT '', "Answer" text NOT NULL DEFAULT '', "Category" text NOT NULL DEFAULT '', "IsActive" boolean NOT NULL DEFAULT true, "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT now(), "UpdatedAtUtc" timestamp with time zone NOT NULL DEFAULT now());""");
    db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_FaqKnowledgeItems_Tenant_Active" ON "FaqKnowledgeItems" ("TenantId","IsActive");""");
}

static void EnsureTenantWorkflowPhase1PatchOnce(TenantDbContext db)
{
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS "__SchemaPatches" (
            "PatchKey" text PRIMARY KEY,
            "AppliedAtUtc" timestamp with time zone NOT NULL DEFAULT now()
        );
        """);

    db.Database.ExecuteSqlRaw("""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM "__SchemaPatches" WHERE "PatchKey" = '20260301_workflow_phase1_additive') THEN
                CREATE TABLE IF NOT EXISTS "WorkflowExecutionStates" (
                    "Id" uuid PRIMARY KEY,
                    "TenantId" uuid NOT NULL,
                    "FlowId" uuid NOT NULL,
                    "ConversationId" uuid NOT NULL,
                    "CurrentNodeId" text NOT NULL DEFAULT '',
                    "ExecutionData" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
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
                    "InputData" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
                    "OutputData" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
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
                    "MessageContent" jsonb NOT NULL DEFAULT '{{}}'::jsonb,
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

                INSERT INTO "__SchemaPatches" ("PatchKey", "AppliedAtUtc")
                VALUES ('20260301_workflow_phase1_additive', now())
                ON CONFLICT ("PatchKey") DO NOTHING;
            END IF;
        END
        $$;
        """);
}
