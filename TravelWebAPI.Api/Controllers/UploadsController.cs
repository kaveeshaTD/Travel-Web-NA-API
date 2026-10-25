using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Api.Controllers;

/// <summary>Image upload for the admin portal. Thin: the checks and storage live in <see cref="UploadService"/>.</summary>
[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(UploadService uploads) : ControllerBase
{
    /// <summary>The largest request: the biggest image plus room for the multipart envelope.</summary>
    public const long MaxRequestBytes = UploadService.MaxBytes + 64 * 1024;

    /// <summary>Uploads one JPEG, PNG or WebP image (form field <c>file</c>, up to 5 MB) and returns its public path.</summary>
    [HttpPost]
    [Authorize(Roles = AuthRoles.Admin)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<UploadResponse>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new ValidationException("file", UploadService.MissingFileMessage);
        }

        await using var stream = file.OpenReadStream();
        var uploaded = await uploads.UploadAsync(file.FileName, stream, cancellationToken);
        return Created(uploaded.Url, uploaded);
    }
}
