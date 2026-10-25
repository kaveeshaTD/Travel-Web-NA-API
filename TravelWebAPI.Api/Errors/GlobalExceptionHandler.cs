using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Api.Errors;

/// <summary>
/// Turns every exception into the uniform error body with the right status code.
/// Expected application errors keep their message; anything unexpected becomes a generic 500
/// and is logged with the correlation id, never shown to the caller.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public const string InvalidBodyMessage = "The request body is not valid JSON";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away: nothing to answer and nothing to report.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug("Request cancelled by the client: {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, message, fields) = Map(exception);

        if (exception is TooManyRequestsException tooMany)
        {
            httpContext.Response.Headers.RetryAfter =
                Math.Ceiling(tooMany.RetryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation(
                "Request failed with {Status} ({ExceptionType}): {Message}",
                status,
                exception.GetType().Name,
                message);
        }

        await ErrorResponse.WriteAsync(httpContext, status, message, fields);
        return true;
    }

    private static (int Status, string Message, IReadOnlyDictionary<string, object>? Fields) Map(Exception exception) => exception switch
    {
        ValidationException e => (StatusCodes.Status400BadRequest, e.Message, AsObjects(e.Fields)),
        UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Message, null),
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message, null),
        NotFoundException e => (StatusCodes.Status404NotFound, e.Message, null),
        PayloadTooLargeException e => (StatusCodes.Status413PayloadTooLarge, e.Message, null),
        TooManyRequestsException e => (StatusCodes.Status429TooManyRequests, e.Message, null),
        DependentsExistException e => (
            StatusCodes.Status409Conflict,
            e.Message,
            new Dictionary<string, object> { ["dependents"] = e.Dependents }),
        ConflictException e => (StatusCodes.Status409Conflict, e.Message, AsObjects(e.Fields)),
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => (
            StatusCodes.Status413PayloadTooLarge, ErrorResponse.DefaultMessage(StatusCodes.Status413PayloadTooLarge), null),
        BadHttpRequestException e => (e.StatusCode, ErrorResponse.DefaultMessage(e.StatusCode), null),
        JsonException => (StatusCodes.Status400BadRequest, InvalidBodyMessage, null),
        _ => (StatusCodes.Status500InternalServerError, ErrorResponse.DefaultMessage(StatusCodes.Status500InternalServerError), null),
    };

    private static Dictionary<string, object> AsObjects(IReadOnlyDictionary<string, string> fields)
        => fields.ToDictionary(f => f.Key, f => (object)f.Value);
}

