using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c> to create migrations. It never runs in the API.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string ConnectionEnvironmentVariable = "TRAVELWEBAPI_DESIGN_CONNECTION";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)
            ?? "Host=localhost;Database=ceylon_roads_design;Username=postgres;Password=design-time-only";

        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseDatabase(connection);

        return new AppDbContext(options.Options, new SystemClock());
    }
}
