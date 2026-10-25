using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Lists;
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

    /// <summary>
    /// One page of payments with their booking reference and customer name, newest paid first. Supports page, pageSize,
    /// search, sort, facets and the filters status, method, currency, booking, from and to.
    /// </summary>
    [HttpGet("api/payments")]
    public async Task<ActionResult<PagedResult<PaymentListItemDto>>> List([FromQuery] PaymentListRequest request, CancellationToken cancellationToken)
        => Ok(await payments.ListAsync(request, cancellationToken));

    /// <summary>The totals over all matching payments (paid, pending, refunded, net), in LKR. Filters: method, currency, from, to.</summary>
    [HttpGet("api/payments/summary")]
    public async Task<ActionResult<PaymentSummary>> Summary([FromQuery] PaymentSummaryRequest request, CancellationToken cancellationToken)
        => Ok(await payments.SummaryAsync(request, cancellationToken));

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
