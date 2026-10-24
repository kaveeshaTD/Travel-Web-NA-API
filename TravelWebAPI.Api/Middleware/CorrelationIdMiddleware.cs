using System.Diagnostics;
using Serilog.Context;

namespace TravelWebAPI.Api.Middleware;

/// <summary>
/// Gives every request a correlation id, returns it in the <c>X-Correlation-Id</c> response header
/// and adds it to every log line written while the request runs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogPropertyName = "CorrelationId";
    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = FromRequest(context) ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    /// <summary>Accepts a caller-supplied id only when it is short and made of safe characters.</summary>
    private static string? FromRequest(HttpContext context)
    {
        var value = context.Request.Headers[HeaderName].ToString();

        return value.Length is > 0 and <= MaxLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? value
            : null;
    }
}
