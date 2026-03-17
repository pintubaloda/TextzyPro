using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using System.Threading.RateLimiting;
using Textzy.Api.Data;
using Textzy.Api.Data.Schema;
using Textzy.Api.Extensions;
using Textzy.Api.Middleware;
using Textzy.Api.Services;
using Textzy.Api.Startup;
using Textzy.Api.Utilities;

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
            var p when p.StartsWith("/api/auth/register") => "auth-register",
            var p when p.StartsWith("/api/auth/login") => "auth-login",
            var p when p.StartsWith("/api/auth/refresh") => "auth-refresh",
            var p when p.StartsWith("/api/auth/forgot-password/") => "auth-forgot-password",
            var p when p.StartsWith("/api/waba/webhook") => "waba-webhook",
            _ => "default"
        };

        var permitLimit = bucket switch
        {
            "auth-register" => 8,
            "auth-login" => 20,
            "auth-refresh" => 60,
            "auth-forgot-password" => 20,
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

var frontendCors = CorsOriginHelper.BuildFrontendCorsOptions(builder.Configuration, builder.Environment.IsProduction());
if (builder.Environment.IsProduction() && frontendCors.AllowedOrigins.Length == 0)
{
    throw new InvalidOperationException("AllowedOrigins is required in production. Set AllowedOrigins with full origin(s).");
}

builder.Services.AddSingleton(frontendCors);

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        if (frontendCors.AllowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(frontendCors.AllowedOrigins)
                .WithHeaders(frontendCors.AllowedHeaders)
                .WithMethods(frontendCors.AllowedMethods)
                .AllowCredentials()
                .WithExposedHeaders(frontendCors.ExposedHeaders);
        }
        else policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// Connection string resolution:
// Prefer ConnectionStrings:Default so operators can override hosting-panel env vars (common on Windows IIS hosts).
var rawControlConnection = ConnectionStringHelper.FirstNonEmpty(
    builder.Configuration.GetConnectionString("Default"),
    builder.Configuration["DATABASE_URL"],
    builder.Configuration["DATABASE_PUBLIC_URL"],
    builder.Configuration["POSTGRES_URL"]);

string controlConnection;
if (string.IsNullOrWhiteSpace(rawControlConnection))
{
    controlConnection = ConnectionStringHelper.BuildFromPgEnvironment()
        ?? throw new InvalidOperationException("Connection string is missing. Set ConnectionStrings__Default, DATABASE_URL, or PG* variables.");
}
else
{
    try
    {
        controlConnection = ConnectionStringHelper.NormalizeConnectionString(rawControlConnection);
    }
    catch when (builder.Environment.IsProduction())
    {
        controlConnection = ConnectionStringHelper.BuildFromPgEnvironment()
            ?? throw new InvalidOperationException("Invalid Postgres URL in ConnectionStrings__Default/DATABASE_URL and PG* fallback values are missing or invalid.");
    }
}

var allowLocalhostInProduction = builder.Configuration.GetValue<bool?>("Database:AllowLocalhostInProduction") ?? false;
if (builder.Environment.IsProduction() && !allowLocalhostInProduction &&
    (controlConnection.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase) ||
     controlConnection.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
{
    var pgFallback = ConnectionStringHelper.BuildFromPgEnvironment();
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

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddHostedServices();

var app = builder.Build();

if (app.Environment.IsProduction() && allowLocalhostInProduction &&
    (controlConnection.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase) ||
     controlConnection.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
{
    app.Logger.LogWarning("Production is configured to use localhost Postgres. This is allowed by Database:AllowLocalhostInProduction=true.");
}

using (var queueScope = app.Services.CreateScope())
{
    var outboundQueue = queueScope.ServiceProvider.GetRequiredService<OutboundMessageQueueService>();
    var webhookQueue = queueScope.ServiceProvider.GetRequiredService<WabaWebhookQueueService>();
    StartupChecks.EnsureQueueProviderExpectations(app, outboundQueue, webhookQueue);
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

if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

DatabaseInitializer.Initialize(
    app,
    controlConnection,
    ControlSchema.EnsureControlAuthSchema,
    TenantSchema.EnsureTenantCoreSchema,
    TenantSchema.EnsureTenantWabaSchema,
    WorkflowSchema.EnsureTenantWorkflowPhase1PatchOnce);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseResponseCompression();
app.UseMiddleware<FrontendCorsMiddleware>();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseMiddleware<PlatformRequestLoggingMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<AuthMiddleware>();
app.MapControllers();
app.MapHub<Textzy.Api.Services.InboxHub>("/hubs/inbox").RequireCors("frontend");
app.Run();
