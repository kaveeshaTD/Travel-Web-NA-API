using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Unit;

namespace TravelWebAPI.Tests.Integration.Support;

/// <summary>The refresh cookie as the browser would see it in a <c>Set-Cookie</c> header.</summary>
public sealed record SetCookie(string Name, string Value, bool HttpOnly, bool Secure, string? Path, string? SameSite, DateTimeOffset? Expires)
{
    public bool IsCleared => Value.Length == 0 || Expires is { } e && e < DateTimeOffset.UtcNow.AddDays(-300);

    public static SetCookie? Find(HttpResponseMessage response, string name = "refresh_token")
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var header in values)
        {
            var parts = header.Split(';', StringSplitOptions.TrimEntries);
            var pair = parts[0].Split('=', 2);
            if (pair[0] != name)
            {
                continue;
            }

            string? Attribute(string key) => parts.Skip(1).Select(p => p.Split('=', 2)).FirstOrDefault(p => p[0].Equals(key, StringComparison.OrdinalIgnoreCase))?.ElementAtOrDefault(1);
            bool Flag(string key) => parts.Skip(1).Any(p => p.Equals(key, StringComparison.OrdinalIgnoreCase));

            DateTimeOffset? expires = DateTimeOffset.TryParse(Attribute("expires"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var e) ? e : null;
            return new SetCookie(pair[0], pair.Length > 1 ? pair[1] : string.Empty, Flag("httponly"), Flag("secure"), Attribute("path"), Attribute("samesite"), expires);
        }

        return null;
    }
}

/// <summary>A tiny client for the auth endpoints. Cookies are sent by hand so the tests control exactly what the browser would send.</summary>
public sealed class AuthApi(HttpClient client)
{
    public static HttpClient CreateClient(ApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null, string? refreshCookie = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        Decorate(request, refreshCookie, bearer);
        return await client.SendAsync(request);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? refreshCookie = null, string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        Decorate(request, refreshCookie, bearer);
        return await client.SendAsync(request);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => client.SendAsync(request);

    public Task<HttpResponseMessage> GetAsync(string path, string? bearer = null, string? refreshCookie = null)
        => SendAsync(HttpMethod.Get, path, null, refreshCookie, bearer);

    public Task<HttpResponseMessage> RegisterAsync(string email = "emma@example.com", string password = "Customer123", string name = "Emma Wilson", object? extra = null)
        => PostAsync("/api/auth/register", extra ?? new { name, email, password, phone = "+44 7700 900123", country = "United Kingdom" });

    public Task<HttpResponseMessage> LoginAsync(string email, string password, bool? remember = null)
        => PostAsync("/api/auth/login", remember is null ? new { email, password } : new { email, password, remember });

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<string> TokenAsync(HttpResponseMessage response)
        => (await JsonAsync(response)).GetProperty("token").GetString()!;

    private static void Decorate(HttpRequestMessage request, string? refreshCookie, string? bearer)
    {
        if (refreshCookie is not null)
        {
            request.Headers.Add("Cookie", $"refresh_token={refreshCookie}");
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }
    }

    public static void AssertError(JsonElement body, HttpStatusCode status, string? message = null)
    {
        var error = body.GetProperty("error");
        Assert.Equal((int)status, error.GetProperty("status").GetInt32());
        if (message is not null)
        {
            Assert.Equal(message, error.GetProperty("message").GetString());
        }
    }
}

/// <summary>Builds access tokens the API accepts (or deliberately does not).</summary>
public static class TestTokens
{
    public static string For(Guid userId, UserRole role, string email = "tester@example.com", string key = ApiFactory.JwtKey, DateTimeOffset? now = null, string issuer = "TravelWebAPI")
    {
        var service = new JwtTokenService(
            Options.Create(new JwtSettings { Key = key, Issuer = issuer }),
            new FakeClock(now ?? DateTimeOffset.UtcNow));

        return service.CreateAccessToken(new User { Id = userId, Name = "Tester", Email = email, Role = role });
    }

    /// <summary>Takes a real token and swaps the role in its payload, keeping the old signature.</summary>
    public static string WithForgedRole(string token, string from, string to)
    {
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]))
            .Replace($"\"{from}\"", $"\"{to}\"", StringComparison.Ordinal);
        return $"{parts[0]}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(payload)}.{parts[2]}";
    }

    public static JsonWebToken Read(string token) => new(token);
}
