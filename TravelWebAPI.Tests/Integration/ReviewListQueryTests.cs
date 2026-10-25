using System.Net;
using System.Text.Json;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>
/// The review lists and the rating summary on the common list contract, with 45 reviews: every third is pending, every
/// fifth rejected (index 15 and 30 are rejected), the rest published; ratings cycle 1 to 5; groups of three share a date.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewListQueryTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private readonly List<Review> seeded = [];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = PostgresFixture.CreateContext(Connection);
        for (var index = 1; index <= 45; index++)
        {
            var review = new Review
            {
                Id = Guid.CreateVersion7(),
                UserId = null,
                Name = index switch { 7 => "100% Sure", 8 => "Under_score", _ => $"Reviewer {index:00}" },
                Country = index % 2 == 0 ? "Germany" : "Canada",
                CountryCode = index % 2 == 0 ? "DE" : "CA",
                Rating = (short)(index % 5 + 1),
                TripType = index % 2 == 0 ? "Day Trip" : "Safari",
                Comment = index % 10 == 0 ? $"A lovely Jaffna visit number {index}" : $"Comment number {index}",
                ReviewDate = new DateOnly(2026, 6, 1).AddDays((index - 1) / 3),
                Status = index % 5 == 0 ? ReviewStatus.Rejected : index % 3 == 0 ? ReviewStatus.Pending : ReviewStatus.Published,
                CreatedAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero).AddMinutes(index),
            };
            db.Reviews.Add(review);
            seeded.Add(review);
        }

        await db.SaveChangesAsync();
    }

    private IEnumerable<Review> Published => seeded.Where(r => r.Status == ReviewStatus.Published);

    private async Task<JsonElement> PublicAsync(string query = "") => await Json(await Api.GetAsync("/api/reviews" + query));

    private async Task<JsonElement> AdminAsync(string query = "")
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/reviews?all=1" + query);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static List<string> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToList();

    // ---------------------------------------------------------------- the public list

    [Fact]
    public async Task The_public_list_has_only_published_reviews_newest_first_without_an_author()
    {
        var page = await PublicAsync("?pageSize=100");

        Assert.Equal(Published.Count(), page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), r =>
        {
            Assert.Equal("published", r.GetProperty("status").GetString());
            Assert.False(r.TryGetProperty("userId", out _));
        });
        var dates = page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("date").GetString()!).ToList();
        Assert.Equal(dates.OrderByDescending(d => d, StringComparer.Ordinal), dates);
    }

    [Fact]
    public async Task The_public_pages_do_not_overlap_and_a_page_past_the_end_is_empty()
    {
        var seen = new List<string>();
        for (var number = 1; number <= 5; number++)
        {
            seen.AddRange(Ids(await PublicAsync($"?page={number}&pageSize=7")));
        }

        Assert.Equal(Published.Count(), seen.Distinct().Count());
        Assert.Empty(Ids(await PublicAsync("?page=9&pageSize=7")));
    }

    [Theory]
    [InlineData("date")]
    [InlineData("-date")]
    [InlineData("rating")]
    [InlineData("-rating")]
    [InlineData("name")]
    [InlineData("-name")]
    public async Task Every_sort_field_works_in_both_directions_with_stable_pages(string sort)
    {
        var seen = new List<string>();
        for (var number = 1; number <= 2; number++)
        {
            seen.AddRange(Ids(await PublicAsync($"?sort={sort}&page={number}&pageSize=20")));
        }

        Assert.Equal(Published.Count(), seen.Distinct().Count());
        Assert.Equal(seen, Ids(await PublicAsync($"?sort={sort}&pageSize=100")));
    }

    [Fact]
    public async Task Sorting_by_rating_puts_the_best_first()
    {
        var ratings = (await PublicAsync("?sort=-rating&pageSize=100")).GetProperty("items").EnumerateArray().Select(r => r.GetProperty("rating").GetInt32()).ToList();

        Assert.Equal(ratings.OrderByDescending(r => r), ratings);
        Assert.Equal(5, ratings[0]);
    }

    [Fact]
    public async Task The_rating_filter_and_the_rating_facet()
    {
        var page = await PublicAsync("?rating=5&facets=rating&pageSize=100");

        Assert.Equal(Published.Count(r => r.Rating == 5), page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), r => Assert.Equal(5, r.GetProperty("rating").GetInt32()));
        var counts = page.GetProperty("facets").GetProperty("rating");
        foreach (var rating in new[] { 1, 2, 3, 4, 5 })
        {
            Assert.Equal(Published.Count(r => r.Rating == rating), counts.GetProperty(rating.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetInt32());
        }
    }

    [Fact]
    public async Task Public_search_looks_in_the_name_country_trip_type_and_comment()
    {
        Assert.Equal(Published.Count(r => r.Country == "Germany"), (await PublicAsync("?search=germany")).GetProperty("totalCount").GetInt32());
        Assert.Equal(Published.Count(r => r.TripType == "Safari"), (await PublicAsync("?search=SAFARI")).GetProperty("totalCount").GetInt32());
        Assert.Equal(Published.Count(r => r.Comment.Contains("Jaffna", StringComparison.Ordinal)), (await PublicAsync("?search=jaffna")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await PublicAsync("?search=" + Uri.EscapeDataString("100%"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await PublicAsync("?search=" + Uri.EscapeDataString("%%"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await PublicAsync("?search=" + Uri.EscapeDataString("r_s"))).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_wrong_rating_or_paging_value_gets_its_message()
    {
        var response = await Api.GetAsync("/api/reviews?rating=9&page=0&sort=photo");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Rating must be a whole number from 1 to 5", await Field(response, "rating"));
        Assert.Equal("Page must be a whole number of 1 or more", await Field(response, "page"));
        Assert.Equal("Sort by one of: date, rating, name", await Field(response, "sort"));
    }

    // ---------------------------------------------------------------- what the public cannot reach

    [Fact]
    public async Task Customers_and_guests_get_published_reviews_whatever_they_ask_for()
    {
        var guest = await PublicAsync("?all=1&status=pending&pageSize=100");
        var customer = await Json(await Api.GetAsync("/api/reviews?all=1&status=rejected&pageSize=100", CustomerToken));

        Assert.Equal(Published.Count(), guest.GetProperty("totalCount").GetInt32());
        Assert.Equal(Published.Count(), customer.GetProperty("totalCount").GetInt32());
        Assert.All(customer.GetProperty("items").EnumerateArray(), r => Assert.False(r.TryGetProperty("userId", out _)));
    }

    [Fact]
    public async Task The_public_cannot_ask_for_the_status_facet()
    {
        var response = await Api.GetAsync("/api/reviews?all=1&facets=status");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Facets can be: rating", await Field(response, "facets"));
    }

    // ---------------------------------------------------------------- the admin list

    [Fact]
    public async Task The_admin_sees_every_status_with_the_author_field()
    {
        var page = await AdminAsync("&pageSize=100");

        Assert.Equal(45, page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), r => Assert.True(r.TryGetProperty("userId", out _)));
    }

    [Fact]
    public async Task Moderation_tabs_filter_by_status_and_show_every_count()
    {
        var page = await AdminAsync("&status=pending&facets=status,rating&pageSize=100");

        Assert.Equal(seeded.Count(r => r.Status == ReviewStatus.Pending), page.GetProperty("totalCount").GetInt32());
        Assert.All(page.GetProperty("items").EnumerateArray(), r => Assert.Equal("pending", r.GetProperty("status").GetString()));
        var status = page.GetProperty("facets").GetProperty("status");
        Assert.Equal(seeded.Count(r => r.Status == ReviewStatus.Pending), status.GetProperty("pending").GetInt32());
        Assert.Equal(seeded.Count(r => r.Status == ReviewStatus.Published), status.GetProperty("published").GetInt32());
        Assert.Equal(seeded.Count(r => r.Status == ReviewStatus.Rejected), status.GetProperty("rejected").GetInt32());
        // the rating counts follow the status filter (pending only)
        var rating = page.GetProperty("facets").GetProperty("rating");
        Assert.Equal(page.GetProperty("totalCount").GetInt32(), rating.EnumerateObject().Sum(r => r.Value.GetInt32()));
    }

    [Fact]
    public async Task A_wrong_admin_status_gets_its_message_and_the_admin_search_reaches_unpublished_reviews()
    {
        var wrong = await AsAdmin(HttpMethod.Get, "/api/reviews?all=1&status=hidden");
        var rejected = await AdminAsync("&status=rejected&search=reviewer%2015");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("Status must be one of: pending, published, rejected", await Field(wrong, "status"));
        Assert.Equal(1, rejected.GetProperty("totalCount").GetInt32());
    }

    // ---------------------------------------------------------------- the summary

    [Fact]
    public async Task The_summary_counts_only_published_reviews_with_the_average_and_the_distribution()
    {
        var response = await Api.GetAsync("/api/reviews/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await Json(response);
        var published = Published.ToList();
        Assert.Equal(published.Count, summary.GetProperty("count").GetInt32());
        Assert.Equal(decimal.Round((decimal)published.Average(r => r.Rating), 1, MidpointRounding.AwayFromZero), summary.GetProperty("average").GetDecimal());
        var distribution = summary.GetProperty("distribution");
        Assert.Equal(["1", "2", "3", "4", "5"], distribution.EnumerateObject().Select(p => p.Name));
        foreach (var rating in new[] { 1, 2, 3, 4, 5 })
        {
            Assert.Equal(published.Count(r => r.Rating == rating), distribution.GetProperty(rating.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetInt32());
        }
    }

    [Fact]
    public async Task The_summary_of_no_reviews_is_all_zeros()
    {
        await Execute("DELETE FROM reviews");

        var summary = await Json(await Api.GetAsync("/api/reviews/summary"));

        Assert.Equal(0, summary.GetProperty("count").GetInt32());
        Assert.Equal(0m, summary.GetProperty("average").GetDecimal());
        Assert.All(summary.GetProperty("distribution").EnumerateObject(), p => Assert.Equal(0, p.Value.GetInt32()));
    }

    [Fact]
    public async Task The_summary_follows_moderation()
    {
        await Execute("UPDATE reviews SET status = 'rejected'");
        await Execute($"UPDATE reviews SET status = 'published', rating = 5 WHERE id IN (SELECT id FROM reviews ORDER BY id LIMIT 2)");
        await Execute($"UPDATE reviews SET status = 'published', rating = 4 WHERE id IN (SELECT id FROM reviews WHERE status = 'rejected' ORDER BY id LIMIT 1)");

        var summary = await Json(await Api.GetAsync("/api/reviews/summary"));

        Assert.Equal(3, summary.GetProperty("count").GetInt32());
        Assert.Equal(4.7m, summary.GetProperty("average").GetDecimal());
        Assert.Equal(2, summary.GetProperty("distribution").GetProperty("5").GetInt32());
        Assert.Equal(1, summary.GetProperty("distribution").GetProperty("4").GetInt32());
    }

    // ---------------------------------------------------------------- the declaration

    [Fact]
    public void The_database_side_implements_exactly_what_the_admin_definition_declares()
    {
        var (sort, facets) = ReviewRepository.ListNames;

        Assert.Equal(ReviewLists.AdminDefinition.SortFields.Order(), sort.Order());
        Assert.Equal(ReviewLists.AdminDefinition.Facets.Order(), facets.Order());
        Assert.Equal(ReviewLists.PublicDefinition.SortFields.Order(), sort.Order());
    }
}
