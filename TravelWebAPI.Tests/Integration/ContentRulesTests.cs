using System.Net;
using System.Text.Json;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The rules that belong to one resource: package routes and visibility, delete protection, defaults and image values.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentRulesTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private async Task<(string First, string Second, string Third)> CreateDestinationsAsync()
    {
        var first = await CreateAsync("destinations", Destination("Sigiriya"));
        var second = await CreateAsync("destinations", Destination("Kandy"));
        var third = await CreateAsync("destinations", Destination("Galle"));
        return (first.GetProperty("id").GetString()!, second.GetProperty("id").GetString()!, third.GetProperty("id").GetString()!);
    }

    private async Task<JsonElement> GetPackageAsync(string id, string? token = null)
    {
        var response = await Api.GetAsync($"/api/packages/{id}", token ?? AdminToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Json(response);
    }

    // ---------------------------------------------------------------- package shape

    [Fact]
    public async Task A_package_has_its_route_in_order_with_destination_names()
    {
        var (sigiriya, kandy, galle) = await CreateDestinationsAsync();

        var package = await CreateAsync("packages", Package(destinationIds: [kandy, galle, sigiriya]));

        Assert.Equal([kandy, galle, sigiriya], Strings(package.GetProperty("destinationIds")));
        Assert.Equal(["Kandy", "Galle", "Sigiriya"], Strings(package.GetProperty("route")));
        Assert.Equal(["Climb Sigiriya", "Visit Dambulla"], Strings(package.GetProperty("highlights")));
        Assert.Equal(["culture", "nature"], Strings(package.GetProperty("interests")));
        Assert.Equal(["img:temple", "/uploads/3f2a.jpg"], Strings(package.GetProperty("gallery")));
        Assert.Equal(["Driver and fuel"], Strings(package.GetProperty("included")));
        Assert.Equal(["Entrance tickets"], Strings(package.GetProperty("excluded")));
        Assert.Equal(120000m, package.GetProperty("priceLkr").GetDecimal());
        Assert.False(package.TryGetProperty("sortOrder", out _));
        Assert.False(package.TryGetProperty("createdAt", out _));
    }

    [Fact]
    public async Task Itinerary_days_are_numbered_from_their_position()
    {
        var package = await CreateAsync("packages", Package());

        var days = package.GetProperty("itinerary").EnumerateArray().ToList();

        Assert.Equal([1, 2], days.Select(d => d.GetProperty("day").GetInt32()));
        Assert.Equal(["Arrival", "Sigiriya"], days.Select(d => d.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task A_destination_rename_shows_in_the_package_route()
    {
        var (sigiriya, _, _) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya]));

        await AsAdmin(HttpMethod.Put, $"/api/destinations/{sigiriya}", new { name = "Sigiriya Rock" });

        Assert.Equal(["Sigiriya Rock"], Strings((await GetPackageAsync("cultural-triangle")).GetProperty("route")));
    }

    [Fact]
    public async Task An_unknown_or_repeated_destination_in_the_route_is_400()
    {
        var (sigiriya, _, _) = await CreateDestinationsAsync();

        var unknown = await AsAdmin(HttpMethod.Post, "/api/packages", Package(destinationIds: [sigiriya, "nope", "nada"]));
        var repeated = await AsAdmin(HttpMethod.Post, "/api/packages", Package(destinationIds: [sigiriya, sigiriya]));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Unknown destinations: nope, nada", await Field(unknown, "destinationIds"));
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Equal("A destination can appear only once in the route", await Field(repeated, "destinationIds"));
        Assert.Empty(await IdsAsync("packages", all: true));
    }

    [Fact]
    public async Task Several_bad_package_fields_are_reported_together()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/packages", Package(extra: new { name = "A", priceLkr = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter a package name", await Field(response, "name"));
        Assert.Equal("Enter a price greater than 0", await Field(response, "priceLkr"));
    }

    // ---------------------------------------------------------------- publishing

    [Fact]
    public async Task Unpublished_packages_are_hidden_from_everyone_but_admins()
    {
        await CreateAsync("packages", Package("Live package"));
        await CreateAsync("packages", Package("Draft package", extra: new { active = false }));

        var visitor = await Api.GetAsync("/api/packages");
        var visitorAll = await Api.GetAsync("/api/packages?all=1");
        var customerAll = await Api.GetAsync("/api/packages?all=1", CustomerToken);
        var adminPlain = await Api.GetAsync("/api/packages", AdminToken);
        var adminAll = await Api.GetAsync("/api/packages?all=1", AdminToken);

        Assert.Equal(["live-package"], (await Json(visitor)).EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["live-package"], (await Json(visitorAll)).EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["live-package"], (await Json(customerAll)).EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["live-package"], (await Json(adminPlain)).EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(["live-package", "draft-package"], (await Json(adminAll)).EnumerateArray().Select(p => p.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task An_unpublished_package_by_id_is_404_except_for_admins()
    {
        await CreateAsync("packages", Package("Draft package", extra: new { active = false }));

        var visitor = await Api.GetAsync("/api/packages/draft-package");
        var customer = await Api.GetAsync("/api/packages/draft-package", CustomerToken);
        var admin = await Api.GetAsync("/api/packages/draft-package", AdminToken);

        Assert.Equal(HttpStatusCode.NotFound, visitor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, customer.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.False((await Json(admin)).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Publishing_a_draft_makes_it_public()
    {
        await CreateAsync("packages", Package("Draft package", extra: new { active = false }));

        await AsAdmin(HttpMethod.Put, "/api/packages/draft-package", new { active = true });

        Assert.Equal(["draft-package"], await IdsAsync("packages"));
    }

    [Fact]
    public async Task Admin_reorder_covers_unpublished_packages_too()
    {
        await CreateAsync("packages", Package("One package"));
        await CreateAsync("packages", Package("Two draft", extra: new { active = false }));
        await CreateAsync("packages", Package("Three package"));

        var response = await AsAdmin(HttpMethod.Post, "/api/packages/reorder", new { ids = new[] { "two-draft", "three-package", "one-package" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["two-draft", "three-package", "one-package"], await IdsAsync("packages", all: true));
        Assert.Equal(["three-package", "one-package"], await IdsAsync("packages"));
    }

    // ---------------------------------------------------------------- replacing children

    [Fact]
    public async Task Changing_the_route_replaces_it_completely_even_when_stops_swap_places()
    {
        var (sigiriya, kandy, galle) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya, kandy, galle]));

        var swapped = await AsAdmin(HttpMethod.Put, "/api/packages/cultural-triangle", new { destinationIds = new[] { kandy, sigiriya } });
        var shorter = await AsAdmin(HttpMethod.Put, "/api/packages/cultural-triangle", new { destinationIds = new[] { galle } });

        Assert.Equal(HttpStatusCode.OK, swapped.StatusCode);
        Assert.Equal([kandy, sigiriya], Strings((await Json(swapped)).GetProperty("destinationIds")));
        Assert.Equal(HttpStatusCode.OK, shorter.StatusCode);
        Assert.Equal([galle], Strings((await GetPackageAsync("cultural-triangle")).GetProperty("destinationIds")));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM package_destinations"));
        Assert.Equal(0, await Scalar<int>("SELECT position FROM package_destinations"));
    }

    [Fact]
    public async Task The_route_can_be_emptied()
    {
        var (sigiriya, _, _) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya]));

        var response = await AsAdmin(HttpMethod.Put, "/api/packages/cultural-triangle", new { destinationIds = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).GetProperty("destinationIds").EnumerateArray());
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM package_destinations"));
    }

    [Fact]
    public async Task Replacing_lists_changes_only_what_was_sent()
    {
        await CreateAsync("packages", Package());
        var itineraryBefore = await Scalar<string>("SELECT string_agg(id::text, ',' ORDER BY day) FROM itinerary_days");

        var response = await AsAdmin(HttpMethod.Put, "/api/packages/cultural-triangle", new
        {
            highlights = new[] { "New highlight" },
            interests = new[] { "beach" },
            gallery = new[] { "img:beach" },
            itinerary = new[] { new { title = "Only day", description = "Relax" } },
            included = new[] { "Water" },
            excluded = Array.Empty<string>(),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var package = await GetPackageAsync("cultural-triangle");
        Assert.Equal(["New highlight"], Strings(package.GetProperty("highlights")));
        Assert.Equal(["beach"], Strings(package.GetProperty("interests")));
        Assert.Equal(["img:beach"], Strings(package.GetProperty("gallery")));
        Assert.Equal(["Water"], Strings(package.GetProperty("included")));
        Assert.Empty(package.GetProperty("excluded").EnumerateArray());
        Assert.Equal(1, package.GetProperty("itinerary").GetArrayLength());
        Assert.Equal("Only day", package.GetProperty("itinerary")[0].GetProperty("title").GetString());
        Assert.NotEqual(itineraryBefore, await Scalar<string>("SELECT string_agg(id::text, ',' ORDER BY day) FROM itinerary_days"));
    }

    [Fact]
    public async Task Changing_a_scalar_leaves_the_child_rows_untouched()
    {
        var (sigiriya, _, _) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya]));
        var before = await Scalar<string>(ChildFingerprint);

        await AsAdmin(HttpMethod.Put, "/api/packages/cultural-triangle", new { featured = true, tagline = "A new tagline" });

        Assert.Equal(before, await Scalar<string>(ChildFingerprint));
        Assert.Equal("A new tagline", (await GetPackageAsync("cultural-triangle")).GetProperty("tagline").GetString());
    }

    private const string ChildFingerprint = """
        SELECT concat_ws('|',
          (SELECT string_agg(id::text, ',' ORDER BY id) FROM itinerary_days),
          (SELECT string_agg(id::text, ',' ORDER BY id) FROM package_images),
          (SELECT string_agg(id::text, ',' ORDER BY id) FROM package_items),
          (SELECT string_agg(destination_id || position, ',' ORDER BY position) FROM package_destinations),
          (SELECT string_agg(interest, ',' ORDER BY interest) FROM package_interests))
        """;

    [Fact]
    public async Task A_failed_update_keeps_the_old_children()
    {
        var (sigiriya, _, _) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya]));
        var before = await Scalar<string>(ChildFingerprint);

        var response = await AsAdmin(HttpMethod.Put, "/api/packages/cultural-triangle", new
        {
            destinationIds = new[] { "nope" },
            highlights = new[] { "Would replace" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await Scalar<string>(ChildFingerprint));
    }

    // ---------------------------------------------------------------- delete protection

    [Fact]
    public async Task Deleting_a_package_removes_all_its_child_rows()
    {
        var (sigiriya, kandy, _) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya, kandy]));

        var response = await AsAdmin(HttpMethod.Delete, "/api/packages/cultural-triangle");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var table in new[] { "package_destinations", "itinerary_days", "package_interests", "package_images", "package_items" })
        {
            Assert.Equal(0L, await Scalar<long>($"SELECT count(*) FROM {table}"));
        }

        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM destinations"));
    }

    [Fact]
    public async Task A_destination_used_by_a_package_cannot_be_deleted_until_the_package_is_gone()
    {
        var (sigiriya, _, _) = await CreateDestinationsAsync();
        await CreateAsync("packages", Package(destinationIds: [sigiriya]));

        var blocked = await AsAdmin(HttpMethod.Delete, $"/api/destinations/{sigiriya}");

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var error = (await Json(blocked)).GetProperty("error");
        Assert.Equal("This destination is still used by: package \"Cultural Triangle\"", error.GetProperty("message").GetString());
        Assert.Equal(["package \"Cultural Triangle\""], Strings(error.GetProperty("fields").GetProperty("dependents")));
        Assert.Contains(sigiriya, await IdsAsync("destinations"));

        await AsAdmin(HttpMethod.Delete, "/api/packages/cultural-triangle");
        var allowed = await AsAdmin(HttpMethod.Delete, $"/api/destinations/{sigiriya}");

        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
    }

    [Fact]
    public async Task A_service_package_or_vehicle_with_bookings_cannot_be_deleted()
    {
        await CreateAsync("services", Service());
        await CreateAsync("packages", Package());
        await CreateAsync("vehicles", Vehicle());
        await AddBookingAsync("airport-transfer", "cultural-triangle", "toyota-prius", "BK-ABC123");
        await AddBookingAsync("airport-transfer", reference: "BK-XYZ789");

        var service = await AsAdmin(HttpMethod.Delete, "/api/services/airport-transfer");
        var package = await AsAdmin(HttpMethod.Delete, "/api/packages/cultural-triangle");
        var vehicle = await AsAdmin(HttpMethod.Delete, "/api/vehicles/toyota-prius");

        foreach (var response in new[] { service, package, vehicle })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        var serviceDependents = Strings((await Json(service)).GetProperty("error").GetProperty("fields").GetProperty("dependents"));
        Assert.Equal(["booking BK-ABC123", "booking BK-XYZ789"], serviceDependents.Order());
        Assert.Equal(["booking BK-ABC123"], Strings((await Json(package)).GetProperty("error").GetProperty("fields").GetProperty("dependents")));
        Assert.Equal(["booking BK-ABC123"], Strings((await Json(vehicle)).GetProperty("error").GetProperty("fields").GetProperty("dependents")));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM services"));
    }

    [Fact]
    public async Task At_most_twenty_dependents_are_listed()
    {
        await CreateAsync("services", Service());
        for (var number = 0; number < 25; number++)
        {
            await AddBookingAsync("airport-transfer", reference: $"BK-{number:D6}");
        }

        var response = await AsAdmin(HttpMethod.Delete, "/api/services/airport-transfer");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(20, (await Json(response)).GetProperty("error").GetProperty("fields").GetProperty("dependents").GetArrayLength());
    }

    // ---------------------------------------------------------------- destinations, vehicles, services, gallery

    [Fact]
    public async Task A_destination_keeps_its_images_in_order_and_rejects_bad_coordinates()
    {
        var created = await CreateAsync("destinations", Destination("Ella", new { images = new[] { "img:ella", "https://cdn.example.com/b.jpg", "img:tea" } }));
        var bad = await AsAdmin(HttpMethod.Post, "/api/destinations", Destination("Far away", new { lat = 12, lng = 70 }));

        Assert.Equal(["img:ella", "https://cdn.example.com/b.jpg", "img:tea"], Strings(created.GetProperty("images")));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("Latitude must be between 5 and 10 (Sri Lanka)", await Field(bad, "lat"));
        Assert.Equal("Longitude must be between 79 and 82.5 (Sri Lanka)", await Field(bad, "lng"));
    }

    [Fact]
    public async Task A_vehicle_type_outside_the_list_is_rejected_and_prices_are_in_lkr()
    {
        var created = await CreateAsync("vehicles", Vehicle());
        var bad = await AsAdmin(HttpMethod.Post, "/api/vehicles", Vehicle("Big bus", new { type = "bus" }));

        Assert.Equal(30m, created.GetProperty("pricePerKmLkr").GetDecimal());
        Assert.Equal(25000m, created.GetProperty("pricePerDayLkr").GetDecimal());
        Assert.False(created.TryGetProperty("pricePerKmUsd", out _));
        Assert.Equal("Type must be one of: car, van, minicoach", await Field(bad, "type"));
    }

    [Fact]
    public async Task Service_details_are_replaced_as_a_list_and_kept_when_not_sent()
    {
        await CreateAsync("services", Service());

        await AsAdmin(HttpMethod.Put, "/api/services/airport-transfer", new { title = "Airport pick-up" });
        var kept = await Json(await Api.GetAsync("/api/services/airport-transfer"));
        var replaced = await Json(await AsAdmin(HttpMethod.Put, "/api/services/airport-transfer", new { details = new[] { "One", "Two", "Three" } }));

        Assert.Equal(["Meet and greet", "Flight tracking"], Strings(kept.GetProperty("details")));
        Assert.Equal(["One", "Two", "Three"], Strings(replaced.GetProperty("details")));
        Assert.Equal(3L, await Scalar<long>("SELECT count(*) FROM service_details"));
    }

    [Fact]
    public async Task A_gallery_image_defaults_its_aspect_and_checks_image_values()
    {
        var withoutAspect = new Dictionary<string, object> { ["src"] = "/uploads/3f2a.jpg", ["alt"] = "A photo of the coast", ["caption"] = "The coast" };
        var created = await CreateAsync("gallery", withoutAspect);

        Assert.Equal(1.5m, created.GetProperty("aspect").GetDecimal());
        Assert.Equal("/uploads/3f2a.jpg", created.GetProperty("src").GetString());

        foreach (var unsafeValue in new[] { "javascript:alert(1)", "data:image/png;base64,AAAA", "/uploads/../secret.txt", "ftp://example.com/a.jpg", "just text" })
        {
            var response = await AsAdmin(HttpMethod.Post, "/api/gallery", Gallery("Bad one", new { src = unsafeValue }));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Choose an image", await Field(response, "src"));
        }

        Assert.Single(await IdsAsync("gallery"));
    }

    [Fact]
    public async Task Text_is_trimmed_and_blank_list_entries_are_dropped()
    {
        var created = await CreateAsync("services", Service("  Padded title  ", new { details = new[] { "  one  ", "", "   ", "two" } }));

        Assert.Equal("Padded title", created.GetProperty("title").GetString());
        Assert.Equal("padded-title", created.GetProperty("id").GetString());
        Assert.Equal(["one", "two"], Strings(created.GetProperty("details")));
    }

    [Fact]
    public async Task Names_without_letters_still_get_an_id_and_the_reserved_word_is_avoided()
    {
        var symbols = await CreateAsync("faqs", Faq("?????????"));
        var reserved = await CreateAsync("gallery", Gallery("Reorder"));

        Assert.Equal("item", symbols.GetProperty("id").GetString());
        Assert.Equal("reorder-2", reserved.GetProperty("id").GetString());
    }
}
