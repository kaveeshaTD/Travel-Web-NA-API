using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Tests.Integration.Support;

public sealed class ProbeRequest
{
    [Required(ErrorMessage = "Enter a valid email address")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    public string? Email { get; set; }

    [Range(1, 20, ErrorMessage = "Adults must be between 1 and 20")]
    public int Adults { get; set; }

    public BookingStatus Status { get; set; }
}

/// <summary>Endpoints that only exist in tests, to provoke every error the API must answer correctly.</summary>
[ApiController]
[Route("api/_probe")]
public sealed class ErrorProbeController : ControllerBase
{
    [HttpGet("validation")]
    public IActionResult Validation()
        => throw new ValidationException(new Dictionary<string, string>
        {
            ["email"] = "Enter a valid email address",
            ["password"] = "Use at least 8 characters",
        });

    [HttpGet("unauthorized")]
    public IActionResult Unauthorized_() => throw new UnauthorizedException();

    [HttpGet("forbidden")]
    public IActionResult Forbidden() => throw new ForbiddenException();

    [HttpGet("not-found")]
    public IActionResult NotFound_() => throw new NotFoundException("Package", "nope");

    [HttpGet("conflict")]
    public IActionResult Conflict_()
        => throw new ConflictException("Email already registered", new Dictionary<string, string> { ["email"] = "Already in use" });

    [HttpDelete("dependents")]
    public IActionResult Dependents() => throw new DependentsExistException("Destination", ["cultural-triangle", "hill-country"]);

    [HttpGet("too-large")]
    public IActionResult TooLarge() => throw new PayloadTooLargeException();

    [HttpPost("duplicate-key")]
    public IActionResult DuplicateKey() => throw new DuplicateKeyException();

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret-connection-string password=hunter2");

    [HttpGet("empty-401")]
    public IActionResult EmptyUnauthorized() => StatusCode(StatusCodes.Status401Unauthorized);

    [HttpGet("empty-403")]
    public IActionResult EmptyForbidden() => StatusCode(StatusCodes.Status403Forbidden);

    [HttpPost("model")]
    public IActionResult Model([FromBody] ProbeRequest request) => Ok(request);

    [HttpPost("echo")]
    public IActionResult Echo([FromBody] ProbeRequest request) => Ok(request);

    [HttpGet("slow")]
    public async Task<IActionResult> Slow(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return Ok();
    }

    [HttpPut("only-put")]
    public IActionResult OnlyPut() => Ok();

    [HttpPost("created")]
    public IActionResult Created_() => Created("/api/_probe/created/42", new { id = 42 });

    [HttpGet("ok")]
    public IActionResult Ok_() => Ok(new { value = 1 });

    [HttpDelete("no-content")]
    public IActionResult NoContent_() => NoContent();

    [HttpGet("whoami")]
    public IActionResult WhoAmI([FromServices] ICurrentUser user)
        => Ok(new { authenticated = user.IsAuthenticated, userId = user.UserId, email = user.Email, role = user.Role, isAdmin = user.IsAdmin });

    [HttpGet("user-only")]
    [Authorize]
    public IActionResult UserOnly() => Ok(new { ok = true });

    [HttpGet("admin-only")]
    [Authorize(Roles = "admin")]
    public IActionResult AdminOnly() => Ok(new { ok = true });

    [HttpGet("sample")]
    public IActionResult Sample() => Ok(new
    {
        TravelDate = new DateOnly(2026, 3, 10),
        CreatedAt = new DateTimeOffset(2026, 3, 10, 8, 30, 0, TimeSpan.Zero),
        EmailKind = EmailKind.PasswordReset,
        Status = BookingStatus.Pending,
        Method = PaymentMethod.BankTransfer,
        Currency = CurrencyCode.Lkr,
    });
}
