using System.Net;
using System.Text.Json;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The dashboard figures through the real API and database. The clock is fixed at 25 October 2026, 10:00 UTC.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Clock.Now = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    }

    private async Task<JsonElement> DashboardAsync()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/stats/dashboard");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static JsonElement Month(JsonElement dashboard, string key)
        => dashboard.GetProperty("months").EnumerateArray().Single(m => m.GetProperty("month").GetString() == key);

    private Task SetBooking(string reference, string set) => Execute($"UPDATE bookings SET {set} WHERE reference = '{reference}'");

    private Task AddPayment(string reference, decimal amount, string status, string paidAt) => Execute(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"""
        INSERT INTO payments (id, booking_id, amount, currency, exchange_rate, amount_lkr, method, status, paid_at)
        SELECT gen_random_uuid(), id, {amount}, 'LKR', 1, {amount}, 'cash', '{status}', '{paidAt}' FROM bookings WHERE reference = '{reference}'
        """));

    private Task AddMessage(string status, string createdAt, string name = "Visitor") => Execute($"""
        INSERT INTO contact_messages (id, name, email, message, status, created_at)
        VALUES (gen_random_uuid(), '{name}', 'v@example.com', 'Hello there', '{status}', '{createdAt}')
        """);

    // ---------------------------------------------------------------- access and empty

    [Fact]
    public async Task Guests_and_customers_are_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync("/api/stats/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsync("/api/stats/dashboard", bearer: CustomerToken)).StatusCode);
    }

    [Fact]
    public async Task An_empty_database_gives_zeros_twelve_empty_months_and_empty_lists()
    {
        var dashboard = await DashboardAsync();

        var kpis = dashboard.GetProperty("kpis");
        foreach (var name in new[] { "bookingsThisMonth", "bookingsLastMonth", "pendingBookings", "revenueThisMonthLkr", "revenueLastMonthLkr", "unreadMessages", "totalCustomers", "outstandingBalanceLkr" })
        {
            Assert.Equal(0m, kpis.GetProperty(name).GetDecimal());
        }

        var months = dashboard.GetProperty("months");
        Assert.Equal(12, months.GetArrayLength());
        Assert.Equal("2025-11", months[0].GetProperty("month").GetString());
        Assert.Equal("Nov 25", months[0].GetProperty("label").GetString());
        Assert.Equal("2026-10", months[11].GetProperty("month").GetString());
        Assert.Equal("Oct 26", months[11].GetProperty("label").GetString());
        Assert.All(months.EnumerateArray(), m =>
        {
            Assert.Equal(0, m.GetProperty("bookings").GetInt32());
            Assert.Equal(0m, m.GetProperty("revenueLkr").GetDecimal());
        });
        foreach (var list in new[] { "byService", "recentBookings", "upcomingTrips", "recentMessages" })
        {
            Assert.Equal(0, dashboard.GetProperty(list).GetArrayLength());
        }
    }

    // ---------------------------------------------------------------- the figures

    private async Task SeedFiguresAsync()
    {
        await CreateAsync("services", Service("Private tour"));
        await CreateAsync("services", Service("Airport transfer"));
        await AddBookingAsync("private-tour", reference: "BK-AAA111");
        await AddBookingAsync("private-tour", reference: "BK-BBB222");
        await AddBookingAsync("airport-transfer", reference: "BK-CCC333");
        await AddBookingAsync("airport-transfer", reference: "BK-DDD444");

        // A: confirmed, 100,000, created in October.
        await SetBooking("BK-AAA111", "status = 'confirmed', price_lkr = 100000, created_at = '2026-10-10T08:00:00Z', travel_date = '2026-10-24'");
        // B: pending, created at 20:00 UTC on 30 September = 01:30 on 1 October in Sri Lanka.
        await SetBooking("BK-BBB222", "status = 'pending', created_at = '2026-09-30T20:00:00Z', travel_date = '2026-10-25', travel_time = '08:00'");
        // C: cancelled, created at 18:00 UTC on 30 September = 23:30 on 30 September in Sri Lanka.
        await SetBooking("BK-CCC333", "status = 'cancelled', created_at = '2026-09-30T18:00:00Z', travel_date = '2026-11-02'");
        // D: confirmed, 50,000, created in September, nothing paid on it in October.
        await SetBooking("BK-DDD444", "status = 'confirmed', price_lkr = 50000, created_at = '2026-09-15T08:00:00Z', travel_date = '2026-11-01'");

        await AddPayment("BK-AAA111", 30_000m, "paid", "2026-10-05");
        await AddPayment("BK-AAA111", 5_000m, "refunded", "2026-10-06");
        await AddPayment("BK-AAA111", 7_000m, "pending", "2026-10-07");
        await AddPayment("BK-DDD444", 20_000m, "paid", "2026-09-10");

        await AddMessage("new", "2026-10-20T08:00:00Z");
        await AddMessage("read", "2026-10-21T08:00:00Z");
    }

    [Fact]
    public async Task The_headline_numbers_follow_the_rules()
    {
        await SeedFiguresAsync();

        var kpis = (await DashboardAsync()).GetProperty("kpis");

        Assert.Equal(2, kpis.GetProperty("bookingsThisMonth").GetInt32());
        Assert.Equal(2, kpis.GetProperty("bookingsLastMonth").GetInt32());
        Assert.Equal(1, kpis.GetProperty("pendingBookings").GetInt32());
        Assert.Equal(25_000m, kpis.GetProperty("revenueThisMonthLkr").GetDecimal());
        Assert.Equal(20_000m, kpis.GetProperty("revenueLastMonthLkr").GetDecimal());
        Assert.Equal(1, kpis.GetProperty("unreadMessages").GetInt32());
        Assert.Equal(1, kpis.GetProperty("totalCustomers").GetInt32());
        Assert.Equal(105_000m, kpis.GetProperty("outstandingBalanceLkr").GetDecimal());
    }

    [Fact]
    public async Task A_booking_made_after_midnight_in_sri_lanka_counts_in_the_new_month()
    {
        await SeedFiguresAsync();

        var dashboard = await DashboardAsync();

        Assert.Equal(2, Month(dashboard, "2026-10").GetProperty("bookings").GetInt32());
        Assert.Equal(2, Month(dashboard, "2026-09").GetProperty("bookings").GetInt32());
        Assert.Equal(25_000m, Month(dashboard, "2026-10").GetProperty("revenueLkr").GetDecimal());
        Assert.Equal(20_000m, Month(dashboard, "2026-09").GetProperty("revenueLkr").GetDecimal());
        Assert.Equal(0, Month(dashboard, "2026-08").GetProperty("bookings").GetInt32());
    }

    [Fact]
    public async Task Bookings_and_payments_outside_the_twelve_months_are_left_out()
    {
        await SeedFiguresAsync();
        await AddBookingAsync("private-tour", reference: "BK-OLD111");
        await SetBooking("BK-OLD111", "created_at = '2025-01-10T08:00:00Z'");
        await AddPayment("BK-OLD111", 9_999m, "paid", "2025-01-10");

        var dashboard = await DashboardAsync();

        Assert.Equal(12, dashboard.GetProperty("months").GetArrayLength());
        Assert.Equal(4, dashboard.GetProperty("months").EnumerateArray().Sum(m => m.GetProperty("bookings").GetInt32()));
        Assert.Equal(45_000m, dashboard.GetProperty("months").EnumerateArray().Sum(m => m.GetProperty("revenueLkr").GetDecimal()));
    }

    [Fact]
    public async Task Per_service_counts_ignore_cancelled_bookings_and_unused_services()
    {
        await SeedFiguresAsync();
        await CreateAsync("services", Service("Unused service"));

        var byService = (await DashboardAsync()).GetProperty("byService");

        Assert.Equal(2, byService.GetArrayLength());
        Assert.Equal("private-tour", byService[0].GetProperty("serviceId").GetString());
        Assert.Equal("Private tour", byService[0].GetProperty("title").GetString());
        Assert.Equal(2, byService[0].GetProperty("count").GetInt32());
        Assert.Equal("airport-transfer", byService[1].GetProperty("serviceId").GetString());
        Assert.Equal(1, byService[1].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Recent_bookings_are_newest_first_with_the_item_shape()
    {
        await SeedFiguresAsync();

        var recent = (await DashboardAsync()).GetProperty("recentBookings");

        Assert.Equal(["BK-AAA111", "BK-BBB222", "BK-CCC333", "BK-DDD444"], recent.EnumerateArray().Select(b => b.GetProperty("reference").GetString()));
        var first = recent[0];
        Assert.Equal("Test User", first.GetProperty("name").GetString());
        Assert.Equal("private-tour", first.GetProperty("serviceId").GetString());
        Assert.Equal("confirmed", first.GetProperty("status").GetString());
        Assert.Equal("2026-10-24", first.GetProperty("date").GetString());
        Assert.Equal("09:30", first.GetProperty("time").GetString());
        Assert.Equal(2, first.GetProperty("adults").GetInt32());
        Assert.Equal(0, first.GetProperty("children").GetInt32());
        Assert.Equal(100_000m, first.GetProperty("priceLkr").GetDecimal());
        Assert.Equal(JsonValueKind.Null, recent[1].GetProperty("priceLkr").ValueKind);
        Assert.Equal(JsonValueKind.String, first.GetProperty("createdAt").ValueKind);
        Assert.False(first.TryGetProperty("priceUsd", out _));
    }

    [Fact]
    public async Task Upcoming_trips_are_pending_or_confirmed_from_today_soonest_first()
    {
        await SeedFiguresAsync();

        var upcoming = (await DashboardAsync()).GetProperty("upcomingTrips");

        // A is yesterday, C is cancelled; B is today and D is next week.
        Assert.Equal(["BK-BBB222", "BK-DDD444"], upcoming.EnumerateArray().Select(b => b.GetProperty("reference").GetString()));
    }

    [Fact]
    public async Task Recent_messages_are_newest_first_in_the_inbox_shape()
    {
        await SeedFiguresAsync();

        var messages = (await DashboardAsync()).GetProperty("recentMessages");

        Assert.Equal(["read", "new"], messages.EnumerateArray().Select(m => m.GetProperty("status").GetString()));
        Assert.Equal("Visitor", messages[0].GetProperty("name").GetString());
        Assert.Equal("Hello there", messages[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task The_lists_are_limited_to_six_five_and_five()
    {
        await CreateAsync("services", Service("Private tour"));
        await AddBookingAsync("private-tour", reference: "BK-SEED11");
        await SetBooking("BK-SEED11", "created_at = '2026-01-01T00:00:00Z'");
        await Execute("""
            INSERT INTO bookings (id, reference, user_id, service_id, travel_date, travel_time, adults, name, email, whatsapp, country, created_at)
            SELECT gen_random_uuid(), 'BK-LIM' || lpad(n::text, 3, '0'), (SELECT user_id FROM bookings LIMIT 1), 'private-tour',
                   DATE '2026-11-01' + n, '09:30', 1, 'N', 'n@example.com', '+94', 'LK', TIMESTAMPTZ '2026-10-01 00:00:00+00' + n * INTERVAL '1 hour'
            FROM generate_series(1, 8) AS n
            """);
        for (var index = 1; index <= 7; index++)
        {
            await AddMessage("new", $"2026-10-{index:00}T08:00:00Z", $"Visitor {index}");
        }

        var dashboard = await DashboardAsync();

        var recent = dashboard.GetProperty("recentBookings");
        Assert.Equal(6, recent.GetArrayLength());
        Assert.Equal("BK-LIM008", recent[0].GetProperty("reference").GetString());
        var upcoming = dashboard.GetProperty("upcomingTrips");
        Assert.Equal(5, upcoming.GetArrayLength());
        Assert.Equal("BK-LIM001", upcoming[0].GetProperty("reference").GetString());
        var messages = dashboard.GetProperty("recentMessages");
        Assert.Equal(5, messages.GetArrayLength());
        Assert.Equal("Visitor 7", messages[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Overpaid_confirmed_bookings_never_make_the_outstanding_balance_negative()
    {
        await CreateAsync("services", Service("Private tour"));
        await AddBookingAsync("private-tour", reference: "BK-OVER11");
        await SetBooking("BK-OVER11", "status = 'confirmed', price_lkr = 10000");
        await AddPayment("BK-OVER11", 15_000m, "paid", "2026-10-05");
        await AddBookingAsync("private-tour", reference: "BK-NOPRC1");
        await SetBooking("BK-NOPRC1", "status = 'confirmed'");

        var kpis = (await DashboardAsync()).GetProperty("kpis");

        Assert.Equal(0m, kpis.GetProperty("outstandingBalanceLkr").GetDecimal());
    }
}
