using System.Net;
using System.Text.Json;
using TravelWebAPI.Application.Search;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The rules of the search text, then the admin search through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SearchEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private async Task<JsonElement> SearchAsync(string q)
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/search?q=" + Uri.EscapeDataString(q));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static List<string> Names(JsonElement group, string property = "name")
        => group.EnumerateArray().Select(item => item.GetProperty(property).GetString()!).ToList();

    private Task AddUser(string name, string email) => AsAdmin(HttpMethod.Post, "/api/users", new { name, email, password = "Secret123", role = "customer" });

    // ---------------------------------------------------------------- text rules

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" a ", "")]
    [InlineData("ab", "ab")]
    [InlineData("  sigiriya  ", "sigiriya")]
    public void Short_or_empty_text_does_not_run_a_search(string? text, string expected)
        => Assert.Equal(expected, SearchRules.Clean(text));

    [Fact]
    public void A_long_text_is_cut_to_100_characters()
        => Assert.Equal(100, SearchRules.Clean(new string('x', 500)).Length);

    [Theory]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("plain", "%plain%")]
    public void Wildcards_in_the_text_become_plain_characters(string text, string pattern)
        => Assert.Equal(pattern, SearchRules.ContainsPattern(text));

    // ---------------------------------------------------------------- access and short text

    [Fact]
    public async Task Guests_and_customers_are_refused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync("/api/search?q=sigiriya")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsync("/api/search?q=sigiriya", bearer: CustomerToken)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("   ")]
    public async Task A_missing_empty_or_one_character_text_gives_four_empty_lists(string q)
    {
        await CreateAsync("destinations", Destination("Sigiriya"));

        var result = await SearchAsync(q);

        foreach (var group in new[] { "bookings", "packages", "destinations", "users" })
        {
            Assert.Equal(0, result.GetProperty(group).GetArrayLength());
        }
    }

    [Fact]
    public async Task No_q_at_all_is_also_four_empty_lists()
    {
        var result = await Json(await AsAdmin(HttpMethod.Get, "/api/search"));

        Assert.Equal(0, result.GetProperty("bookings").GetArrayLength());
        Assert.Equal(0, result.GetProperty("users").GetArrayLength());
    }

    // ---------------------------------------------------------------- each group

    [Fact]
    public async Task Packages_are_found_by_name_or_tagline_published_or_not_and_the_item_has_id_and_name()
    {
        await CreateAsync("packages", Package("Cultural Triangle", extra: new { tagline = "Ancient kingdoms" }));
        await CreateAsync("packages", Package("Hill Country Escape", extra: new { tagline = "Tea and trains", active = false }));
        await CreateAsync("packages", Package("Beach Break", extra: new { tagline = "Southern sun and cultural stops" }));

        var byName = await SearchAsync("HILL country");
        var byTagline = await SearchAsync("cultural");

        Assert.Equal(["Hill Country Escape"], Names(byName.GetProperty("packages")));
        Assert.Equal("hill-country-escape", byName.GetProperty("packages")[0].GetProperty("id").GetString());
        Assert.Equal(["Beach Break", "Cultural Triangle"], Names(byTagline.GetProperty("packages")));
        Assert.Equal(2, byName.GetProperty("packages")[0].EnumerateObject().Count());
    }

    [Fact]
    public async Task Destinations_are_found_by_name_or_region()
    {
        await CreateAsync("destinations", Destination("Sigiriya", new { region = "Cultural Triangle" }));
        await CreateAsync("destinations", Destination("Ella", new { region = "Hill Country" }));

        var byName = await SearchAsync("sigi");
        var byRegion = await SearchAsync("hill");

        Assert.Equal(["Sigiriya"], Names(byName.GetProperty("destinations")));
        Assert.Equal("sigiriya", byName.GetProperty("destinations")[0].GetProperty("id").GetString());
        Assert.Equal(["Ella"], Names(byRegion.GetProperty("destinations")));
    }

    [Fact]
    public async Task Users_are_found_by_name_or_email_with_their_role()
    {
        await AddUser("Priya Nair", "priya@example.com");
        await AddUser("Someone Else", "else@nair-family.example");

        var byName = await SearchAsync("PRIYA");
        var byEmail = await SearchAsync("nair-family");

        var user = byName.GetProperty("users")[0];
        Assert.Equal("Priya Nair", user.GetProperty("name").GetString());
        Assert.Equal("priya@example.com", user.GetProperty("email").GetString());
        Assert.Equal("customer", user.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.String, user.GetProperty("id").ValueKind);
        Assert.False(user.TryGetProperty("passwordHash", out _));
        Assert.Equal(["Someone Else"], Names(byEmail.GetProperty("users")));
    }

    [Fact]
    public async Task Bookings_are_found_by_reference_name_or_email_newest_first_with_the_item_shape()
    {
        await CreateAsync("services", Service("Private tour"));
        await AddBookingAsync("private-tour", reference: "BK-ABC123");
        await AddBookingAsync("private-tour", reference: "BK-XYZ789");
        await Execute("UPDATE bookings SET created_at = '2026-01-01T00:00:00Z' WHERE reference = 'BK-ABC123'");

        var byReference = await SearchAsync("bk-xyz");
        var byName = await SearchAsync("test user");
        var byEmail = await SearchAsync("TESTER@example");

        Assert.Equal(["BK-XYZ789"], Names(byReference.GetProperty("bookings"), "reference"));
        Assert.Equal(["BK-XYZ789", "BK-ABC123"], Names(byName.GetProperty("bookings"), "reference"));
        Assert.Equal(2, byEmail.GetProperty("bookings").GetArrayLength());
        var item = byReference.GetProperty("bookings")[0];
        Assert.Equal("Test User", item.GetProperty("name").GetString());
        Assert.Equal("pending", item.GetProperty("status").GetString());
        Assert.Equal("2030-01-15", item.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.String, item.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task One_text_can_match_several_groups()
    {
        await CreateAsync("services", Service("Private tour"));
        await CreateAsync("destinations", Destination("Kandy City", new { region = "Central" }));
        await AddBookingAsync("private-tour", reference: "BK-KAN111");
        await AddUser("Kandy Visitor", "kv@example.com");

        var result = await SearchAsync("kan");

        Assert.Equal(1, result.GetProperty("bookings").GetArrayLength());
        Assert.Equal(1, result.GetProperty("destinations").GetArrayLength());
        Assert.Equal(1, result.GetProperty("users").GetArrayLength());
    }

    // ---------------------------------------------------------------- limits and safety

    [Fact]
    public async Task Each_group_is_limited_to_six_for_bookings_and_five_for_the_others()
    {
        await CreateAsync("services", Service("Private tour"));
        await AddBookingAsync("private-tour", reference: "BK-SEED11");
        await Execute("""
            INSERT INTO bookings (id, reference, user_id, service_id, travel_date, travel_time, adults, name, email, whatsapp, country)
            SELECT gen_random_uuid(), 'BK-LIM' || lpad(n::text, 3, '0'), (SELECT user_id FROM bookings LIMIT 1), 'private-tour',
                   DATE '2030-01-01', '09:30', 1, 'Many Match', 'm@example.com', '+94', 'LK'
            FROM generate_series(1, 8) AS n
            """);
        for (var index = 1; index <= 7; index++)
        {
            await CreateAsync("destinations", Destination($"Many Match {index}"));
            await CreateAsync("packages", Package($"Many Match {index}"));
            await AddUser($"Many Match {index}", $"many{index}@example.com");
        }

        var result = await SearchAsync("many match");

        Assert.Equal(6, result.GetProperty("bookings").GetArrayLength());
        Assert.Equal(5, result.GetProperty("packages").GetArrayLength());
        Assert.Equal(5, result.GetProperty("destinations").GetArrayLength());
        Assert.Equal(5, result.GetProperty("users").GetArrayLength());
    }

    [Fact]
    public async Task Percent_underscore_and_backslash_are_matched_as_themselves()
    {
        await CreateAsync("destinations", Destination("Plain Name"));
        await CreateAsync("destinations", Destination("Half 100% Sure"));
        await CreateAsync("destinations", Destination("Under_score Town"));
        await CreateAsync("destinations", Destination("Back\\slash Bay"));

        Assert.Equal(["Half 100% Sure"], Names((await SearchAsync("100%")).GetProperty("destinations")));
        Assert.Equal(0, (await SearchAsync("%%")).GetProperty("destinations").GetArrayLength());
        Assert.Equal(["Under_score Town"], Names((await SearchAsync("r_s")).GetProperty("destinations")));
        Assert.Equal(0, (await SearchAsync("p_a")).GetProperty("destinations").GetArrayLength());
        Assert.Equal(["Back\\slash Bay"], Names((await SearchAsync("k\\s")).GetProperty("destinations")));
    }

    [Fact]
    public async Task Quotes_and_a_very_long_text_do_not_break_the_search()
    {
        await CreateAsync("destinations", Destination("Sigiriya"));

        var quote = await SearchAsync("'; DROP TABLE users; --");
        var longText = await SearchAsync(new string('x', 5000));

        Assert.Equal(0, quote.GetProperty("destinations").GetArrayLength());
        Assert.Equal(0, longText.GetProperty("destinations").GetArrayLength());
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM destinations"));
    }
}
