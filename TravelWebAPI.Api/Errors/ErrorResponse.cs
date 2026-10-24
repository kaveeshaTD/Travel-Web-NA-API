using System.Text.Json;
using System.Text.Json.Serialization;

namespace TravelWebAPI.Api.Errors;

/// <summary>The single error body of the API: <c>{ "error": { "status", "message", "fields" } }</c>.</summary>
public sealed record ErrorResponse(ErrorBody Error)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ErrorResponse Create(int status, string message, IReadOnlyDictionary<string, object>? fields = null)
        => new(new ErrorBody(status, message, fields is { Count: > 0 } ? fields : null));

    /// <summary>Writes the error as JSON, unless a response body was already started.</summary>
    public static async Task WriteAsync(
        HttpContext context,
        int status,
        string message,
        IReadOnlyDictionary<string, object>? fields = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(Create(status, message, fields), Options),
            context.RequestAborted);
    }

    public static string DefaultMessage(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "Bad request",
        StatusCodes.Status401Unauthorized => "Sign in required",
        StatusCodes.Status403Forbidden => "You are not allowed to do this",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status405MethodNotAllowed => "Method not allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Request body is too large",
        StatusCodes.Status415UnsupportedMediaType => "Unsupported content type",
        StatusCodes.Status429TooManyRequests => "Too many requests, try again later",
        _ when status >= 500 => "Something went wrong",
        _ => "Request failed",
    };
}

public sealed record ErrorBody(
    int Status,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object>? Fields);
