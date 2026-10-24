using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class RefreshCookieTests(PostgresFixture postgres)
{
    private async Task<ApiFactory> FactoryAsync(string environment = "Staging")
    {
        var connection = await postgres.CreateDatabaseAsync();
        return ApiFactory.With(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection }, environment);
    }

    [Fact]
    public async Task Registration_sets_a_secure_http_only_lax_cookie_for_the_auth_path_lasting_30_days()
    {
        await using var factory = await FactoryAsync();
        var api = new AuthApi(AuthApi.CreateClient(factory));

        var response = await api.RegisterAsync();

        var cookie = SetCookie.Find(response);
        Assert.NotNull(cookie);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal("/api/auth", cookie.Path);
        Assert.Equal("lax", cookie.SameSite?.ToLowerInvariant());
        Assert.False(string.IsNullOrEmpty(cookie.Value));
        Assert.InRange((cookie.Expires!.Value - factory.Clock.Now).TotalDays, 29.99, 30.01);
    }

    [Fact]
    public async Task Login_cookie_lasts_1_day_by_default_and_30_days_with_remember_me()
    {
        await using var factory = await FactoryAsync();
        var api = new AuthApi(AuthApi.CreateClient(factory));
        await api.RegisterAsync();

        var normal = SetCookie.Find(await api.LoginAsync("emma@example.com", "Customer123"));
        var remembered = SetCookie.Find(await api.LoginAsync("emma@example.com", "Customer123", remember: true));

        Assert.InRange((normal!.Expires!.Value - factory.Clock.Now).TotalHours, 23.99, 24.01);
        Assert.InRange((remembered!.Expires!.Value - factory.Clock.Now).TotalDays, 29.99, 30.01);
    }

    [Fact]
    public async Task The_response_body_never_contains_the_refresh_token()
    {
        await using var factory = await FactoryAsync();
        var api = new AuthApi(AuthApi.CreateClient(factory));

        var response = await api.RegisterAsync();
        var text = await response.Content.ReadAsStringAsync();
        var cookie = SetCookie.Find(response)!;

        var properties = JsonDocument.Parse(text).RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["token", "user"], properties);
        Assert.DoesNotContain(cookie.Value, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_hash_of_the_refresh_token_is_stored()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        var api = new AuthApi(AuthApi.CreateClient(factory));

        var cookie = SetCookie.Find(await api.RegisterAsync())!;

        var stored = await PostgresFixture.ScalarAsync<string>(connection, "SELECT token_hash FROM refresh_tokens");
        Assert.NotEqual(cookie.Value, stored);
        Assert.Matches("^[0-9a-f]{64}$", stored);
    }

    [Fact]
    public async Task Logout_clears_the_cookie_with_the_same_path()
    {
        await using var factory = await FactoryAsync();
        var api = new AuthApi(AuthApi.CreateClient(factory));
        var cookie = SetCookie.Find(await api.RegisterAsync())!;

        var response = await api.PostAsync("/api/auth/logout", refreshCookie: cookie.Value);

        var cleared = SetCookie.Find(response);
        Assert.NotNull(cleared);
        Assert.True(cleared.IsCleared);
        Assert.Equal("/api/auth", cleared.Path);
        Assert.True(cleared.HttpOnly);
    }

    [Fact]
    public async Task In_Development_over_plain_http_the_cookie_is_not_marked_secure()
    {
        await using var factory = await FactoryAsync("Development");
        var api = new AuthApi(AuthApi.CreateClient(factory));

        var cookie = SetCookie.Find(await api.RegisterAsync());

        Assert.NotNull(cookie);
        Assert.False(cookie.Secure);
        Assert.True(cookie.HttpOnly);
    }

    [Fact]
    public async Task In_Development_over_https_the_cookie_is_secure()
    {
        await using var factory = await FactoryAsync("Development");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
        var api = new AuthApi(client);

        var cookie = SetCookie.Find(await api.RegisterAsync());

        Assert.True(cookie!.Secure);
    }

    [Fact]
    public async Task In_Production_the_cookie_is_always_secure_even_behind_a_proxy_that_speaks_http()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var factory = ApiFactory.With(ApiFactory.ProductionSettings(connection), "Production");
        var api = new AuthApi(AuthApi.CreateClient(factory));

        var response = await api.RegisterAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(SetCookie.Find(response)!.Secure);
    }
}
