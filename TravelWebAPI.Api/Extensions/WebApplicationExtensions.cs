using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using TravelWebAPI.Api.Configuration;
using TravelWebAPI.Api.Errors;
using TravelWebAPI.Api.Middleware;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Infrastructure.Seed;

namespace TravelWebAPI.Api.Extensions;

public static class WebApplicationExtensions
{
    public const string MigrateSwitch = "--migrate";
    public const string SeedSwitch = "--seed";

    /// <summary>The middleware order matters: ids and logging wrap everything, errors are shaped next, then routing, limits, CORS and auth.</summary>
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();

        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            // The handler logs what it needs to, so the framework's own duplicate error log is switched off.
            SuppressDiagnosticsCallback = _ => true,
            ExceptionHandler = context => ErrorResponse.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ErrorResponse.DefaultMessage(StatusCodes.Status500InternalServerError)),
        });

        // 401, 403, 404, 405 and friends that carry no body get the uniform error body too.
        app.UseStatusCodePages(context => ErrorResponse.WriteAsync(
            context.HttpContext,
            context.HttpContext.Response.StatusCode,
            ErrorResponse.DefaultMessage(context.HttpContext.Response.StatusCode)));

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();
        app.UseMiddleware<RequestSizeLimitMiddleware>();
        app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
        app.UseAuthorization();

        app.MapControllers();
        app.MapApiHealth();

        // The API description and its interactive pages (Scalar and Swagger UI read the same document) are for developers only.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Ceylon Roads API v1");
                options.RoutePrefix = "swagger";
            });
        }

        return app;
    }

    /// <summary>
    /// Applies migrations (always in Development, or with <c>--migrate</c>) and seeds when asked
    /// (<c>Seed:Enabled</c> or <c>--seed</c>). Returns true when a one-off switch was used and the app should exit.
    /// </summary>
    public static async Task<bool> InitializeDatabaseAsync(this WebApplication app, string[] args)
    {
        var migrateSwitch = args.Contains(MigrateSwitch);
        var seedSwitch = args.Contains(SeedSwitch);
        var seeding = app.Configuration.GetSection(SeedingOptions.Section).Get<SeedingOptions>() ?? new SeedingOptions();

        var migrate = app.Environment.IsDevelopment() || migrateSwitch || seedSwitch;
        var seed = seedSwitch || (seeding.Enabled && !migrateSwitch);
        if (!migrate && !seed)
        {
            return false;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        if (migrate)
        {
            logger.LogInformation("Applying database migrations");
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        if (seed)
        {
            if (app.Environment.IsProduction())
            {
                logger.LogWarning("Seeding demo data into a Production database because it was requested explicitly");
            }

            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>()
                .SeedAsync(new SeedOptions(seeding.IncludeSampleActivity));
        }

        return migrateSwitch || seedSwitch;
    }
}
