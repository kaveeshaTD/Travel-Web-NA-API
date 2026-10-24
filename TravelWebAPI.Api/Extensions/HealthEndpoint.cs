using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TravelWebAPI.Infrastructure.Persistence;

namespace TravelWebAPI.Api.Extensions;

public static class HealthEndpoint
{
    public const string Path = "/api/health";

    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");
        return services;
    }

    /// <summary>200 and <c>healthy</c> when the database answers, 503 and <c>unhealthy</c> when it does not. No details leak.</summary>
    public static IEndpointRouteBuilder MapApiHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(Path, new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var status = report.Status == HealthStatus.Healthy ? "healthy" : "unhealthy";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { status }, JsonSerializerOptions.Web));
            },
        }).AllowAnonymous();

        return endpoints;
    }
}
