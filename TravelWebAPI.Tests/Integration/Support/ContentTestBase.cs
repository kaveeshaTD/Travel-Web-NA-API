using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration;

namespace TravelWebAPI.Tests.Integration.Support;

/// <summary>
/// The real API on its own empty PostgreSQL database, with admin and customer tokens and small helpers for the
/// content endpoints. Tokens are signed with the test key, so no user rows are needed unless a test says so.
/// </summary>
public abstract class ContentTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected string Connection { get; private set; } = string.Empty;
    protected ApiFactory Factory { get; private set; } = null!;
    protected AuthApi Api { get; private set; } = null!;

    protected Guid AdminId { get; } = Guid.CreateVersion7();

    protected string AdminToken => TestTokens.For(AdminId, UserRole.Admin, "admin@example.com");
    protected string CustomerToken { get; } = TestTokens.For(Guid.CreateVersion7(), UserRole.Customer, "customer@example.com");

    public virtual async Task InitializeAsync()
    {
        Connection = await postgres.CreateDatabaseAsync();
        Factory = ApiFactory.With(Settings(), configure: ConfigureServices);
        Api = new AuthApi(AuthApi.CreateClient(Factory));
    }

    public virtual async Task DisposeAsync() => await Factory.DisposeAsync();

    protected virtual Dictionary<string, string?> Settings() => new() { ["ConnectionStrings:Default"] = Connection };

    /// <summary>Lets a test class swap services of the running API, for example the exchange-rate sources.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected Task<T> Scalar<T>(string sql) => PostgresFixture.ScalarAsync<T>(Connection, sql);

    protected Task<int> Execute(string sql) => PostgresFixture.ExecuteAsync(Connection, sql);

    protected Task<HttpResponseMessage> AsAdmin(HttpMethod method, string path, object? body = null) => Api.SendAsync(method, path, body, bearer: AdminToken);

    /// <summary>Reads the body as JSON. It can be called more than once for the same response.</summary>
    protected static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    protected static async Task<string?> Field(HttpResponseMessage response, string field)
    {
        var fields = (await Json(response)).GetProperty("error").GetProperty("fields");
        return fields.TryGetProperty(field, out var value) ? value.GetString() : null;
    }

    /// <summary>Creates a record as admin, checks it worked and returns its body.</summary>
    protected async Task<JsonElement> CreateAsync(string resource, object body)
    {
        var response = await AsAdmin(HttpMethod.Post, $"/api/{resource}", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"create {resource} failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return await Json(response);
    }

    protected async Task<List<string>> IdsAsync(string resource, bool all = false)
    {
        var response = all ? await AsAdmin(HttpMethod.Get, $"/api/{resource}?all=1") : await Api.GetAsync($"/api/{resource}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToList();
    }

    /// <summary>Compares two JSON values; numbers are compared by value, so 1.5 and 1.500 are the same.</summary>
    protected static bool SameJson(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDecimal() == b.GetDecimal(),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(pair => SameJson(pair.First, pair.Second)),
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var other) && SameJson(p.Value, other)),
            _ => a.ToString() == b.ToString(),
        };
    }

    protected static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(x => x.GetString()!).ToList();

    // ---------------------------------------------------------------- valid bodies

    public static object Destination(string name = "Sigiriya", object? extra = null) => Merge(new
    {
        name,
        region = "Cultural Triangle",
        lat = 7.957,
        lng = 80.76,
        description = "A rock fortress rising out of the jungle.",
        bestTime = "January to April",
        travelTimeFromAirport = "4 h",
        images = new[] { "img:sigiriya" },
        featured = true,
    }, extra);

    public static object Package(string name = "Cultural Triangle", string[]? destinationIds = null, object? extra = null) => Merge(new
    {
        name,
        tagline = "Ancient kingdoms and rock fortresses",
        durationDays = 3,
        highlights = new[] { "Climb Sigiriya", "Visit Dambulla" },
        interests = new[] { "culture", "nature" },
        priceLkr = 120000,
        image = "img:sigiriya",
        gallery = new[] { "img:temple", "/uploads/3f2a.jpg" },
        itinerary = new[]
        {
            new { day = 9, title = "Arrival", description = "Meet at the airport." },
            new { day = 7, title = "Sigiriya", description = "Climb the rock." },
        },
        included = new[] { "Driver and fuel" },
        excluded = new[] { "Entrance tickets" },
        featured = false,
        active = true,
        destinationIds = destinationIds ?? [],
    }, extra);

    public static object Vehicle(string name = "Toyota Prius", object? extra = null) => Merge(new
    {
        type = "car",
        name,
        description = "Comfortable hybrid car for up to three guests.",
        image = "img:vehicle-car",
        seats = 3,
        luggage = 2,
        ac = true,
        pricePerKmLkr = 30,
        pricePerDayLkr = 25000,
    }, extra);

    public static object Service(string title = "Airport transfer", object? extra = null) => Merge(new
    {
        title,
        description = "Pick-up at the airport and a smooth drive to your hotel.",
        details = new[] { "Meet and greet", "Flight tracking" },
        icon = "plane",
        image = "img:airport",
    }, extra);

    public static object Faq(string question = "Do you provide child seats?", object? extra = null) => Merge(new
    {
        question,
        answer = "Yes, child seats are available on request.",
    }, extra);

    public static object Gallery(string caption = "Sunrise at Sigiriya", object? extra = null) => Merge(new
    {
        src = "img:sigiriya",
        alt = "The rock fortress at sunrise",
        caption,
        aspect = 1.5,
    }, extra);

    /// <summary>Overrides properties of a body with the properties of <paramref name="extra"/>.</summary>
    private static Dictionary<string, object?> Merge(object body, object? extra)
    {
        var result = ToDictionary(body);
        if (extra is not null)
        {
            foreach (var (key, value) in ToDictionary(extra))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, object?> ToDictionary(object value)
        => JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());

    // ---------------------------------------------------------------- database rows

    /// <summary>A customer with one booking for the given service and optionally package and vehicle.</summary>
    protected async Task AddBookingAsync(string serviceId, string? packageId = null, string? vehicleId = null, string reference = "BK-ABC123")
    {
        await using var db = PostgresFixture.CreateContext(Connection);

        var user = db.Users.FirstOrDefault(u => u.Email == "booker@example.com");
        if (user is null)
        {
            user = TestData.User("booker@example.com");
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var booking = TestData.Booking(user.Id, serviceId, reference);
        booking.PackageId = packageId;
        booking.VehicleId = vehicleId;
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
    }

    protected static StringContent Raw(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    protected static JsonContent JsonBody(object body) => JsonContent.Create(body);
}
