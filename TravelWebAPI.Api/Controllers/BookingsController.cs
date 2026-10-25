using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Bookings;

namespace TravelWebAPI.Api.Controllers;

/// <summary>
/// Bookings. Customers create and read their own; the admin sees and manages all of them. Thin: the rules live in
/// <see cref="BookingService"/>, which also decides what each caller may see.
/// </summary>
[ApiController]
[Route("api/bookings")]
public sealed class BookingsController(BookingService bookings) : ControllerBase
{
    /// <summary>Creates a booking for the signed-in user. The answer is the booking reference.</summary>
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] BookingRequest? request, CancellationToken cancellationToken)
    {
        var receipt = await bookings.CreateAsync(request ?? new BookingRequest(), cancellationToken);
        return Created($"/api/bookings/{receipt.Reference}", receipt);
    }

    /// <summary>The signed-in user's own bookings, newest first.</summary>
    [HttpGet("mine")]
    [Authorize]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken)
        => Ok(await bookings.ListMineAsync(cancellationToken));

    /// <summary>Every booking (admin).</summary>
    [HttpGet]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await bookings.ListAllAsync(cancellationToken));

    /// <summary>One booking by id or reference. A customer only gets their own; the admin gets the full detail.</summary>
    [HttpGet("{idOrReference}")]
    [Authorize]
    public async Task<IActionResult> Get(string idOrReference, CancellationToken cancellationToken)
        => Ok(await bookings.GetAsync(idOrReference, cancellationToken));

    /// <summary>Edits a booking: what is not sent keeps its value. A real change of status emails the customer.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Update(string id, [FromBody] BookingUpdateRequest? request, CancellationToken cancellationToken)
        => Ok(await bookings.UpdateAsync(id, request ?? new BookingUpdateRequest(), cancellationToken));

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await bookings.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Sends the customer a message of the admin's own and returns the email log entry.</summary>
    [HttpPost("{id}/email")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> SendEmail(string id, [FromBody] BookingEmailRequest? request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await bookings.SendEmailAsync(id, request ?? new BookingEmailRequest(), cancellationToken));
}
