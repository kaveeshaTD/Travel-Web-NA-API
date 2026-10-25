using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Stats;

namespace TravelWebAPI.Api.Controllers;

/// <summary>The admin dashboard numbers. Thin: the figures are put together in <see cref="DashboardService"/>.</summary>
[ApiController]
[Route("api/stats")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class StatsController(DashboardService dashboard) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await dashboard.GetAsync(cancellationToken));
}
