using System.Reflection;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Tests.Unit;

public class ArchitectureTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Serilog",
    ];

    [Theory]
    [InlineData("TravelWebAPI.Domain")]
    [InlineData("TravelWebAPI.Application")]
    public void Inner_layers_do_not_depend_on_frameworks_or_outer_layers(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var references = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.DoesNotContain(references, r => ForbiddenPrefixes.Any(p => r.StartsWith(p, StringComparison.Ordinal)));
        Assert.DoesNotContain(references, r => r is "TravelWebAPI.Infrastructure" or "TravelWebAPI.Api");
    }

    [Fact]
    public void Domain_depends_on_no_other_project()
    {
        var references = typeof(User).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, r => r.StartsWith("TravelWebAPI.", StringComparison.Ordinal));
    }
}
