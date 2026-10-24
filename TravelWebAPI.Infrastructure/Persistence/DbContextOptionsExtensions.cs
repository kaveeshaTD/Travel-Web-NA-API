using Microsoft.EntityFrameworkCore;

namespace TravelWebAPI.Infrastructure.Persistence;

public static class DbContextOptionsExtensions
{
    /// <summary>The one place that decides how the app talks to PostgreSQL (provider and snake_case names).</summary>
    public static DbContextOptionsBuilder<AppDbContext> UseDatabase(this DbContextOptionsBuilder<AppDbContext> options, string connectionString)
    {
        ConfigureProvider(options, connectionString);
        return options;
    }

    public static DbContextOptionsBuilder UseDatabase(this DbContextOptionsBuilder options, string connectionString)
    {
        ConfigureProvider(options, connectionString);
        return options;
    }

    private static void ConfigureProvider(DbContextOptionsBuilder options, string connectionString)
    {
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention();
    }
}
