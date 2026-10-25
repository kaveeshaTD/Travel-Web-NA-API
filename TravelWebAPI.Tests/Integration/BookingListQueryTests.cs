using System.Globalization;
using System.Net;
using System.Text.Json;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>
/// The booking lists (admin and "mine") on the common list contract, with 45 bookings of two customers: groups of three share
/// a creation time, every fifth has no price, and the status, service and travel date vary.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BookingListQueryTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private string emmaToken = string.Empty;
    private string liamToken = string.Empty;
    private Guid emmaId;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateAsync("services", Service("Private tour"));
        await CreateAsync("services", Service("Airport transfer"));

        await using var db = PostgresFixture.CreateContext(Connection);
        var emma = TestData.User("emma@example.com");
        emma.Name = "Emma Wilson";
        var liam = TestData.User("liam@example.com");
        liam.Name = "Liam O'Connor";
        db.Users.AddRange(emma, liam);
        await db.SaveChangesAsync();
        emmaId = emma.Id;
        emmaToken = TestTokens.For(emma.Id, UserRole.Customer, "emma@example.com");
        liamToken = TestTokens.For(liam.Id, UserRole.Customer, "liam@example.com");

        var statuses = new[] { BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.Pending };
        for (var index = 1; index <= 45; index++)
        {
            var booking = TestData.Booking(index % 3 == 0 ? liam.Id : emma.Id, index % 2 == 0 ? "airport-transfer" : "private-tour", $"BK-T{index:000}");
            booking.Name = index switch { 7 => "100% Sure", 8 => "Under_score", _ => $"Guest {index:00}" };
            booking.Email = $"guest{index:00}@example.com";
            booking.Whatsapp = $"+94 77 000 {index:0000}";
            booking.TravelDate = new DateOnly(2030, 1, 1).AddDays(index);
            booking.Status = statuses[index % 5];
            booking.PriceLkr = index % 5 == 0 ? null : index * 1000m;
            booking.CreatedAt = Start.AddHours((index - 1) / 3);
            booking.UpdatedAt = booking.CreatedAt;
            booking.AdminNotes = "internal only";
            db.Bookings.Add(booking);
        }

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> ListAsync(string query = "", string? bearer = null)
    {
        var response = bearer is null
            ? await AsAdmin(HttpMethod.Get, "/api/bookings" + query)
            : await Api.GetAsync("/api/bookings/mine" + query, bearer);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static List<string> References(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("reference").GetString()!).ToList();

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task The_admin_list_refuses_guests_and_customers_and_mine_refuses_guests()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync("/api/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsync("/api/bookings", emmaToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync("/api/bookings/mine")).StatusCode);
    }

    // ---------------------------------------------------------------- paging

    [Fact]
    public async Task The_first_page_has_twenty_newest_first_with_the_totals_and_the_admin_fields()
    {
        var page = await ListAsync();

        Assert.Equal((1, 20, 45, 3), (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32(), page.GetProperty("totalCount").GetInt32(), page.GetProperty("totalPages").GetInt32()));
        Assert.Equal(20, page.GetProperty("items").GetArrayLength());
        Assert.False(page.TryGetProperty("facets", out _));
        var times = page.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.All(page.GetProperty("items").EnumerateArray(), b =>
        {
            Assert.Equal("internal only", b.GetProperty("adminNotes").GetString());
            Assert.True(b.TryGetProperty("paidLkr", out _));
            Assert.True(b.TryGetProperty("balanceLkr", out _));
        });
    }

    [Fact]
    public async Task The_last_page_has_the_rest_and_a_page_past_the_end_is_empty_with_the_real_totals()
    {
        var last = await ListAsync("?page=3");
        var beyond = await ListAsync("?page=9&pageSize=20");

        Assert.Equal(5, last.GetProperty("items").GetArrayLength());
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        Assert.Equal(45, beyond.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Walking_every_page_shows_each_booking_exactly_once()
    {
        var seen = new List<string>();
        for (var page = 1; page <= 7; page++)
        {
            seen.AddRange(References(await ListAsync($"?page={page}&pageSize=7")));
        }

        Assert.Equal(45, seen.Count);
        Assert.Equal(45, seen.Distinct().Count());
    }

    [Fact]
    public async Task Wrong_paging_values_get_their_messages_together()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/bookings?page=0&pageSize=500");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page must be a whole number of 1 or more", await Field(response, "page"));
        Assert.Equal("Page size must be a whole number from 1 to 100", await Field(response, "pageSize"));
    }

    // ---------------------------------------------------------------- sorting

    [Theory]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("date")]
    [InlineData("-date")]
    [InlineData("reference")]
    [InlineData("-reference")]
    [InlineData("name")]
    [InlineData("-name")]
    [InlineData("status")]
    [InlineData("-status")]
    [InlineData("priceLkr")]
    [InlineData("-priceLkr")]
    public async Task Every_sort_field_works_in_both_directions_and_pages_do_not_overlap(string sort)
    {
        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            seen.AddRange(References(await ListAsync($"?sort={sort}&page={page}&pageSize=20")));
        }

        Assert.Equal(45, seen.Distinct().Count());
        var all = (await ListAsync($"?sort={sort}&pageSize=100")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(seen, all.Select(b => b.GetProperty("reference").GetString()));

        var field = sort.TrimStart('-');
        var descending = sort.StartsWith('-');
        if (field is "date" or "reference" or "createdAt")
        {
            var values = all.Select(b => b.GetProperty(field).ToString()).ToList();
            Assert.Equal(descending ? values.OrderByDescending(v => v, StringComparer.Ordinal) : values.OrderBy(v => v, StringComparer.Ordinal), values);
        }
    }

    [Fact]
    public async Task Bookings_without_a_price_come_last_whichever_way_the_price_sorts()
    {
        var cheapest = (await ListAsync("?sort=priceLkr&pageSize=100")).GetProperty("items").EnumerateArray().Select(b => b.GetProperty("priceLkr")).ToList();
        var dearest = (await ListAsync("?sort=-priceLkr&pageSize=100")).GetProperty("items").EnumerateArray().Select(b => b.GetProperty("priceLkr")).ToList();

        Assert.Equal(1000m, cheapest[0].GetDecimal());
        Assert.Equal(JsonValueKind.Null, cheapest[^1].ValueKind);
        Assert.Equal(44000m, dearest[0].GetDecimal());
        Assert.Equal(JsonValueKind.Null, dearest[^1].ValueKind);
        Assert.Equal(9, cheapest.Count(price => price.ValueKind == JsonValueKind.Null));
        Assert.All(dearest.Take(36), price => Assert.NotEqual(JsonValueKind.Null, price.ValueKind));
    }

    [Fact]
    public async Task Several_sort_fields_order_within_ties()
    {
        var rows = (await ListAsync("?sort=status,-date&pageSize=100")).GetProperty("items").EnumerateArray()
            .Select(b => (Status: b.GetProperty("status").GetString()!, Date: b.GetProperty("date").GetString()!)).ToList();

        Assert.Equal(rows.OrderBy(r => r.Status, StringComparer.Ordinal).Select(r => r.Status), rows.Select(r => r.Status));
        foreach (var group in rows.GroupBy(r => r.Status))
        {
            Assert.Equal(group.Select(r => r.Date).OrderByDescending(d => d, StringComparer.Ordinal), group.Select(r => r.Date));
        }
    }

    [Fact]
    public async Task An_unknown_sort_field_names_the_allowed_ones()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/bookings?sort=password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Sort by one of: createdAt, date, reference, name, status, priceLkr", await Field(response, "sort"));
    }

    // ---------------------------------------------------------------- search

    [Theory]
    [InlineData("T017", "BK-T017")]
    [InlineData("bk-t017", "BK-T017")]
    [InlineData("guest17", "BK-T017")]
    [InlineData("GUEST17@EXAMPLE", "BK-T017")]
    [InlineData("000 0017", "BK-T017")]
    public async Task Search_finds_a_booking_by_reference_name_email_or_whatsapp_ignoring_case(string text, string expected)
    {
        var page = await ListAsync($"?search={Uri.EscapeDataString(text)}");

        Assert.Equal([expected], References(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_as_plain_characters_and_counts_only_matches()
    {
        Assert.Equal(["BK-T007"], References(await ListAsync("?search=" + Uri.EscapeDataString("100%"))));
        Assert.Empty(References(await ListAsync("?search=" + Uri.EscapeDataString("%%"))));
        Assert.Equal(["BK-T008"], References(await ListAsync("?search=" + Uri.EscapeDataString("r_s"))));
        Assert.Empty(References(await ListAsync("?search=" + Uri.EscapeDataString("t_0"))));

        var guests = await ListAsync("?search=guest&pageSize=10");
        Assert.Equal(45, guests.GetProperty("totalCount").GetInt32()); // every email is guestNN@example.com
        Assert.Equal(5, guests.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, (await ListAsync("?search=sure")).GetProperty("totalCount").GetInt32());
    }

    // ---------------------------------------------------------------- filters

    [Fact]
    public async Task The_status_filter_and_its_tab_counts()
    {
        var page = await ListAsync("?status=pending&facets=status&pageSize=100");

        Assert.Equal(18, page.GetProperty("totalCount").GetInt32()); // indexes with index % 5 in {0, 4}: 9 + 9
        Assert.All(page.GetProperty("items").EnumerateArray(), b => Assert.Equal("pending", b.GetProperty("status").GetString()));
        var counts = page.GetProperty("facets").GetProperty("status");
        Assert.Equal(18, counts.GetProperty("pending").GetInt32());
        Assert.Equal(9, counts.GetProperty("confirmed").GetInt32());
        Assert.Equal(9, counts.GetProperty("completed").GetInt32());
        Assert.Equal(9, counts.GetProperty("cancelled").GetInt32());
    }

    [Fact]
    public async Task Facet_counts_follow_the_search_but_not_the_status_filter()
    {
        var page = await ListAsync("?status=cancelled&search=guest1&facets=status");

        // Guest 10 to Guest 19: statuses by index % 5 -> 10 pending, 11 confirmed, 12 completed, 13 cancelled, 14 pending, ...
        var counts = page.GetProperty("facets").GetProperty("status");
        Assert.Equal(2, counts.GetProperty("cancelled").GetInt32());
        Assert.Equal(4, counts.GetProperty("pending").GetInt32());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_service_filter_and_the_travel_date_range()
    {
        var airport = await ListAsync("?serviceId=airport-transfer&pageSize=100");
        var range = await ListAsync("?from=2030-01-11&to=2030-01-20&pageSize=100");
        var combined = await ListAsync("?from=2030-01-11&to=2030-01-20&serviceId=airport-transfer&status=pending");

        Assert.Equal(22, airport.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, range.GetProperty("totalCount").GetInt32()); // both ends are included
        Assert.All(range.GetProperty("items").EnumerateArray(), b =>
        {
            var date = b.GetProperty("date").GetString()!;
            Assert.InRange(string.CompareOrdinal(date, "2030-01-11"), 0, int.MaxValue);
            Assert.InRange(string.CompareOrdinal(date, "2030-01-20"), int.MinValue, 0);
        });
        Assert.All(combined.GetProperty("items").EnumerateArray(), b => Assert.Equal("pending", b.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Wrong_filter_values_get_their_messages()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/bookings?status=done&from=tomorrow&to=2026-02-30");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Status must be one of: pending, confirmed, completed, cancelled", await Field(response, "status"));
        Assert.Equal("Choose a valid date", await Field(response, "from"));
        Assert.Equal("Choose a valid date", await Field(response, "to"));
    }

    [Fact]
    public async Task An_unknown_facet_names_the_allowed_ones()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/bookings?facets=role");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Facets can be: status", await Field(response, "facets"));
    }

    // ---------------------------------------------------------------- paid amounts and "mine"

    [Fact]
    public async Task Paid_and_balance_are_correct_for_the_bookings_on_the_page()
    {
        await Execute("""
            INSERT INTO payments (id, booking_id, amount, currency, exchange_rate, amount_lkr, method, status, paid_at)
            SELECT gen_random_uuid(), id, 5000, 'LKR', 1, 5000, 'cash', 'paid', '2026-10-05' FROM bookings WHERE reference = 'BK-T012';
            INSERT INTO payments (id, booking_id, amount, currency, exchange_rate, amount_lkr, method, status, paid_at)
            SELECT gen_random_uuid(), id, 1000, 'LKR', 1, 1000, 'cash', 'refunded', '2026-10-06' FROM bookings WHERE reference = 'BK-T012'
            """);

        var page = await ListAsync("?search=T012");

        var booking = page.GetProperty("items")[0];
        Assert.Equal(4000m, booking.GetProperty("paidLkr").GetDecimal());
        Assert.Equal(8000m, booking.GetProperty("balanceLkr").GetDecimal()); // price 12,000
    }

    [Fact]
    public async Task A_customer_sees_only_their_own_bookings_in_every_combination()
    {
        var own = await ListAsync("?pageSize=100", emmaToken);
        var filtered = await ListAsync("?status=pending&search=guest&sort=-date&facets=status&pageSize=100", emmaToken);

        Assert.Equal(30, own.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain(References(own), reference => int.Parse(reference[4..], CultureInfo.InvariantCulture) % 3 == 0);
        Assert.All(filtered.GetProperty("items").EnumerateArray(), b => Assert.False(b.TryGetProperty("adminNotes", out _)));
        Assert.DoesNotContain(References(filtered), reference => int.Parse(reference[4..], CultureInfo.InvariantCulture) % 3 == 0);
        var counts = filtered.GetProperty("facets").GetProperty("status");
        Assert.Equal(30, counts.EnumerateObject().Sum(p => p.Value.GetInt32())); // only Emma's 30, whatever the status filter
    }

    [Fact]
    public async Task Another_customer_gets_a_different_list_and_a_search_cannot_reach_someone_elses_booking()
    {
        var liam = await ListAsync("?pageSize=100", liamToken);
        var cross = await ListAsync("?search=T001", liamToken); // BK-T001 is Emma's

        Assert.Equal(15, liam.GetProperty("totalCount").GetInt32());
        Assert.Empty(References(cross));
        Assert.Equal(0, cross.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task My_bookings_use_the_same_checks()
    {
        var response = await Api.GetAsync("/api/bookings/mine?pageSize=0&sort=oops&status=done", emmaToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page size must be a whole number from 1 to 100", await Field(response, "pageSize"));
        Assert.NotNull(await Field(response, "sort"));
        Assert.NotNull(await Field(response, "status"));
    }

    // ---------------------------------------------------------------- the declaration

    [Fact]
    public void The_database_side_implements_exactly_what_the_definition_declares()
    {
        var (sort, facets) = BookingRepository.ListNames;

        Assert.Equal(BookingLists.Definition.SortFields.Order(), sort.Order());
        Assert.Equal(BookingLists.Definition.Facets.Order(), facets.Order());
    }
}
