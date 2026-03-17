using Textzy.Api.Services;

namespace Textzy.Api.Startup;

public static class StartupChecks
{
    public static void EnsureQueueProviderExpectations(WebApplication app, OutboundMessageQueueService outboundQueue, WabaWebhookQueueService webhookQueue)
    {
        // Default is resilient: never take the API down because a queue provider is temporarily unavailable.
        // Set QueueProviders__StrictInProduction=true to hard-fail on mismatch.
        var strictInProd = app.Configuration.GetValue<bool?>("QueueProviders:StrictInProduction") ?? false;
        if (!app.Environment.IsProduction()) return;

        var outboundConfigured = (app.Configuration["OutboundQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();
        var webhookConfigured = (app.Configuration["WebhookQueue:Provider"] ?? "memory").Trim().ToLowerInvariant();
        var outboundActive = (outboundQueue.ActiveProvider ?? "memory").Trim().ToLowerInvariant();
        var webhookActive = (webhookQueue.ActiveProvider ?? "memory").Trim().ToLowerInvariant();

        if (outboundConfigured != "memory" && outboundActive == "memory")
        {
            var message = $"Outbound queue configured as '{outboundConfigured}' but active provider resolved to memory.";
            if (strictInProd) throw new InvalidOperationException(message);
            app.Logger.LogError("{Message}", message);
        }

        if (webhookConfigured != "memory" && webhookActive == "memory")
        {
            var message = $"Webhook queue configured as '{webhookConfigured}' but active provider resolved to memory.";
            if (strictInProd) throw new InvalidOperationException(message);
            app.Logger.LogError("{Message}", message);
        }

        if (outboundConfigured == "memory" || webhookConfigured == "memory")
        {
            app.Logger.LogWarning(
                "Production is using memory queue provider(s). outboundConfigured={OutboundConfigured}, webhookConfigured={WebhookConfigured}",
                outboundConfigured,
                webhookConfigured);
        }
    }
}
