using System.Net;
using System.Text.Json;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>What all six content collections have in common, checked against each of them through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ContentCollectionTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    public static TheoryData<string> Resources => new() { "packages", "destinations", "vehicles", "services", "faqs", "gallery" };

    private static object Valid(string resource, string label) => resource switch
    {
        "packages" => Package(label),
        "destinations" => Destination(label),
        "vehicles" => Vehicle(label),
        "services" => Service(label),
        "faqs" => Faq(label),
        "gallery" => Gallery(label),
        _ => throw new ArgumentOutOfRangeException(nameof(resource)),
    };

    /// <summary>One change to a valid body that is easy to read back: the property name, the new value and its JSON text.</summary>
    private static (object Patch, string Property, string Expected) Change(string resource) => resource switch
    {
        "packages" => (new { featured = true }, "featured", "True"),
        "destinations" => (new { region = "Changed region" }, "region", "Changed region"),
        "vehicles" => (new { ac = false }, "ac", "False"),
        "services" => (new { icon = "sun" }, "icon", "sun"),
        "faqs" => (new { answer = "A different answer." }, "answer", "A different answer."),
        "gallery" => (new { alt = "A different description" }, "alt", "A different description"),
        _ => throw new ArgumentOutOfRangeException(nameof(resource)),
    };

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();

    // ---------------------------------------------------------------- access

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Anyone_can_read_the_list_and_one_record(string resource)
    {
        var created = await CreateAsync(resource, Valid(resource, "Alpha item"));
        var id = created.GetProperty("id").GetString();

        var list = await Api.GetAsync($"/api/{resource}");
        var one = await Api.GetAsync($"/api/{resource}/{id}");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single((await Json(list)).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(id, (await Json(one)).GetProperty("id").GetString());
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task An_empty_collection_is_an_empty_array(string resource)
    {
        var response = await Api.GetAsync($"/api/{resource}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await Json(response)).EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Writes_need_a_token_and_the_admin_role(string resource)
    {
        var created = await CreateAsync(resource, Valid(resource, "Alpha item"));
        var path = $"/api/{resource}/{created.GetProperty("id").GetString()}";
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, $"/api/{resource}", Valid(resource, "Beta item")),
            (HttpMethod.Put, path, new { }),
            (HttpMethod.Delete, path, null),
            (HttpMethod.Post, $"/api/{resource}/reorder", new { ids = new[] { "alpha-item" } }),
        };

        foreach (var (method, requestPath, body) in attempts)
        {
            var anonymous = await Api.SendAsync(method, requestPath, body);
            var customer = await Api.SendAsync(method, requestPath, body, bearer: CustomerToken);

            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, customer.StatusCode);
        }

        Assert.Equal(["alpha-item"], await IdsAsync(resource, all: true));
    }

    // ---------------------------------------------------------------- create

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Create_is_201_with_a_location_a_slug_id_and_goes_last(string resource)
    {
        await CreateAsync(resource, Valid(resource, "Alpha item"));

        var response = await AsAdmin(HttpMethod.Post, $"/api/{resource}", Valid(resource, "Beta item"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/{resource}/beta-item", response.Headers.Location?.ToString());
        Assert.Equal("beta-item", (await Json(response)).GetProperty("id").GetString());
        Assert.Equal(["alpha-item", "beta-item"], await IdsAsync(resource));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task A_clashing_name_gets_a_numbered_id(string resource)
    {
        await CreateAsync(resource, Valid(resource, "Alpha item"));
        await CreateAsync(resource, Valid(resource, "Alpha item"));
        await CreateAsync(resource, Valid(resource, "Alpha item"));

        Assert.Equal(["alpha-item", "alpha-item-2", "alpha-item-3"], await IdsAsync(resource));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task A_missing_body_is_400_in_the_uniform_shape(string resource)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/{resource}") { Content = Raw(string.Empty) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AdminToken);
        var response = await Api.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AuthApi.AssertError(await Json(response), HttpStatusCode.BadRequest);
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task An_empty_object_reports_the_required_fields(string resource)
    {
        var response = await AsAdmin(HttpMethod.Post, $"/api/{resource}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await Json(response)).GetProperty("error").GetProperty("fields");
        Assert.True(fields.EnumerateObject().Any());
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM {(resource == "gallery" ? "gallery_images" : resource)}"));
    }

    // ---------------------------------------------------------------- update

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task An_update_with_one_field_keeps_every_other_field(string resource)
    {
        var created = await CreateAsync(resource, Valid(resource, "Alpha item"));
        var (patch, property, expected) = Change(resource);

        var response = await AsAdmin(HttpMethod.Put, $"/api/{resource}/alpha-item", patch);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await Json(response);
        Assert.Equal(expected, Text(updated.GetProperty(property)));
        foreach (var field in created.EnumerateObject().Where(f => f.Name != property))
        {
            Assert.True(SameJson(field.Value, updated.GetProperty(field.Name)), $"{field.Name} changed");
        }

        var stored = await Json(await Api.GetAsync($"/api/{resource}/alpha-item"));
        Assert.True(SameJson(updated, stored));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task An_invalid_merged_record_changes_nothing(string resource)
    {
        await CreateAsync(resource, Valid(resource, "Alpha item"));
        var before = await Json(await Api.GetAsync($"/api/{resource}/alpha-item"));
        object bad = resource switch
        {
            "packages" => new { durationDays = 90 },
            "destinations" => new { lat = 12 },
            "vehicles" => new { seats = 0 },
            "services" => new { icon = "rocket" },
            "faqs" => new { answer = "no" },
            _ => new { aspect = 9 },
        };

        var response = await AsAdmin(HttpMethod.Put, $"/api/{resource}/alpha-item", bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await Json(await Api.GetAsync($"/api/{resource}/alpha-item"));
        Assert.True(SameJson(before, after));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task The_id_does_not_change_when_the_name_does(string resource)
    {
        await CreateAsync(resource, Valid(resource, "Alpha item"));
        object rename = resource switch
        {
            "services" => new { title = "Completely new title" },
            "faqs" => new { question = "A completely new question?" },
            "gallery" => new { caption = "A completely new caption" },
            _ => new { name = "Completely new name" },
        };

        var response = await AsAdmin(HttpMethod.Put, $"/api/{resource}/alpha-item", rename);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("alpha-item", (await Json(response)).GetProperty("id").GetString());
    }

    // ---------------------------------------------------------------- not found

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task An_unknown_id_is_404_for_read_update_and_delete(string resource)
    {
        var read = await Api.GetAsync($"/api/{resource}/nope");
        var update = await AsAdmin(HttpMethod.Put, $"/api/{resource}/nope", new { });
        var delete = await AsAdmin(HttpMethod.Delete, $"/api/{resource}/nope");

        foreach (var response in new[] { read, update, delete })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var message = (await Json(response)).GetProperty("error").GetProperty("message").GetString();
            Assert.EndsWith(" not found", message, StringComparison.Ordinal);
            Assert.True(char.IsUpper(message![0]));
        }
    }

    // ---------------------------------------------------------------- delete

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Delete_is_204_and_a_second_delete_is_404(string resource)
    {
        await CreateAsync(resource, Valid(resource, "Alpha item"));

        var first = await AsAdmin(HttpMethod.Delete, $"/api/{resource}/alpha-item");
        var second = await AsAdmin(HttpMethod.Delete, $"/api/{resource}/alpha-item");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Empty(await first.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Empty(await IdsAsync(resource, all: true));
    }

    // ---------------------------------------------------------------- reorder

    private async Task<string[]> CreateThreeAsync(string resource)
    {
        foreach (var label in new[] { "Alpha item", "Beta item", "Gamma item" })
        {
            await CreateAsync(resource, Valid(resource, label));
        }

        return ["alpha-item", "beta-item", "gamma-item"];
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Reorder_stores_the_given_order_and_returns_the_list(string resource)
    {
        await CreateThreeAsync(resource);

        var response = await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { ids = new[] { "gamma-item", "alpha-item", "beta-item" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["gamma-item", "alpha-item", "beta-item"], (await Json(response)).EnumerateArray().Select(x => x.GetProperty("id").GetString()!));
        Assert.Equal(["gamma-item", "alpha-item", "beta-item"], await IdsAsync(resource));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Reorder_with_a_short_list_puts_those_first_and_keeps_the_rest_in_order(string resource)
    {
        await CreateThreeAsync(resource);

        var response = await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { ids = new[] { "gamma-item" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["gamma-item", "alpha-item", "beta-item"], await IdsAsync(resource));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Reorder_with_an_empty_list_is_not_treated_as_a_record_called_reorder(string resource)
    {
        await CreateThreeAsync(resource);

        var response = await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { ids = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await Json(response)).GetArrayLength());
        Assert.Equal(["alpha-item", "beta-item", "gamma-item"], await IdsAsync(resource));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Reorder_rejects_unknown_repeated_and_missing_ids_and_changes_nothing(string resource)
    {
        await CreateThreeAsync(resource);

        var unknown = await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { ids = new[] { "gamma-item", "nope" } });
        var repeated = await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { ids = new[] { "gamma-item", "gamma-item" } });
        var missing = await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { });

        foreach (var response in new[] { unknown, repeated, missing })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.NotNull(await Field(response, "ids"));
        }

        Assert.Contains("nope", await Field(unknown, "ids"), StringComparison.Ordinal);
        Assert.Equal(["alpha-item", "beta-item", "gamma-item"], await IdsAsync(resource));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task A_record_created_after_a_reorder_goes_to_the_end(string resource)
    {
        await CreateThreeAsync(resource);
        await AsAdmin(HttpMethod.Post, $"/api/{resource}/reorder", new { ids = new[] { "gamma-item", "beta-item", "alpha-item" } });

        await CreateAsync(resource, Valid(resource, "Delta item"));

        Assert.Equal(["gamma-item", "beta-item", "alpha-item", "delta-item"], await IdsAsync(resource));
    }
}
