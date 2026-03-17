using Microsoft.AspNetCore.HttpOverrides;
using Textzy.Api.Data;
using Textzy.Api.Data.Schema;
using Textzy.Api.Middleware;
using Textzy.Api.Services;
using Textzy.Api.Startup;

namespace Textzy.Api.Extensions;

public static class AppPipelineExtensions
{
    public static WebApplication UsePlatformStartupPipeline(this WebApplication app, string controlConnection, bool allowLocalhostInProduction)
    {
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

        return app;
    }
}
