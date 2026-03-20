using Textzy.Api.Extensions;
using Textzy.Api.Utilities;

var builder = WebApplication.CreateBuilder(args);
builder
    .ConfigureProductionLogging()
    .AddPlatformApiCore()
    .AddFrontendCors();

var controlConnection = builder.ResolveControlConnection(out var allowLocalhostInProduction);
var sharedTenantConnection = builder.ResolveSharedTenantConnection(controlConnection);
ConnectionStringHelper.ConfigureSharedTenantConnectionString(sharedTenantConnection);
builder.AddPlatformDatabases(controlConnection);

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddHostedServices();

var app = builder.Build();
app.UsePlatformStartupPipeline(controlConnection, allowLocalhostInProduction);
app.Run();
