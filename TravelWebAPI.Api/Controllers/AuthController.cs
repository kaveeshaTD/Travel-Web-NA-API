using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Auth;

namespace TravelWebAPI.Api.Controllers;

/// <summary>Sign up, sign in, sessions, profile and password recovery. Thin: all rules live in <see cref="AuthService"/>.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, RefreshCookie refreshCookie) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request, CancellationToken cancellationToken)
    {
        var session = await auth.RegisterAsync(request ?? new RegisterRequest(), Client(), cancellationToken);
        refreshCookie.Append(Response, session);
        return StatusCode(StatusCodes.Status201Created, session.Response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        var session = await auth.LoginAsync(request ?? new LoginRequest(), Client(), cancellationToken);
        refreshCookie.Append(Response, session);
        return Ok(session.Response);
    }

    /// <summary>Renews the access token from the refresh cookie and rotates the cookie.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await auth.RefreshAsync(refreshCookie.Read(Request), Client(), cancellationToken);
        if (result.Session is null)
        {
            if (result.ClearCookie)
            {
                refreshCookie.Delete(Response);
            }

            // An empty 401 is shaped into the uniform error body by the status-code middleware.
            return StatusCode(StatusCodes.Status401Unauthorized);
        }

        refreshCookie.Append(Response, result.Session);
        return Ok(result.Session.Response);
    }

    /// <summary>Works with only the cookie, so an expired access token never blocks sign out.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(refreshCookie.Read(Request), cancellationToken);
        refreshCookie.Delete(Response);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
        => Ok(await auth.GetProfileAsync(cancellationToken));

    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest? request, CancellationToken cancellationToken)
        => Ok(await auth.UpdateProfileAsync(request ?? new UpdateProfileRequest(), refreshCookie.Read(Request), cancellationToken));

    /// <summary>Always answers <c>{ ok: true }</c> so it never reveals which emails have accounts.</summary>
    [HttpPost("forgot")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Forgot([FromBody] ForgotPasswordRequest? request, CancellationToken cancellationToken)
    {
        await auth.ForgotPasswordAsync(request ?? new ForgotPasswordRequest(), cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost("reset")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordRequest? request, CancellationToken cancellationToken)
    {
        await auth.ResetPasswordAsync(request ?? new ResetPasswordRequest(), cancellationToken);
        return NoContent();
    }

    private ClientInfo Client() => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());
}
