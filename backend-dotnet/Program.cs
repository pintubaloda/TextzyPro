using Textzy.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder
    .ConfigureProductionLogging()
    .AddPlatformApiCore()
    .AddFrontendCors();

var controlConnection = builder.ResolveControlConnection(out var allowLocalhostInProduction);
builder.AddPlatformDatabases(controlConnection);

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddHostedServices();

var app = builder.Build();
app.UsePlatformStartupPipeline(controlConnection, allowLocalhostInProduction);
app.Run();
