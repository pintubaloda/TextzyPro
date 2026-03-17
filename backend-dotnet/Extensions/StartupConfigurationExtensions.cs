using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Textzy.Api.Data;
using Textzy.Api.Services;
using Textzy.Api.Utilities;

namespace Textzy.Api.Extensions;

public static class StartupConfigurationExtensions
{
    public static WebApplicationBuilder ConfigureProductionLogging(this WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsProduction())
        {
            return builder;
        }

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddFilter("Default", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Error);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        return builder;
    }

    public static WebApplicationBuilder AddPlatformApiCore(this WebApplicationBuilder builder)
    {
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

        return builder;
    }

    public static WebApplicationBuilder AddFrontendCors(this WebApplicationBuilder builder)
    {
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
                else
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
            });
        });

        return builder;
    }

    public static string ResolveControlConnection(this WebApplicationBuilder builder, out bool allowLocalhostInProduction)
    {
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

        allowLocalhostInProduction = builder.Configuration.GetValue<bool?>("Database:AllowLocalhostInProduction") ?? false;
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

        return controlConnection;
    }

    public static WebApplicationBuilder AddPlatformDatabases(this WebApplicationBuilder builder, string controlConnection)
    {
        builder.Services.AddDbContext<ControlDbContext>(opt => opt.UseNpgsql(controlConnection));
        builder.Services.AddDbContext<TenantDbContext>((sp, opt) =>
        {
            var tenancy = sp.GetRequiredService<TenancyContext>();
            var tenantConnection = string.IsNullOrWhiteSpace(tenancy.DataConnectionString)
                ? controlConnection
                : tenancy.DataConnectionString;
            opt.UseNpgsql(tenantConnection);
        });

        return builder;
    }

}
