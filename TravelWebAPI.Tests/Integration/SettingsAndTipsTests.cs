using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The site-settings and travel-tips documents through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SettingsAndTipsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static object ValidSettings(object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "  Ceylon Roads  ",
            ["tagline"] = "Private driver & tours",
            ["driverName"] = "Nuwan Dissanayake",
            ["driverRole"] = "Guide, driver & owner",
            ["phone"] = "+94 77 123 4567",
            ["whatsappNumber"] = "94771234567",
            ["email"] = "hello@ceylonroads.example",
            ["address"] = "Negombo, Sri Lanka",
            ["socials"] = new object[]
            {
                new { label = "Facebook", href = "https://facebook.com/ceylonroads" },
                new { label = "Broken", href = "" },
                new { label = "", href = "https://example.com" },
            },
            ["trust"] = new { yearsExperience = 12, tripsCompleted = 2400, rating = 4.86, licensed = "SLTDA licensed driver" },
            ["languages"] = new[] { "English", "  ", "Sinhala" },
            ["licenses"] = new[] { "SLTDA licence" },
            ["currencyRates"] = new { USD = 1, EUR = 0.9, GBP = 0.8, LKR = 300 },
        };

        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                body[property.Name] = property.Value.Clone();
            }
        }

        return body;
    }

    private async Task SeedAsync()
    {
        await using var db = PostgresFixture.CreateContext(Connection);
        await new DatabaseSeeder(db, new PasswordHasher(iterations: 1_000), NullLogger<DatabaseSeeder>.Instance).SeedAsync(new SeedOptions());
    }

    private static object Region(string id = "south-west", int months = 12, string suitability = "great") => new
    {
        id,
        region = "South & West Coast",
        summary = "Beach season runs December to April.",
        months = Enumerable.Range(0, months).Select(_ => new { suitability, note = "Dry and sunny" }).ToArray(),
    };

    private static object Info(string id = "eta", object? link = null) => new
    {
        id,
        title = "ETA / Visa",
        body = "Most visitors need an Electronic Travel Authorisation before arrival.",
        link,
    };

    // ---------------------------------------------------------------- read settings

    [Fact]
    public async Task Anyone_can_read_the_seeded_settings_in_the_website_shape()
    {
        await SeedAsync();

        var response = await Api.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await Json(response);
        Assert.Equal("Ceylon Roads", settings.GetProperty("name").GetString());
        Assert.Equal("94771234567", settings.GetProperty("whatsappNumber").GetString());
        var trust = settings.GetProperty("trust");
        Assert.Equal(12, trust.GetProperty("yearsExperience").GetInt32());
        Assert.Equal(2400, trust.GetProperty("tripsCompleted").GetInt32());
        Assert.Equal(4.9m, trust.GetProperty("rating").GetDecimal());
        Assert.Equal("SLTDA licensed driver", trust.GetProperty("licensed").GetString());
        var social = settings.GetProperty("socials")[0];
        Assert.Equal("Facebook", social.GetProperty("label").GetString());
        Assert.StartsWith("https://", social.GetProperty("href").GetString(), StringComparison.Ordinal);
        Assert.Equal(["English", "Sinhala"], Strings(settings.GetProperty("languages")));
        Assert.False(settings.TryGetProperty("currencyRates", out _));
        Assert.False(settings.TryGetProperty("updatedAt", out _));
    }

    [Fact]
    public async Task Settings_on_an_empty_database_are_an_empty_document_not_an_error()
    {
        var response = await Api.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var settings = await Json(response);
        Assert.Equal(string.Empty, settings.GetProperty("name").GetString());
        Assert.Empty(settings.GetProperty("socials").EnumerateArray());
        Assert.Equal(0, settings.GetProperty("trust").GetProperty("yearsExperience").GetInt32());
    }

    // ---------------------------------------------------------------- replace settings

    [Fact]
    public async Task An_admin_replaces_the_settings_and_the_change_is_public()
    {
        await SeedAsync();

        var response = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Json(response);
        var read = await Json(await Api.GetAsync("/api/settings"));
        Assert.True(SameJson(saved, read));
        Assert.Equal("Ceylon Roads", read.GetProperty("name").GetString());
        Assert.Equal("Private driver & tours", read.GetProperty("tagline").GetString());
        Assert.Equal(4.9m, read.GetProperty("trust").GetProperty("rating").GetDecimal());
        Assert.Equal(["English", "Sinhala"], Strings(read.GetProperty("languages")));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM site_settings"));
    }

    [Fact]
    public async Task Incomplete_social_links_are_dropped_and_old_currency_rates_are_ignored()
    {
        var response = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Json(response);
        var socials = saved.GetProperty("socials");
        Assert.Equal(1, socials.GetArrayLength());
        Assert.Equal("https://facebook.com/ceylonroads", socials[0].GetProperty("href").GetString());
        Assert.False(saved.TryGetProperty("currencyRates", out _));
        Assert.DoesNotContain("currency", await Scalar<string>("SELECT row_to_json(site_settings)::text FROM site_settings"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_first_save_on_an_empty_database_creates_the_row()
    {
        var response = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM site_settings"));
        Assert.Equal("hello@ceylonroads.example", await Scalar<string>("SELECT email FROM site_settings"));
    }

    [Fact]
    public async Task Only_an_admin_can_replace_the_settings()
    {
        await SeedAsync();
        var before = await Json(await Api.GetAsync("/api/settings"));

        var anonymous = await Api.SendAsync(HttpMethod.Put, "/api/settings", ValidSettings());
        var customer = await Api.SendAsync(HttpMethod.Put, "/api/settings", ValidSettings(), bearer: CustomerToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.True(SameJson(before, await Json(await Api.GetAsync("/api/settings"))));
    }

    [Fact]
    public async Task An_empty_settings_body_reports_the_required_fields()
    {
        var response = await AsAdmin(HttpMethod.Put, "/api/settings", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter the business name", await Field(response, "name"));
        Assert.Equal("Enter the owner name", await Field(response, "driverName"));
        Assert.Equal("Enter a valid email address", await Field(response, "email"));
        Assert.Equal("Enter a phone number", await Field(response, "phone"));
        Assert.Equal("WhatsApp number must be digits only, with country code (e.g. 94771234567)", await Field(response, "whatsappNumber"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM site_settings"));
    }

    [Theory]
    [InlineData("whatsappNumber", "+94 77 123 4567", "whatsappNumber", "WhatsApp number must be digits only, with country code (e.g. 94771234567)")]
    [InlineData("whatsappNumber", "1234567", "whatsappNumber", "WhatsApp number must be digits only, with country code (e.g. 94771234567)")]
    [InlineData("email", "not-an-email", "email", "Enter a valid email address")]
    [InlineData("phone", "123", "phone", "Enter a phone number")]
    [InlineData("name", "A", "name", "Enter the business name")]
    public async Task Invalid_text_settings_give_the_contract_messages(string property, string value, string field, string message)
    {
        var response = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings(new Dictionary<string, string> { [property] = value }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await Field(response, field));
    }

    [Fact]
    public async Task Trust_figures_and_social_links_are_checked()
    {
        var badRating = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings(new { trust = new { yearsExperience = 12, tripsCompleted = 5, rating = 7, licensed = "x" } }));
        var negative = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings(new { trust = new { yearsExperience = -1, tripsCompleted = -5, rating = 4, licensed = "x" } }));
        var unsafeLink = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings(new { socials = new[] { new { label = "Hack", href = "javascript:alert(1)" } } }));
        var plainHttp = await AsAdmin(HttpMethod.Put, "/api/settings", ValidSettings(new { socials = new[] { new { label = "Old", href = "http://example.com" } } }));

        Assert.Equal("Rating must be between 0 and 5", await Field(badRating, "trust.rating"));
        Assert.Equal("Enter a whole number of 0 or more", await Field(negative, "trust.yearsExperience"));
        Assert.Equal("Enter a whole number of 0 or more", await Field(negative, "trust.tripsCompleted"));
        Assert.Equal("Social links must start with https://", await Field(unsafeLink, "socials"));
        Assert.Equal("Social links must start with https://", await Field(plainHttp, "socials"));
    }

    // ---------------------------------------------------------------- tips

    [Fact]
    public async Task Anyone_can_read_the_seeded_tips()
    {
        await SeedAsync();

        var response = await Api.GetAsync("/api/tips");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tips = await Json(response);
        var regions = tips.GetProperty("regions").EnumerateArray().ToList();
        Assert.Equal(4, regions.Count);
        Assert.All(regions, region => Assert.Equal(12, region.GetProperty("months").GetArrayLength()));
        var eta = tips.GetProperty("info").EnumerateArray().First(i => i.GetProperty("id").GetString() == "eta");
        Assert.Equal("https://www.eta.gov.lk/", eta.GetProperty("link").GetProperty("href").GetString());
        var currency = tips.GetProperty("info").EnumerateArray().First(i => i.GetProperty("id").GetString() == "currency");
        Assert.False(currency.TryGetProperty("link", out _));
    }

    [Fact]
    public async Task Tips_on_an_empty_database_are_two_empty_lists()
    {
        var tips = await Json(await Api.GetAsync("/api/tips"));

        Assert.Empty(tips.GetProperty("regions").EnumerateArray());
        Assert.Empty(tips.GetProperty("info").EnumerateArray());
    }

    [Fact]
    public async Task An_admin_replaces_the_tips_and_they_read_back_the_same()
    {
        await SeedAsync();
        var body = new { regions = new[] { Region("east", suitability: "good") }, info = new[] { Info(link: new { href = "https://example.com/eta", label = "Official site" }), Info("plain") } };

        var response = await AsAdmin(HttpMethod.Put, "/api/tips", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await Json(response);
        var read = await Json(await Api.GetAsync("/api/tips"));
        Assert.True(SameJson(saved, read));
        Assert.Equal(["east"], read.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.Equal(["eta", "plain"], read.GetProperty("info").EnumerateArray().Select(r => r.GetProperty("id").GetString()));
        Assert.False(read.GetProperty("info")[1].TryGetProperty("link", out _));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM travel_tips"));
    }

    [Fact]
    public async Task Both_lists_are_required()
    {
        var noInfo = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = Array.Empty<object>() });
        var neither = await AsAdmin(HttpMethod.Put, "/api/tips", new { });

        Assert.Equal(HttpStatusCode.BadRequest, noInfo.StatusCode);
        Assert.Contains("required", await Field(noInfo, "info"), StringComparison.Ordinal);
        Assert.Null(await Field(noInfo, "regions"));
        Assert.Contains("required", await Field(neither, "regions"), StringComparison.Ordinal);
        Assert.Contains("required", await Field(neither, "info"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_region_needs_exactly_twelve_months_with_a_known_suitability()
    {
        var eleven = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = new[] { Region(months: 11) }, info = Array.Empty<object>() });
        var unknown = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = new[] { Region(suitability: "amazing") }, info = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, eleven.StatusCode);
        Assert.Contains("12 months", await Field(eleven, "regions[0].months"), StringComparison.Ordinal);
        Assert.Contains("great, good, fair, poor", await Field(unknown, "regions[0].months"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ids_must_be_present_and_unique_and_links_must_be_web_addresses()
    {
        var duplicate = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = new[] { Region("a"), Region("a") }, info = new[] { Info("x"), Info("x") } });
        var noId = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = new[] { Region("") }, info = Array.Empty<object>() });
        var badLink = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = Array.Empty<object>(), info = new[] { Info(link: new { href = "javascript:alert(1)", label = "Click" }) } });

        Assert.Equal("Region ids must be unique", await Field(duplicate, "regions"));
        Assert.Equal("Information ids must be unique", await Field(duplicate, "info"));
        Assert.Equal("Every region needs an id", await Field(noId, "regions[0].id"));
        Assert.Contains("https://", await Field(badLink, "info[0].link"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_an_admin_can_replace_the_tips()
    {
        await SeedAsync();
        var before = await Json(await Api.GetAsync("/api/tips"));
        var body = new { regions = Array.Empty<object>(), info = Array.Empty<object>() };

        var anonymous = await Api.SendAsync(HttpMethod.Put, "/api/tips", body);
        var customer = await Api.SendAsync(HttpMethod.Put, "/api/tips", body, bearer: CustomerToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        Assert.True(SameJson(before, await Json(await Api.GetAsync("/api/tips"))));
    }

    [Fact]
    public async Task A_failed_tips_save_keeps_the_old_tips()
    {
        await SeedAsync();
        var before = await Json(await Api.GetAsync("/api/tips"));

        var response = await AsAdmin(HttpMethod.Put, "/api/tips", new { regions = new[] { Region(months: 3) }, info = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(SameJson(before, await Json(await Api.GetAsync("/api/tips"))));
    }
}
