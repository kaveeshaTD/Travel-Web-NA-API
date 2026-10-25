using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Reviews through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static object Submission(object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Emma Wilson",
            ["country"] = "United Kingdom",
            ["rating"] = 5,
            ["tripType"] = "Cultural",
            ["comment"] = "Wonderful trip, our driver was excellent.",
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

    private async Task<JsonElement> AddAsync(string name, string date = "2026-01-01", string? status = null, int rating = 5)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["country"] = "Germany",
            ["rating"] = rating,
            ["tripType"] = "Beach",
            ["comment"] = $"{name} had a really nice time here.",
            ["date"] = date,
        };
        if (status is not null)
        {
            body["status"] = status;
        }

        var response = await AsAdmin(HttpMethod.Post, "/api/reviews/admin", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private async Task<List<string>> PublicNamesAsync() =>
        (await Json(await Api.GetAsync("/api/reviews"))).EnumerateArray().Select(r => r.GetProperty("name").GetString()!).ToList();

    private async Task AddCustomerAsync(Guid id)
    {
        await using var db = PostgresFixture.CreateContext(Connection);
        var user = TestData.User($"{id:N}@example.com");
        user.Id = id;
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- reading

    [Fact]
    public async Task An_empty_list_is_an_empty_array()
    {
        var response = await Api.GetAsync("/api/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).EnumerateArray());
    }

    [Fact]
    public async Task Visitors_see_only_published_reviews_newest_first_without_an_author()
    {
        await AddAsync("Older", "2026-01-01");
        await AddAsync("Newest", "2026-03-01");
        await AddAsync("Middle", "2026-02-01");
        await AddAsync("Waiting", "2026-04-01", status: "pending");
        await AddAsync("Refused", "2026-05-01", status: "rejected");

        var list = await Json(await Api.GetAsync("/api/reviews"));

        Assert.Equal(["Newest", "Middle", "Older"], list.EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        var first = list[0];
        Assert.False(first.TryGetProperty("userId", out _));
        Assert.Equal("published", first.GetProperty("status").GetString());
        Assert.Equal("2026-03-01", first.GetProperty("date").GetString());
        Assert.Equal("DE", first.GetProperty("countryCode").GetString());
        Assert.Equal(5, first.GetProperty("rating").GetInt32());
        Assert.Equal(string.Empty, first.GetProperty("photo").GetString());
        Assert.Equal(JsonValueKind.String, first.GetProperty("id").ValueKind);
    }

    [Fact]
    public async Task Reviews_on_the_same_day_are_ordered_by_when_they_were_created()
    {
        await AddAsync("First", "2026-02-01");
        await AddAsync("Second", "2026-02-01");

        Assert.Equal(["Second", "First"], await PublicNamesAsync());
    }

    [Fact]
    public async Task An_admin_with_all_sees_every_status_and_the_author_field()
    {
        await AddAsync("Live", status: "published");
        await AddAsync("Waiting", status: "pending");
        await AddAsync("Refused", status: "rejected");

        var list = await Json(await AsAdmin(HttpMethod.Get, "/api/reviews?all=1"));

        Assert.Equal(3, list.GetArrayLength());
        Assert.Equal(["pending", "published", "rejected"], list.EnumerateArray().Select(r => r.GetProperty("status").GetString()).Order(StringComparer.Ordinal));
        Assert.All(list.EnumerateArray(), review =>
        {
            Assert.True(review.TryGetProperty("userId", out var userId));
            Assert.Equal(JsonValueKind.Null, userId.ValueKind);
        });
    }

    [Fact]
    public async Task Anonymous_visitors_and_customers_cannot_widen_the_list_with_all()
    {
        await AddAsync("Live");
        await AddAsync("Waiting", status: "pending");

        var anonymous = await Json(await Api.GetAsync("/api/reviews?all=1"));
        var customer = await Json(await Api.GetAsync("/api/reviews?all=1", CustomerToken));

        Assert.Equal(1, anonymous.GetArrayLength());
        Assert.Equal(1, customer.GetArrayLength());
        Assert.False(customer[0].TryGetProperty("userId", out _));
    }

    [Fact]
    public async Task An_admin_without_all_gets_the_public_list()
    {
        await AddAsync("Live");
        await AddAsync("Waiting", status: "pending");

        var list = await Json(await Api.GetAsync("/api/reviews", AdminToken));

        Assert.Equal(1, list.GetArrayLength());
    }

    // ---------------------------------------------------------------- submitting

    [Fact]
    public async Task A_guest_review_is_201_with_a_reference_and_waits_as_pending()
    {
        var response = await Api.PostAsync("/api/reviews", Submission());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Matches("^REV-[0-9A-F]{8}$", (await Json(response)).GetProperty("reference").GetString());
        Assert.Equal("pending", await Scalar<string>("SELECT status FROM reviews"));
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT user_id FROM reviews"));
        Assert.Equal("GB", (await Scalar<string>("SELECT country_code FROM reviews")).Trim());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5)), await Scalar<DateOnly>("SELECT review_date FROM reviews"));
        Assert.Empty(await PublicNamesAsync());
    }

    [Fact]
    public async Task A_signed_in_customer_review_is_linked_to_the_customer()
    {
        var customerId = Guid.CreateVersion7();
        await AddCustomerAsync(customerId);
        var token = TestTokens.For(customerId, UserRole.Customer);

        var response = await Api.PostAsync("/api/reviews", Submission(), bearer: token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(customerId.ToString(), await Scalar<string>("SELECT user_id::text FROM reviews"));
    }

    [Fact]
    public async Task An_unknown_country_still_saves_with_an_empty_code()
    {
        var response = await Api.PostAsync("/api/reviews", Submission(new { country = "Narnia" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(string.Empty, (await Scalar<string>("SELECT country_code FROM reviews")).Trim());
    }

    [Fact]
    public async Task Submitted_text_is_trimmed()
    {
        await Api.PostAsync("/api/reviews", Submission(new { name = "  Emma  ", comment = "   Wonderful trip, our driver was excellent.   " }));

        Assert.Equal("Emma", await Scalar<string>("SELECT name FROM reviews"));
        Assert.Equal("Wonderful trip, our driver was excellent.", await Scalar<string>("SELECT comment FROM reviews"));
    }

    [Fact]
    public async Task Invalid_submissions_report_every_field_and_store_nothing()
    {
        var response = await Api.PostAsync("/api/reviews", new { name = "E", country = "U", rating = 9, tripType = "", comment = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter your name", await Field(response, "name"));
        Assert.Equal("Enter your country", await Field(response, "country"));
        Assert.Equal("Choose a rating from 1 to 5", await Field(response, "rating"));
        Assert.Equal("Choose your trip type", await Field(response, "tripType"));
        Assert.Equal("Please write at least 10 characters", await Field(response, "comment"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM reviews"));
    }

    [Fact]
    public async Task A_rating_with_a_fraction_gets_the_contract_message()
    {
        var response = await Api.PostAsync("/api/reviews", Submission(new { rating = 4.5 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Choose a rating from 1 to 5", await Field(response, "rating"));
    }

    [Fact]
    public async Task An_empty_object_and_an_empty_body_both_report_the_required_fields()
    {
        var empty = await Api.PostAsync("/api/reviews", new { });
        using var noBody = new HttpRequestMessage(HttpMethod.Post, "/api/reviews") { Content = Raw(string.Empty) };
        var missing = await Api.SendAsync(noBody);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("Enter your name", await Field(empty, "name"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task A_submitted_review_cannot_choose_its_own_status_date_or_author()
    {
        var response = await Api.PostAsync("/api/reviews", Submission(new { status = "published", date = "2000-01-01", userId = Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("pending", await Scalar<string>("SELECT status FROM reviews"));
        Assert.NotEqual(2000, (await Scalar<DateOnly>("SELECT review_date FROM reviews")).Year);
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT user_id FROM reviews"));
    }

    [Fact]
    public async Task The_sixth_submission_from_one_address_is_429_with_retry_after_and_nothing_is_stored()
    {
        await using var factory = ApiFactory.With(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = Connection,
            ["RateLimiting:SubmitPermitLimit"] = "5",
            ["RateLimiting:SubmitWindowSeconds"] = "600",
        });
        var api = new AuthApi(AuthApi.CreateClient(factory));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.PostAsync("/api/reviews", Submission())).StatusCode);
        }

        var sixth = await api.PostAsync("/api/reviews", Submission());

        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.True(sixth.Headers.Contains("Retry-After"));
        AuthApi.AssertError(await Json(sixth), HttpStatusCode.TooManyRequests);
        Assert.Equal(5L, await Scalar<long>("SELECT count(*) FROM reviews"));
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/health")).StatusCode);
    }

    // ---------------------------------------------------------------- admin add

    [Fact]
    public async Task An_admin_added_review_is_201_published_and_public()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/reviews/admin", new { name = "Hans Muller", country = "Germany", rating = 5, tripType = "Wildlife", comment = "Saw elephants and leopards!" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var review = await Json(response);
        Assert.Equal("published", review.GetProperty("status").GetString());
        Assert.True(review.TryGetProperty("userId", out _));
        Assert.Equal(["Hans Muller"], await PublicNamesAsync());
    }

    [Fact]
    public async Task Admin_messages_are_the_ones_in_the_contract()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/reviews/admin", new { name = "H", rating = 9, comment = "short", status = "archived", date = "2999-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter the reviewer name", await Field(response, "name"));
        Assert.Equal("Rating must be 1 to 5", await Field(response, "rating"));
        Assert.Equal("Comment is too short", await Field(response, "comment"));
        Assert.Equal("Status must be one of: pending, published, rejected", await Field(response, "status"));
        Assert.Equal("Enter a valid date that is not in the future", await Field(response, "date"));
    }

    [Fact]
    public async Task Admin_routes_need_an_admin()
    {
        var created = await AddAsync("Someone");
        var id = created.GetProperty("id").GetString();
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, "/api/reviews/admin", new { name = "Hans" }),
            (HttpMethod.Put, $"/api/reviews/{id}", new { status = "rejected" }),
            (HttpMethod.Delete, $"/api/reviews/{id}", null),
        };

        foreach (var (method, path, body) in attempts)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(method, path, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Api.SendAsync(method, path, body, bearer: CustomerToken)).StatusCode);
        }

        Assert.Equal(["Someone"], await PublicNamesAsync());
    }

    // ---------------------------------------------------------------- moderation

    [Fact]
    public async Task Publishing_a_pending_review_makes_it_public_and_rejecting_hides_it_again()
    {
        var pending = await AddAsync("Waiting", status: "pending");
        var id = pending.GetProperty("id").GetString();
        Assert.Empty(await PublicNamesAsync());

        var published = await AsAdmin(HttpMethod.Put, $"/api/reviews/{id}", new { status = "published" });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        Assert.Equal(["Waiting"], await PublicNamesAsync());

        var rejected = await AsAdmin(HttpMethod.Put, $"/api/reviews/{id}", new { status = "rejected" });
        Assert.Equal("rejected", (await Json(rejected)).GetProperty("status").GetString());
        Assert.Empty(await PublicNamesAsync());
        Assert.Equal(1, (await Json(await AsAdmin(HttpMethod.Get, "/api/reviews?all=1"))).GetArrayLength());
    }

    [Fact]
    public async Task A_partial_edit_changes_only_the_sent_fields()
    {
        var review = await AddAsync("Hans", "2026-02-02", rating: 4);
        var id = review.GetProperty("id").GetString();

        var response = await AsAdmin(HttpMethod.Put, $"/api/reviews/{id}", new { comment = "An updated comment about the trip." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Json(response);
        Assert.Equal("An updated comment about the trip.", updated.GetProperty("comment").GetString());
        Assert.Equal("Hans", updated.GetProperty("name").GetString());
        Assert.Equal(4, updated.GetProperty("rating").GetInt32());
        Assert.Equal("2026-02-02", updated.GetProperty("date").GetString());
        Assert.Equal("DE", updated.GetProperty("countryCode").GetString());
        Assert.Equal("published", updated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_invalid_edit_is_400_and_changes_nothing()
    {
        var review = await AddAsync("Hans");
        var id = review.GetProperty("id").GetString();

        var response = await AsAdmin(HttpMethod.Put, $"/api/reviews/{id}", new { rating = 7, comment = "no" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((short)5, await Scalar<short>("SELECT rating FROM reviews"));
        Assert.Equal("Hans had a really nice time here.", await Scalar<string>("SELECT comment FROM reviews"));
    }

    [Fact]
    public async Task Unknown_or_malformed_ids_are_404_for_edit_and_delete()
    {
        foreach (var id in new[] { Guid.NewGuid().ToString(), "not-a-guid", "123" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await AsAdmin(HttpMethod.Put, $"/api/reviews/{id}", new { status = "published" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await AsAdmin(HttpMethod.Delete, $"/api/reviews/{id}")).StatusCode);
        }
    }

    [Fact]
    public async Task Delete_is_204_and_a_second_delete_is_404()
    {
        var review = await AddAsync("Hans");
        var id = review.GetProperty("id").GetString();

        var first = await AsAdmin(HttpMethod.Delete, $"/api/reviews/{id}");
        var second = await AsAdmin(HttpMethod.Delete, $"/api/reviews/{id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM reviews"));
    }

    [Fact]
    public async Task A_review_stays_when_its_author_account_is_removed()
    {
        var customerId = Guid.CreateVersion7();
        await AddCustomerAsync(customerId);
        await Api.PostAsync("/api/reviews", Submission(), bearer: TestTokens.For(customerId, UserRole.Customer));

        await Execute($"DELETE FROM users WHERE id = '{customerId}'");

        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM reviews"));
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT user_id FROM reviews"));
    }

    // ---------------------------------------------------------------- seed

    [Fact]
    public async Task Seeded_reviews_are_served_with_two_letter_trimmed_codes()
    {
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            await new DatabaseSeeder(db, new PasswordHasher(iterations: 1_000), NullLogger<DatabaseSeeder>.Instance).SeedAsync(new SeedOptions());
        }

        var publicList = await Json(await Api.GetAsync("/api/reviews"));
        var all = await Json(await AsAdmin(HttpMethod.Get, "/api/reviews?all=1"));

        Assert.NotEmpty(publicList.EnumerateArray());
        Assert.All(publicList.EnumerateArray(), review =>
        {
            Assert.Equal("published", review.GetProperty("status").GetString());
            Assert.True(review.GetProperty("countryCode").GetString()!.Length is 0 or 2);
            Assert.Matches("^\\d{4}-\\d{2}-\\d{2}$", review.GetProperty("date").GetString());
        });
        Assert.True(all.GetArrayLength() >= publicList.GetArrayLength());
    }
}
