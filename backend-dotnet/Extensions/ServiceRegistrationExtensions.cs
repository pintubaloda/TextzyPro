using Textzy.Api.Data;
using Textzy.Api.Middleware;
using Textzy.Api.Providers;
using Textzy.Api.Services;
using Textzy.Api.Utilities;

namespace Textzy.Api.Extensions;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        var redisCacheConn = ConnectionStringHelper.FirstNonEmpty(
            configuration["Redis__ConnectionString"],
            configuration["REDIS_CONNECTION_STRING"],
            configuration["REDIS_URL"]);
        if (!string.IsNullOrWhiteSpace(redisCacheConn))
        {
            services.AddStackExchangeRedisCache(options => { options.Configuration = redisCacheConn; });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddScoped<TenancyContext>();
        services.AddScoped<AuthContext>();
        services.AddScoped<PasswordHasher>();
        services.AddScoped<SecurityIpRuleService>();
        services.AddScoped<SessionService>();
        services.AddScoped<AuthenticatorTotpService>();
        services.AddScoped<RbacService>();
        services.AddScoped<SecretCryptoService>();
        services.AddScoped<SensitiveDataRedactor>();
        services.AddScoped<AuthCookieService>();
        services.AddScoped<ContactPiiService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<EmailService>();
        services.AddScoped<IRazorpayPaymentValidator, RazorpayPaymentValidator>();
        services.AddScoped<InvoiceAttachmentService>();
        services.AddScoped<BillingGuardService>();
        services.AddScoped<IntegrationCatalogBillingService>();
        services.AddScoped<SecurityControlService>();
        services.AddScoped<OpsMetricsService>();
        services.AddScoped<TataSmsMessageProvider>();
        services.AddScoped<EquenceSmsMessageProvider>();
        services.AddScoped<IMessageProvider, SmsProviderRouter>();
        services.AddScoped<MessagingService>();
        services.AddScoped<TriggerEvaluationService>();
        services.AddScoped<WorkflowExecutionEngine>();
        services.AddScoped<TemplateVariableResolverService>();
        services.AddScoped<TemplateSyncOrchestrator>();
        services.Configure<WorkflowRuntimeOptions>(configuration.GetSection("Workflow"));
        services.Configure<WhatsAppOptions>(configuration.GetSection("WhatsApp"));
        services.AddHttpClient();

        // KYC providers (plugin-style routing)
        services.AddScoped<Textzy.Api.Services.Kyc.IKycProvider, Textzy.Api.Services.Kyc.DigiLockerKycProvider>();
        services.AddScoped<Textzy.Api.Services.Kyc.IKycProvider, Textzy.Api.Services.Kyc.GstKycProvider>();
        services.AddScoped<Textzy.Api.Services.Kyc.AadhaarXmlKycService>();
        services.AddScoped<Textzy.Api.Services.Kyc.KycProviderRouter>();
        services.AddSignalR();
        services.AddScoped<WhatsAppCloudService>();
        services.AddScoped<WabaTenantResolver>();
        services.AddScoped<TenantProvisioningService>();
        services.AddSingleton<TenantSchemaGuardService>();
        services.AddSingleton<UserPresenceService>();
        services.AddSingleton<DeliveryDebugBuffer>();
        services.AddSingleton<BroadcastQueueService>();
        services.AddSingleton<OutboundMessageQueueService>();
        services.AddSingleton<WabaWebhookQueueService>();

        return services;
    }

    public static IServiceCollection AddHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<BroadcastWorker>();
        services.AddHostedService<OutboundMessageWorker>();
        services.AddHostedService<WabaWebhookWorker>();
        services.AddHostedService<WabaOnboardingHealthWorker>();
        services.AddHostedService<SecurityMonitoringWorker>();
        services.AddHostedService<TemplateStatusSyncWorker>();
        services.AddHostedService<WorkflowDelayResumeWorker>();
        services.AddHostedService<BillingLifecycleWorker>();
        return services;
    }
}
