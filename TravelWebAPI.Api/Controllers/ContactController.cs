using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Api.Controllers;

/// <summary>The public contact form. Thin: rules live in <see cref="ContactService"/>.</summary>
[ApiController]
[Route("api/contact")]
public sealed class ContactController(ContactService contact) : ControllerBase
{
    /// <summary>Saves the message, records an acknowledgement email and answers with a reference.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Submit)]
    public async Task<IActionResult> Send([FromBody] ContactRequest? request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await contact.SubmitAsync(request ?? new ContactRequest(), cancellationToken));
}

/// <summary>The admin inbox.</summary>
[ApiController]
[Route("api/messages")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class MessagesController(ContactService contact) : ControllerBase
{
    /// <summary>Every message, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await contact.ListAsync(cancellationToken));

    /// <summary>Sets the status to <c>new</c>, <c>read</c> or <c>replied</c>.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> SetStatus(string id, [FromBody] MessageStatusRequest? request, CancellationToken cancellationToken)
        => Ok(await contact.SetStatusAsync(ParseId(id), request ?? new MessageStatusRequest(), cancellationToken));

    /// <summary>Records the reply email and marks the message replied. The answer is the email log entry.</summary>
    [HttpPost("{id}/reply")]
    public async Task<IActionResult> Reply(string id, [FromBody] ReplyRequest? request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await contact.ReplyAsync(ParseId(id), request ?? new ReplyRequest(), cancellationToken));

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await contact.DeleteAsync(ParseId(id), cancellationToken);
        return NoContent();
    }

    private static Guid ParseId(string id)
        => Guid.TryParse(id, out var parsed) ? parsed : throw new NotFoundException(ContactService.NotFoundMessage);
}

/// <summary>The log of every email the system recorded, with whether it was delivered.</summary>
[ApiController]
[Route("api/emails")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class EmailsController(EmailLogService emails) : ControllerBase
{
    /// <summary>Newest first. <c>?limit=</c> sets how many (default 500, at most 2000).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? limit, CancellationToken cancellationToken)
        => Ok(await emails.ListAsync(limit, cancellationToken));
}
