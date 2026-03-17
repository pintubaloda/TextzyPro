using Microsoft.EntityFrameworkCore;
using Textzy.Api.Services;

namespace Textzy.Api.Data;

public static class DatabaseInitializer
{
    public static void Initialize(
        WebApplication app,
        string controlConnection,
        Action<ControlDbContext> ensureControlAuthSchema,
        Action<TenantDbContext> ensureTenantCoreSchema,
        Action<TenantDbContext> ensureTenantWabaSchema,
        Action<TenantDbContext> ensureTenantWorkflowPhase1PatchOnce)
    {
        using var scope = app.Services.CreateScope();
        var controlDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var seedEnabled = app.Configuration.GetValue<bool?>("SeedData:Enabled") ?? !app.Environment.IsProduction();
        var warmTenantSchemas = app.Configuration.GetValue<bool?>("Startup:WarmTenantSchemas") ?? !app.Environment.IsProduction();

        var startupRetrySeconds = app.Configuration.GetValue<int?>("Database:StartupConnectRetrySeconds") ?? 30;
        RetryUntilDbReady(
            logger,
            startupRetrySeconds,
            () =>
            {
                ensureControlAuthSchema(controlDb);
                controlDb.Database.EnsureCreated();
            });

        if (seedEnabled)
            SeedData.InitializeControl(controlDb, controlConnection);

        if (warmTenantSchemas)
        {
            var tenants = controlDb.Tenants.ToList();
            foreach (var tenant in tenants)
            {
                try
                {
                    var tenantConn = string.IsNullOrWhiteSpace(tenant.DataConnectionString) ? controlConnection : tenant.DataConnectionString;
                    using var tenantDb = SeedData.CreateTenantDbContext(tenantConn);
                    tenantDb.Database.EnsureCreated();
                    ensureTenantCoreSchema(tenantDb);
                    ensureTenantWabaSchema(tenantDb);
                    ensureTenantWorkflowPhase1PatchOnce(tenantDb);
                    if (seedEnabled)
                        SeedData.InitializeTenant(tenantDb, tenant.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Skipping tenant seed for {TenantSlug} due to DB connectivity/config issue. errorType={ErrorType}", tenant.Slug, ex.GetType().Name);
                }
            }
        }
        else
        {
            logger.LogInformation("Skipping startup tenant schema warmup. Set Startup:WarmTenantSchemas=true to re-enable eager tenant initialization.");
        }
    }

    private static void RetryUntilDbReady(ILogger logger, int maxSeconds, Action action)
    {
        var maxAttempts = Math.Max(1, maxSeconds / 3);
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                action();
                if (attempt > 1)
                    logger.LogInformation("Database connectivity restored after retries. attempts={Attempts}", attempt);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                // Typical transient: DB service not yet up during reboot; avoid a tight crash loop.
                logger.LogWarning(
                    "Database not ready yet; will retry. attempt={Attempt} errorType={ErrorType} message={Message}",
                    attempt,
                    ex.GetType().Name,
                    ex.Message);
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }

        throw new InvalidOperationException(
            $"Database was not reachable within {maxSeconds} seconds; aborting startup.",
            last);
    }
}
