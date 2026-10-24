using System.Net;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

public sealed class AccessControlTests : IClassFixture<ApiFactory>
{
    private readonly AuthApi _api;
    private static readonly Guid UserId = Guid.Parse("0197a1b2-1111-7222-8333-444455556666");

    public AccessControlTests(ApiFactory factory) => _api = new AuthApi(AuthApi.CreateClient(factory));

    private static string Token(UserRole role = UserRole.Customer) => TestTokens.For(UserId, role);

    // ---------------------------------------------------------------- access levels

    [Fact]
    public async Task A_user_route_without_a_token_is_401_in_the_uniform_shape()
    {
        var response = await _api.GetAsync("/api/_probe/user-only");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AuthApi.AssertError(await AuthApi.JsonAsync(response), HttpStatusCode.Unauthorized, "Sign in required");
    }

    [Fact]
    public async Task A_garbage_token_is_401()
    {
        var response = await _api.GetAsync("/api/_probe/user-only", bearer: "not.a.token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AuthApi.AssertError(await AuthApi.JsonAsync(response), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_customer_may_use_user_routes_but_gets_403_on_admin_routes()
    {
        var customer = Token(UserRole.Customer);

        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/_probe/user-only", customer)).StatusCode);

        var forbidden = await _api.GetAsync("/api/_probe/admin-only", customer);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        AuthApi.AssertError(await AuthApi.JsonAsync(forbidden), HttpStatusCode.Forbidden, "You are not allowed to do this");
    }

    [Fact]
    public async Task An_admin_may_use_admin_routes()
    {
        var response = await _api.GetAsync("/api/_probe/admin-only", Token(UserRole.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_a_forged_role_is_rejected()
    {
        var forged = TestTokens.WithForgedRole(Token(UserRole.Customer), "customer", "admin");

        var response = await _api.GetAsync("/api/_probe/admin-only", forged);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_foreign_key_and_foreign_issuer_tokens_are_401()
    {
        var expired = TestTokens.For(UserId, UserRole.Admin, now: DateTimeOffset.UtcNow.AddHours(-1));
        var otherKey = TestTokens.For(UserId, UserRole.Admin, key: "another-signing-key-0123456789-zzzzzzzzzzzz");
        var otherIssuer = TestTokens.For(UserId, UserRole.Admin, issuer: "SomeoneElse");

        foreach (var token in new[] { expired, otherKey, otherIssuer })
        {
            var response = await _api.GetAsync("/api/_probe/admin-only", token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // ---------------------------------------------------------------- optional routes and current user

    [Fact]
    public async Task An_optional_route_identifies_a_valid_token()
    {
        var response = await _api.GetAsync("/api/_probe/whoami", Token(UserRole.Admin));

        var body = await AuthApi.JsonAsync(response);
        Assert.True(body.GetProperty("authenticated").GetBoolean());
        Assert.Equal(UserId.ToString(), body.GetProperty("userId").GetString());
        Assert.Equal("admin", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("isAdmin").GetBoolean());
        Assert.Equal("tester@example.com", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task An_optional_route_works_without_a_token()
    {
        var response = await _api.GetAsync("/api/_probe/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AuthApi.JsonAsync(response);
        Assert.False(body.GetProperty("authenticated").GetBoolean());
        Assert.False(body.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, body.GetProperty("userId").ValueKind);
    }

    [Fact]
    public async Task An_optional_route_treats_an_expired_token_as_anonymous_instead_of_failing()
    {
        var expired = TestTokens.For(UserId, UserRole.Admin, now: DateTimeOffset.UtcNow.AddHours(-1));

        var response = await _api.GetAsync("/api/_probe/whoami", expired);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await AuthApi.JsonAsync(response)).GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task A_customer_is_not_an_admin_for_use_cases()
    {
        var body = await AuthApi.JsonAsync(await _api.GetAsync("/api/_probe/whoami", Token(UserRole.Customer)));

        Assert.True(body.GetProperty("authenticated").GetBoolean());
        Assert.Equal("customer", body.GetProperty("role").GetString());
        Assert.False(body.GetProperty("isAdmin").GetBoolean());
    }
}

[Collection(PostgresCollection.Name)]
public sealed class RateLimitTests(PostgresFixture postgres)
{
    private static ApiFactory Factory(int limit, int windowSeconds, string? connection = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPermitLimit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:AuthWindowSeconds"] = windowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (connection is not null)
        {
            settings["ConnectionStrings:Default"] = connection;
        }

        return ApiFactory.With(settings);
    }

    private static Task<HttpResponseMessage> LoginAsync(AuthApi api, int attempt)
        => api.LoginAsync($"person{attempt}@example.com", "Whatever123");

    [Fact]
    public async Task The_default_limit_is_10_requests_per_minute_and_the_11th_is_429_with_retry_after()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connection,
            ["RateLimiting:AuthPermitLimit"] = "10",
        });
        var api = new AuthApi(AuthApi.CreateClient(factory));

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(api, i)).StatusCode);
        }

        var limited = await LoginAsync(api, 11);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var retryAfter = Assert.Single(limited.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture), 1, 60);
        AuthApi.AssertError(await AuthApi.JsonAsync(limited), HttpStatusCode.TooManyRequests, "Too many requests, try again later");
    }

    [Fact]
    public async Task The_limit_covers_register_login_forgot_reset_and_refresh()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = Factory(limit: 1, windowSeconds: 60, connection);
        var api = new AuthApi(AuthApi.CreateClient(factory));

        Assert.NotEqual(HttpStatusCode.TooManyRequests, (await api.PostAsync("/api/auth/forgot", new { email = "a@example.com" })).StatusCode);

        foreach (var path in new[] { "/api/auth/register", "/api/auth/login", "/api/auth/forgot", "/api/auth/reset", "/api/auth/refresh" })
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await api.PostAsync(path, new { })).StatusCode);
        }
    }

    [Fact]
    public async Task Other_routes_are_not_affected_when_the_auth_limit_is_used_up()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = Factory(limit: 1, windowSeconds: 60, connection);
        var api = new AuthApi(AuthApi.CreateClient(factory));
        await LoginAsync(api, 1);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(api, 2)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/_probe/ok")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync("/api/auth/logout")).StatusCode);
    }

    [Fact]
    public async Task Requests_are_accepted_again_after_the_window_passes()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = Factory(limit: 2, windowSeconds: 1, connection);
        var api = new AuthApi(AuthApi.CreateClient(factory));
        await LoginAsync(api, 1);
        await LoginAsync(api, 2);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(api, 3)).StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(api, 4)).StatusCode);
    }

    [Fact]
    public async Task X_Forwarded_For_is_ignored_when_forwarding_is_not_enabled()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = Factory(limit: 2, windowSeconds: 60, connection);
        var client = AuthApi.CreateClient(factory);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { email = $"spoof{i}@example.com", password = "Whatever123" }),
            };
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{i + 1}");
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);
    }
}
