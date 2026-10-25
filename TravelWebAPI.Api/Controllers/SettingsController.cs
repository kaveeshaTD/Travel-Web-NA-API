using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Content;

namespace TravelWebAPI.Api.Controllers;

/// <summary>Business details, trust figures and social links. The website loads them before it renders.</summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken cancellationToken)
        => Ok(await settings.GetAsync(cancellationToken));

    /// <summary>Replaces the whole document. A <c>currencyRates</c> field sent by an older client is ignored.</summary>
    [HttpPut]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<SettingsDto>> Put([FromBody] SettingsRequest request, CancellationToken cancellationToken)
        => Ok(await settings.UpdateAsync(request, cancellationToken));
}

/// <summary>When to travel where, and practical information for visitors.</summary>
[ApiController]
[Route("api/tips")]
public sealed class TipsController(TipsService tips) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<TipsDto>> Get(CancellationToken cancellationToken)
        => Ok(await tips.GetAsync(cancellationToken));

    /// <summary>Replaces the whole document.</summary>
    [HttpPut]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<TipsDto>> Put([FromBody] TipsRequest request, CancellationToken cancellationToken)
        => Ok(await tips.UpdateAsync(request, cancellationToken));
}
