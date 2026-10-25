using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Content;

namespace TravelWebAPI.Api.Controllers;

/// <summary>
/// The routes every content collection shares. Anyone can read; creating, changing, deleting and reordering are admin only.
/// The rules live in the service, so each collection is only a route name and a service.
/// </summary>
[ApiController]
public abstract class ContentController<TRequest, TDto>(IContentService<TRequest, TDto> service) : ControllerBase
    where TDto : IContentDto
{
    /// <summary>All records in display order. Admins can add <c>?all=1</c> to include unpublished packages.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<TDto>>> List([FromQuery] string? all, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(all == "1", cancellationToken));

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<ActionResult<TDto>> Get(string id, CancellationToken cancellationToken)
        => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<TDto>> Create([FromBody] TRequest request, CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(request, cancellationToken);
        return Created(Url.Action(nameof(Get), new { id = created.Id }) ?? string.Empty, created);
    }

    /// <summary>Partial update: what is not sent keeps its value.</summary>
    [HttpPut("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<TDto>> Update(string id, [FromBody] TRequest request, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id}")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Puts the listed ids first, in the given order, and returns the full list.</summary>
    [HttpPost("reorder")]
    [Authorize(Roles = AuthRoles.Admin)]
    public async Task<ActionResult<IReadOnlyList<TDto>>> Reorder([FromBody] ReorderRequest request, CancellationToken cancellationToken)
        => Ok(await service.ReorderAsync(request, cancellationToken));
}

[Route("api/packages")]
public sealed class PackagesController(PackageService service) : ContentController<PackageRequest, PackageDto>(service);

[Route("api/destinations")]
public sealed class DestinationsController(DestinationService service) : ContentController<DestinationRequest, DestinationDto>(service);

[Route("api/vehicles")]
public sealed class VehiclesController(VehicleService service) : ContentController<VehicleRequest, VehicleDto>(service);

[Route("api/services")]
public sealed class ServicesController(ServiceOfferingService service) : ContentController<ServiceRequest, ServiceDto>(service);

[Route("api/faqs")]
public sealed class FaqsController(FaqService service) : ContentController<FaqRequest, FaqDto>(service);

[Route("api/gallery")]
public sealed class GalleryController(GalleryService service) : ContentController<GalleryImageRequest, GalleryImageDto>(service);
