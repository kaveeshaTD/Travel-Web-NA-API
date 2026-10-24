using Serilog;
using TravelWebAPI.Api.Configuration;
using TravelWebAPI.Api.Extensions;
using TravelWebAPI.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName),
    preserveStaticLogger: true);

builder.Services.AddApi();

var app = builder.Build();

// Settings are final once the app is built (including overrides from user-secrets and environment variables).
ConfigurationValidator.EnsureValid(app.Configuration, app.Environment);

if (await app.InitializeDatabaseAsync(args))
{
    return;
}

app.UseApiPipeline();
app.Run();

/// <summary>Marker so integration tests can start the API with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
