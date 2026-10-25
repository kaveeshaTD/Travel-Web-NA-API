using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The seeded content is served by the content endpoints, and seeding again leaves an admin's changes alone.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentSeedTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private async Task SeedAsync()
    {
        await using var db = PostgresFixture.CreateContext(Connection);
        await new DatabaseSeeder(db, new PasswordHasher(iterations: 1_000), NullLogger<DatabaseSeeder>.Instance).SeedAsync(new SeedOptions());
    }

    [Fact]
    public async Task Every_seeded_collection_is_served_with_lkr_prices()
    {
        await SeedAsync();

        foreach (var resource in new[] { "packages", "destinations", "vehicles", "services", "faqs", "gallery" })
        {
            var response = await Api.GetAsync($"/api/{resource}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty((await Json(response)).EnumerateArray());
        }

        var package = (await Json(await Api.GetAsync("/api/packages")))[0];
        Assert.True(package.GetProperty("priceLkr").GetDecimal() > 0);
        Assert.NotEmpty(package.GetProperty("route").EnumerateArray());
        Assert.Equal(package.GetProperty("route").GetArrayLength(), package.GetProperty("destinationIds").GetArrayLength());
        Assert.Equal(30m, (await Json(await Api.GetAsync("/api/vehicles")))[0].GetProperty("pricePerKmLkr").GetDecimal());
    }

    [Fact]
    public async Task A_seeded_package_can_be_read_by_id_and_the_seeded_ids_are_valid_slugs()
    {
        await SeedAsync();

        var ids = await IdsAsync("packages");
        var one = await Api.GetAsync($"/api/packages/{ids[0]}");

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.All(ids, id => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id));
    }

    [Fact]
    public async Task Seeding_again_keeps_what_an_admin_changed_and_adds_nothing()
    {
        await SeedAsync();
        var vehicleId = (await IdsAsync("vehicles"))[0];
        await AsAdmin(HttpMethod.Put, $"/api/vehicles/{vehicleId}", new { name = "Renamed by the admin", pricePerKmLkr = 45 });
        await AsAdmin(HttpMethod.Post, "/api/faqs/reorder", new { ids = new[] { (await IdsAsync("faqs")).Last() } });
        var before = new[] { await IdsAsync("packages"), await IdsAsync("vehicles"), await IdsAsync("faqs"), await IdsAsync("gallery") };

        await SeedAsync();

        var after = new[] { await IdsAsync("packages"), await IdsAsync("vehicles"), await IdsAsync("faqs"), await IdsAsync("gallery") };
        Assert.Equal(before, after);
        var vehicle = await Json(await Api.GetAsync($"/api/vehicles/{vehicleId}"));
        Assert.Equal("Renamed by the admin", vehicle.GetProperty("name").GetString());
        Assert.Equal(45m, vehicle.GetProperty("pricePerKmLkr").GetDecimal());
    }
}
