using TravelWebAPI.Application.Auth;

namespace TravelWebAPI.Api.Auth;

/// <summary>
/// The refresh token cookie: httpOnly (scripts cannot read it), limited to <c>/api/auth</c>, same-site, and secure
/// over HTTPS. Only plain HTTP on localhost in Development is allowed to skip the Secure flag.
/// </summary>
public sealed class RefreshCookie(IWebHostEnvironment environment)
{
    public const string Name = "refresh_token";
    public const string Path = "/api/auth";

    public string? Read(HttpRequest request) => request.Cookies[Name];

    public void Append(HttpResponse response, AuthSession session)
        => response.Cookies.Append(Name, session.RefreshToken, Options(response.HttpContext.Request, session.RefreshExpiresAt));

    public void Delete(HttpResponse response)
        => response.Cookies.Delete(Name, Options(response.HttpContext.Request, expires: null));

    private CookieOptions Options(HttpRequest request, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = request.IsHttps || !environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
