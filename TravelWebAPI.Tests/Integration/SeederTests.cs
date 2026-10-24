using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class SeederTests(PostgresFixture postgres)
{
    // A cheap hasher keeps the test fast; the seeder only needs the IPasswordHasher contract.
    private static readonly PasswordHasher CheapHasher = new(iterations: 1_000);

    private static DatabaseSeeder CreateSeeder(AppDbContext db)
        => new(db, CheapHasher, NullLogger<DatabaseSeeder>.Instance);

    private static JsonElement ReadSeedFile()
    {
        using var stream = typeof(DatabaseSeeder).Assembly.GetManifestResourceStream("seed.json")!;
        return JsonDocument.Parse(stream).RootElement.Clone();
    }

    [Fact]
    public async Task First_run_loads_content_and_sample_activity_matching_the_seed_file()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var seed = ReadSeedFile();
        await using var db = PostgresFixture.CreateContext(connection);

        await CreateSeeder(db).SeedAsync(new SeedOptions());

        Assert.Equal(seed.GetProperty("services").GetArrayLength(), await db.Services.CountAsync());
        Assert.Equal(seed.GetProperty("vehicles").GetArrayLength(), await db.Vehicles.CountAsync());
        Assert.Equal(seed.GetProperty("destinations").GetArrayLength(), await db.Destinations.CountAsync());
        Assert.Equal(seed.GetProperty("packages").GetArrayLength(), await db.Packages.CountAsync());
        Assert.Equal(seed.GetProperty("gallery").GetArrayLength(), await db.GalleryImages.CountAsync());
        Assert.Equal(seed.GetProperty("reviews").GetArrayLength(), await db.Reviews.CountAsync());
        Assert.Equal(seed.GetProperty("faqs").GetArrayLength(), await db.Faqs.CountAsync());
        Assert.Equal(seed.GetProperty("bookings").GetArrayLength(), await db.Bookings.CountAsync());
        Assert.Equal(seed.GetProperty("payments").GetArrayLength(), await db.Payments.CountAsync());
        Assert.Equal(seed.GetProperty("messages").GetArrayLength(), await db.ContactMessages.CountAsync());
        Assert.Equal(seed.GetProperty("emails").GetArrayLength(), await db.EmailLogs.CountAsync());
        Assert.Equal(1, await db.SiteSettings.CountAsync());
        Assert.Equal(1, await db.TravelTips.CountAsync());
    }

    [Fact]
    public async Task Prices_are_converted_to_lkr_and_vehicles_cost_30_per_km()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);

        await CreateSeeder(db).SeedAsync(new SeedOptions(IncludeSampleActivity: false));

        var cultural = await db.Packages.SingleAsync(p => p.Id == "cultural-triangle");
        Assert.Equal(99_000m, cultural.PriceLkr); // 330 USD x 300

        var vehicles = await db.Vehicles.ToListAsync();
        Assert.All(vehicles, v => Assert.Equal(30m, v.PricePerKmLkr));
        Assert.Equal(19_500m, vehicles.Single(v => v.Id == "car").PricePerDayLkr); // 65 USD x 300
    }

    [Fact]
    public async Task Demo_accounts_exist_with_hashed_passwords_and_every_booking_has_a_customer()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);

        await CreateSeeder(db).SeedAsync(new SeedOptions());

        var admin = await db.Users.SingleAsync(u => u.Email == "admin@ceylonroads.example");
        var emma = await db.Users.SingleAsync(u => u.Email == "emma.wilson@example.com");
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.Equal(UserRole.Customer, emma.Role);
        Assert.StartsWith("v1.", admin.PasswordHash, StringComparison.Ordinal);
        Assert.True(CheapHasher.Verify("Admin@12345", admin.PasswordHash));
        Assert.True(CheapHasher.Verify("Customer123", emma.PasswordHash));
        Assert.DoesNotContain("Admin@12345", admin.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(0, await db.Bookings.CountAsync(b => b.UserId == Guid.Empty));
    }

    [Fact]
    public async Task Seeded_payments_keep_the_original_currency_and_rate()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);

        await CreateSeeder(db).SeedAsync(new SeedOptions());

        var payments = await db.Payments.ToListAsync();
        Assert.All(payments, p =>
        {
            var expectedRate = p.Currency == CurrencyCode.Lkr ? 1m : 300m;
            Assert.Equal(expectedRate, p.ExchangeRate);
            Assert.Equal(Math.Round(p.Amount * expectedRate, 2), p.AmountLkr);
        });
        Assert.Contains(payments, p => p.Currency == CurrencyCode.Usd);
    }

    [Fact]
    public async Task Seeded_emails_use_the_hyphenated_kinds_and_link_to_their_booking()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);

        await CreateSeeder(db).SeedAsync(new SeedOptions());

        Assert.Contains(await db.EmailLogs.Select(e => e.Kind).Distinct().ToListAsync(), k => k == EmailKind.BookingReceived);
        var stored = await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM email_logs WHERE kind = 'booking-received'");
        Assert.True(stored > 0);
        Assert.True(await db.EmailLogs.AnyAsync(e => e.BookingId != null));
    }

    [Fact]
    public async Task Sample_activity_can_be_left_out()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);

        await CreateSeeder(db).SeedAsync(new SeedOptions(IncludeSampleActivity: false));

        Assert.Equal(0, await db.Bookings.CountAsync());
        Assert.Equal(0, await db.Payments.CountAsync());
        Assert.Equal(0, await db.EmailLogs.CountAsync());
        Assert.True(await db.Packages.AnyAsync());
    }

    [Fact]
    public async Task Second_run_duplicates_nothing_and_keeps_admin_edits()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var seeder = CreateSeeder(db);
        await seeder.SeedAsync(new SeedOptions());

        var package = await db.Packages.SingleAsync(p => p.Id == "cultural-triangle");
        package.PriceLkr = 123_456m;
        await db.SaveChangesAsync();
        var counts = await CountRowsAsync(db);

        await using var second = PostgresFixture.CreateContext(connection);
        await CreateSeeder(second).SeedAsync(new SeedOptions());

        Assert.Equal(counts, await CountRowsAsync(second));
        Assert.Equal(123_456m, (await second.Packages.SingleAsync(p => p.Id == "cultural-triangle")).PriceLkr);
    }

    [Fact]
    public async Task Package_routes_keep_their_order_and_items_keep_their_kind()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        await CreateSeeder(db).SeedAsync(new SeedOptions(IncludeSampleActivity: false));

        var package = await db.Packages
            .Include(p => p.Destinations)
            .Include(p => p.Items)
            .Include(p => p.ItineraryDays)
            .SingleAsync(p => p.Id == "cultural-triangle");

        Assert.Equal(["sigiriya", "dambulla", "polonnaruwa"], package.Destinations.OrderBy(d => d.Position).Select(d => d.DestinationId).Where(id => id is "sigiriya" or "dambulla" or "polonnaruwa"));
        Assert.Contains(package.Items, i => i.Kind == PackageItemKind.Highlight);
        Assert.Contains(package.Items, i => i.Kind == PackageItemKind.Included);
        Assert.Equal(3, package.ItineraryDays.Count);
    }

    private static async Task<int[]> CountRowsAsync(AppDbContext db) =>
    [
        await db.Users.CountAsync(),
        await db.Services.CountAsync(),
        await db.Vehicles.CountAsync(),
        await db.Destinations.CountAsync(),
        await db.Packages.CountAsync(),
        await db.PackageItems.CountAsync(),
        await db.GalleryImages.CountAsync(),
        await db.Reviews.CountAsync(),
        await db.Faqs.CountAsync(),
        await db.Bookings.CountAsync(),
        await db.Payments.CountAsync(),
        await db.ContactMessages.CountAsync(),
        await db.EmailLogs.CountAsync(),
    ];

    [Fact]
    public void The_seed_file_is_embedded()
    {
        var names = Assembly.GetAssembly(typeof(DatabaseSeeder))!.GetManifestResourceNames();

        Assert.Contains("seed.json", names);
    }
}
