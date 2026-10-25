using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

public sealed class ErrorContractTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ErrorContractTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static async Task<JsonElement> ReadErrorAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = root.GetProperty("error");
        Assert.Equal((int)expected, error.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        return error;
    }

    [Fact]
    public async Task Validation_errors_are_400_and_name_each_field()
    {
        var error = await ReadErrorAsync(await _client.GetAsync("/api/_probe/validation"), HttpStatusCode.BadRequest);

        Assert.Equal("Validation failed", error.GetProperty("message").GetString());
        Assert.Equal("Enter a valid email address", error.GetProperty("fields").GetProperty("email").GetString());
        Assert.Equal("Use at least 8 characters", error.GetProperty("fields").GetProperty("password").GetString());
    }

    [Fact]
    public async Task Not_signed_in_is_401_and_not_allowed_is_403()
    {
        await ReadErrorAsync(await _client.GetAsync("/api/_probe/unauthorized"), HttpStatusCode.Unauthorized);
        await ReadErrorAsync(await _client.GetAsync("/api/_probe/forbidden"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Empty_401_and_403_responses_get_the_uniform_body()
    {
        var unauthorized = await ReadErrorAsync(await _client.GetAsync("/api/_probe/empty-401"), HttpStatusCode.Unauthorized);
        var forbidden = await ReadErrorAsync(await _client.GetAsync("/api/_probe/empty-403"), HttpStatusCode.Forbidden);

        Assert.Equal("Sign in required", unauthorized.GetProperty("message").GetString());
        Assert.Equal("You are not allowed to do this", forbidden.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unknown_record_and_unknown_route_are_404_json()
    {
        var record = await ReadErrorAsync(await _client.GetAsync("/api/_probe/not-found"), HttpStatusCode.NotFound);
        var route = await ReadErrorAsync(await _client.GetAsync("/api/does-not-exist"), HttpStatusCode.NotFound);

        Assert.Equal("Package 'nope' was not found", record.GetProperty("message").GetString());
        Assert.Equal("Not found", route.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Wrong_method_on_a_known_path_is_405_json()
    {
        var error = await ReadErrorAsync(await _client.PostAsync("/api/_probe/only-put", null), HttpStatusCode.MethodNotAllowed);

        Assert.Equal("Method not allowed", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Conflicts_are_409_and_list_dependents()
    {
        var duplicate = await ReadErrorAsync(await _client.GetAsync("/api/_probe/conflict"), HttpStatusCode.Conflict);
        var dependents = await ReadErrorAsync(await _client.DeleteAsync("/api/_probe/dependents"), HttpStatusCode.Conflict);

        Assert.Equal("Already in use", duplicate.GetProperty("fields").GetProperty("email").GetString());
        var listed = dependents.GetProperty("fields").GetProperty("dependents").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(["cultural-triangle", "hill-country"], listed);
        Assert.Equal("This Destination is still used by: cultural-triangle, hill-country", dependents.GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_oversized_file_is_413_and_a_duplicate_key_is_409()
    {
        var large = await ReadErrorAsync(await _client.GetAsync("/api/_probe/too-large"), HttpStatusCode.RequestEntityTooLarge);
        var duplicate = await ReadErrorAsync(await _client.PostAsync("/api/_probe/duplicate-key", null), HttpStatusCode.Conflict);

        Assert.Equal("Request body is too large", large.GetProperty("message").GetString());
        Assert.False(large.TryGetProperty("fields", out _));
        Assert.Equal("That record already exists, please try again", duplicate.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unexpected_errors_are_a_generic_500_and_logged_with_the_correlation_id()
    {
        var response = await _client.GetAsync("/api/_probe/boom");
        var body = await response.Content.ReadAsStringAsync();

        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        Assert.Equal(500, error.GetProperty("status").GetInt32());
        Assert.Equal("Something went wrong", error.GetProperty("message").GetString());
        Assert.False(error.TryGetProperty("fields", out _));
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);

        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        var logged = Assert.Single(_factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException);
        Assert.Contains("hunter2", logged.Exception!.Message, StringComparison.Ordinal);
        Assert.NotEmpty(correlationId);
    }

    [Fact]
    public async Task Invalid_model_gives_400_with_camel_case_field_names()
    {
        var response = await _client.PostAsJsonAsync("/api/_probe/model", new { email = "not-an-email", adults = 0 });

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("Validation failed", error.GetProperty("message").GetString());
        var fields = error.GetProperty("fields");
        Assert.Equal("Enter a valid email address", fields.GetProperty("email").GetString());
        Assert.Equal("Adults must be between 1 and 20", fields.GetProperty("adults").GetString());
    }

    [Fact]
    public async Task Malformed_json_is_400_without_parser_details()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/_probe/echo", content);

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("The request body is not valid JSON", error.GetProperty("message").GetString());
        Assert.False(error.TryGetProperty("fields", out _));
    }

    [Fact]
    public async Task An_unknown_enum_value_names_the_field()
    {
        using var content = new StringContent("""{"email":"a@b.co","adults":2,"status":"banana"}""", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/_probe/echo", content);

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("Invalid value", error.GetProperty("fields").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Success_status_codes_match_the_outcome()
    {
        var created = await _client.PostAsync("/api/_probe/created", null);
        var read = await _client.GetAsync("/api/_probe/ok");
        var deleted = await _client.DeleteAsync("/api/_probe/no-content");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/api/_probe/created/42", created.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await deleted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Oversized_bodies_are_413_json()
    {
        await using var factory = ApiFactory.With(new Dictionary<string, string?> { ["Limits:MaxRequestBodyBytes"] = "100" });
        using var client = factory.CreateClient();
        using var content = new StringContent(new string('x', 500), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/_probe/echo", content);

        var error = await ReadErrorAsync(response, HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal("Request body is too large", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_client_that_cancels_is_not_logged_as_an_error()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("/api/_probe/slow", cts.Token));
        await Task.Delay(500);

        Assert.DoesNotContain(factory.Logs.Entries, e => e.Level >= LogLevel.Error);
    }
}
