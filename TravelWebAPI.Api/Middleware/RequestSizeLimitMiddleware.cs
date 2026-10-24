using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Options;
using TravelWebAPI.Api.Configuration;
using TravelWebAPI.Api.Errors;

namespace TravelWebAPI.Api.Middleware;

/// <summary>
/// Rejects request bodies over the configured size with a 413 in the uniform error shape.
/// An endpoint can set its own limit with <c>[RequestSizeLimit]</c> (the upload endpoint does this later).
/// </summary>
public sealed class RequestSizeLimitMiddleware(RequestDelegate next, IOptions<LimitsOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize
            ?? options.Value.MaxRequestBodyBytes;

        if (context.Request.ContentLength > limit)
        {
            await ErrorResponse.WriteAsync(
                context,
                StatusCodes.Status413PayloadTooLarge,
                ErrorResponse.DefaultMessage(StatusCodes.Status413PayloadTooLarge));
            return;
        }

        // Bodies without a Content-Length (chunked) are cut off by the server while they are read.
        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = limit;
        }

        await next(context);
    }
}
