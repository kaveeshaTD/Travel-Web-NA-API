using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class PlatformTests(PostgresFixture postgres)
{
    private const string ProductionOrigin = "https://ceylonroads.example";

    private async Task<ApiFactory> ProductionAsync(Dictionary<string, string?>? extra = null)
    {
        var connection = await postgres.CreateDatabaseAsync();
        var settings = ApiFactory.ProductionSettings(connection);
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        return ApiFactory.With(settings, "Production");
    }

    // ---------------------------------------------------------------- health

    [Fact]
    public async Task Health_is_200_and_healthy_when_the_database_answers()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_is_503_and_unhealthy_without_connection_details_when_the_database_is_down()
    {
        await using var factory = ApiFactory.With(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=nope;Username=u;Password=very-secret-password;Timeout=2",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unhealthy", JsonDocument.Parse(text).RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("very-secret-password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- docs

    [Fact]
    public async Task Docs_are_served_in_Development()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection },
            "Development");
        using var client = factory.CreateClient();

        var document = await client.GetAsync("/openapi/v1.json");
        var page = await client.GetAsync("/scalar/v1");
        var swagger = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Contains("openapi", await document.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }

    [Fact]
    public async Task Docs_are_not_served_in_Production()
    {
        await using var factory = await ProductionAsync();
        using var client = factory.CreateClient();

        var document = await client.GetAsync("/openapi/v1.json");
        var page = await client.GetAsync("/scalar/v1");
        var swagger = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
        var body = await page.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, body.GetProperty("error").GetProperty("status").GetInt32());
    }

    // ---------------------------------------------------------------- CORS

    [Fact]
    public async Task A_configured_origin_gets_cors_headers_with_credentials()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_probe/ok");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    [Fact]
    public async Task A_preflight_from_a_configured_origin_is_allowed()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/_probe/echo");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("POST", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Methods")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_origin_gets_no_cors_headers()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_probe/ok");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ---------------------------------------------------------------- JSON

    [Fact]
    public async Task Json_uses_camel_case_iso_dates_and_wire_enums()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<JsonElement>("/api/_probe/sample");

        Assert.Equal("2026-03-10", body.GetProperty("travelDate").GetString());
        Assert.StartsWith("2026-03-10T08:30:00", body.GetProperty("createdAt").GetString(), StringComparison.Ordinal);
        Assert.Equal("password-reset", body.GetProperty("emailKind").GetString());
        Assert.Equal("pending", body.GetProperty("status").GetString());
        Assert.Equal("bank_transfer", body.GetProperty("method").GetString());
        Assert.Equal("LKR", body.GetProperty("currency").GetString());
    }

    // ---------------------------------------------------------------- logging and correlation id

    [Fact]
    public async Task Every_response_carries_a_correlation_id_and_a_safe_caller_id_is_kept()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var generated = await client.GetAsync("/api/_probe/ok");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_probe/ok");
        request.Headers.Add("X-Correlation-Id", "web-12345");
        var echoed = await client.SendAsync(request);
        using var unsafeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/_probe/ok");
        unsafeRequest.Headers.Add("X-Correlation-Id", "bad id with spaces");
        var replaced = await client.SendAsync(unsafeRequest);

        Assert.NotEmpty(Assert.Single(generated.Headers.GetValues("X-Correlation-Id")));
        Assert.Equal("web-12345", Assert.Single(echoed.Headers.GetValues("X-Correlation-Id")));
        Assert.NotEqual("bad id with spaces", Assert.Single(replaced.Headers.GetValues("X-Correlation-Id")));
    }

    [Fact]
    public async Task Each_request_is_logged_with_method_path_status_and_time_but_never_the_query_string()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        await client.GetAsync("/api/_probe/ok?token=super-secret-token&password=hunter2");

        var entry = Assert.Single(factory.Logs.Entries, e => e.Message.Contains("/api/_probe/ok\" responded 200", StringComparison.Ordinal));
        Assert.Contains("GET", entry.Message, StringComparison.Ordinal);
        Assert.Contains(" ms", entry.Message, StringComparison.Ordinal);
        Assert.All(factory.Logs.Entries, e =>
        {
            Assert.DoesNotContain("super-secret-token", e.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", e.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Log_entries_of_a_request_share_its_correlation_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/_probe/boom");
        request.Headers.Add("X-Correlation-Id", "trace-abc-1");

        await client.SendAsync(request);

        var errors = factory.Logs.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.Contains(errors, e => e.Exception is InvalidOperationException);
        Assert.All(errors, e => Assert.Equal("trace-abc-1", e.Properties["CorrelationId"]));
    }

    // ---------------------------------------------------------------- configuration

    [Fact]
    public async Task Production_refuses_to_start_when_required_settings_are_missing_and_names_them()
    {
        await using var factory = ApiFactory.With(new Dictionary<string, string?>(), "Production");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Default", ex.ToString(), StringComparison.Ordinal);
        Assert.Contains("Jwt:Key", ex.ToString(), StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Production_refuses_a_wildcard_origin()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var settings = ApiFactory.ProductionSettings(connection);
        settings["Cors:AllowedOrigins:0"] = "*";
        await using var factory = ApiFactory.With(settings, "Production");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Cors:AllowedOrigins", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_refuses_a_placeholder_secret_without_printing_it()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var settings = ApiFactory.ProductionSettings(connection);
        settings["Jwt:Key"] = "CHANGE_ME-CHANGE_ME-CHANGE_ME-CHANGE_ME";
        await using var factory = ApiFactory.With(settings, "Production");

        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:Key", ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CHANGE_ME-CHANGE_ME", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_starts_with_all_required_settings()
    {
        await using var factory = await ProductionAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_starts_without_email_or_jwt_settings()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection },
            "Development");

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    // ---------------------------------------------------------------- database start-up

    [Fact]
    public async Task Production_does_not_create_demo_accounts_by_default()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(ApiFactory.ProductionSettings(connection), "Production");
        using var client = factory.CreateClient();

        await client.GetAsync("/api/health");

        Assert.Equal(0, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task Development_migrates_and_seeds_when_seeding_is_enabled()
    {
        var connection = await postgres.CreateDatabaseAsync(migrate: false);
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connection,
                ["Seed:Enabled"] = "true",
                ["Seed:IncludeSampleActivity"] = "false",
            },
            "Development");

        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.True(await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM users") >= 5);
        Assert.True(await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM packages") > 0);
        await using var db = PostgresFixture.CreateContext(connection);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
