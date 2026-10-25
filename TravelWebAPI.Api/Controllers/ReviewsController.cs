using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Api.Controllers;

/// <summary>Customer reviews. Anyone can read published ones and submit a new one; the admin moderates. Thin: rules live in <see cref="ReviewService"/>.</summary>
[ApiController]
[Route("api/reviews")]
public sealed class ReviewsController(ReviewService reviews, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// One page of published reviews, newest first. An admin who adds <c>?all=1</c> gets every review with its author and may
    /// filter by status. Supports page, pageSize, search, sort, facets and the filter rating.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ReviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ReviewListRequest request, CancellationToken cancellationToken)
        => request.All == "1" && currentUser.IsAdmin
            ? Ok(await reviews.ListAllAsync(request, cancellationToken))
            : Ok(await reviews.ListPublishedAsync(request, cancellationToken));

    /// <summary>Number of published reviews, their average rating and how many have each rating from 1 to 5.</summary>
    [HttpGet("summary")]
    [AllowAnonymous]
    public async Task<ActionResult<ReviewSummary>> Summary(CancellationToken cancellationToken)
        => Ok(await reviews.SummaryAsync(cancellationToken));

    /// <summary>A visitor's review (guests allowed). It waits as <c>pending</c> until an admin publishes it.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Submit)]
    public async Task<IActionResult> Submit([FromBody] ReviewSubmission? request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await reviews.SubmitAsync(request ?? new ReviewSubmission(), cancellationToken));

    [HttpPost("admin")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Add([FromBody] ReviewRequest? request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await reviews.AddAsync(request ?? new ReviewRequest(), cancellationToken));

    /// <summary>Edits or moderates a review: what is not sent keeps its value.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Update(string id, [FromBody] ReviewRequest? request, CancellationToken cancellationToken)
        => Ok(await reviews.UpdateAsync(ParseId(id), request ?? new ReviewRequest(), cancellationToken));

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await reviews.DeleteAsync(ParseId(id), cancellationToken);
        return NoContent();
    }

    /// <summary>An id that is not a valid identifier cannot exist, so it is a plain "not found".</summary>
    private static Guid ParseId(string id)
        => Guid.TryParse(id, out var parsed) ? parsed : throw new NotFoundException(ReviewService.NotFoundMessage);
}
