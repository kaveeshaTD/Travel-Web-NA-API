using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Payments;

namespace TravelWebAPI.Api.Controllers;

/// <summary>Money received for bookings (admin only). Thin: the rules live in <see cref="PaymentService"/>.</summary>
[ApiController]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>Records a payment for a booking (id or reference). The answer points at the booking, where payments are read.</summary>
    [HttpPost("api/bookings/{bookingId}/payments")]
    public async Task<IActionResult> Create(string bookingId, [FromBody] PaymentRequest? request, CancellationToken cancellationToken)
    {
        var payment = await payments.CreateAsync(bookingId, request ?? new PaymentRequest(), cancellationToken);
        return Created($"/api/bookings/{payment.BookingId}", payment);
    }

    /// <summary>Every payment with its booking reference and customer name, newest paid first.</summary>
    [HttpGet("api/payments")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await payments.ListAsync(cancellationToken));

    /// <summary>Edits a payment: what is not sent keeps its value; the rate changes only when the currency does.</summary>
    [HttpPut("api/payments/{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] PaymentRequest? request, CancellationToken cancellationToken)
        => Ok(await payments.UpdateAsync(id, request ?? new PaymentRequest(), cancellationToken));

    [HttpDelete("api/payments/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await payments.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
