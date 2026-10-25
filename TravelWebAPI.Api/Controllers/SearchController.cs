using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Search;

namespace TravelWebAPI.Api.Controllers;

/// <summary>The global search of the admin. Thin: the rules live in <see cref="SearchService"/>.</summary>
[ApiController]
[Route("api/search")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class SearchController(SearchService search) : ControllerBase
{
    /// <summary>Bookings, packages, destinations and users that contain <c>q</c> (at least 2 characters).</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken cancellationToken)
        => Ok(await search.SearchAsync(q, cancellationToken));
}
