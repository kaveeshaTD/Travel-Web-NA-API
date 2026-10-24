using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;

namespace TravelWebAPI.Tests.Integration.Support;

public sealed record CapturedLog(
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, string> Properties);

/// <summary>
/// Serilog sink that keeps events in memory so tests can assert on what the API logged.
/// The API picks it up because Serilog is configured with <c>ReadFrom.Services</c>.
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public void Emit(LogEvent logEvent)
    {
        var properties = logEvent.Properties.ToDictionary(
            p => p.Key,
            p => p.Value is ScalarValue { Value: string text } ? text : p.Value.ToString());

        _entries.Enqueue(new CapturedLog(
            ToLogLevel(logEvent.Level),
            properties.GetValueOrDefault("SourceContext", string.Empty),
            logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture),
            logEvent.Exception,
            properties));
    }

    private static LogLevel ToLogLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => LogLevel.Trace,
        LogEventLevel.Debug => LogLevel.Debug,
        LogEventLevel.Information => LogLevel.Information,
        LogEventLevel.Warning => LogLevel.Warning,
        LogEventLevel.Error => LogLevel.Error,
        _ => LogLevel.Critical,
    };
}
