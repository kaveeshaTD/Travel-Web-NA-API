using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>
/// Reads exchange rates from a web address. Both free sources answer "units of the currency per 1 LKR"; the stored rate is the
/// inverse, "LKR per 1 unit". Any problem (network, status, size, format) is logged and answered with null, never thrown.
/// </summary>
public abstract class HttpExchangeRateProvider(HttpClient http, ILogger logger) : IExchangeRateProvider
{
    /// <summary>The most bytes read from an answer. The real answers are a few kilobytes.</summary>
    public const int MaxBodyBytes = 512 * 1024;

    public abstract string Name { get; }

    protected abstract string Url { get; }

    /// <summary>Currency per 1 LKR for the currencies found in the answer, or null when the answer is not what was expected.</summary>
    protected abstract Dictionary<CurrencyCode, decimal>? ReadPerLkr(JsonElement root);

    public async Task<RateQuote?> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("{Provider} answered HTTP {Status}", Name, (int)response.StatusCode);
                return null;
            }

            var body = await ReadLimitedAsync(response, cancellationToken);
            if (body is null)
            {
                logger.LogWarning("{Provider} answer is larger than {Limit} bytes", Name, MaxBodyBytes);
                return null;
            }

            using var document = JsonDocument.Parse(body);
            var perLkr = ReadPerLkr(document.RootElement);
            if (perLkr is null)
            {
                logger.LogWarning("{Provider} answer does not have the expected format", Name);
                return null;
            }

            var rates = new Dictionary<CurrencyCode, decimal>();
            foreach (var (currency, value) in perLkr)
            {
                if (Invert(value) is { } inverse)
                {
                    rates[currency] = inverse;
                }
            }

            return rates.Count == 0 ? null : new RateQuote(Name, rates);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or FormatException)
        {
            // A timeout surfaces as TaskCanceledException; a shutdown (our own token) must still stop the refresh.
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning("{Provider} could not be read: {Error}", Name, exception.Message);
            return null;
        }
    }

    /// <summary>LKR per 1 unit, rounded to the 6 decimals the column holds; null when the source value cannot be used.</summary>
    public static decimal? Invert(decimal perLkr)
    {
        if (perLkr <= 0)
        {
            return null;
        }

        var inverse = 1m / perLkr;
        return inverse <= RateCheck.MaxRate ? decimal.Round(inverse, 6) : null;
    }

    protected static decimal? ReadNumber(JsonElement parent, string name)
        => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
            ? number
            : null;

    /// <summary>The numbers for USD, EUR and GBP that are present, found under the key <paramref name="keyOf"/> gives for each code.</summary>
    protected static Dictionary<CurrencyCode, decimal> Pick(JsonElement rates, Func<string, string> keyOf)
    {
        var found = new Dictionary<CurrencyCode, decimal>();
        foreach (var currency in RateCheck.Currencies)
        {
            if (ReadNumber(rates, keyOf(EnumWire.ToWire(currency))) is { } number)
            {
                found[currency] = number;
            }
        }

        return found;
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

/// <summary><c>open.er-api.com</c>: <c>{ "result": "success", "base_code": "LKR", "rates": { "USD": 0.003011, ... } }</c>.</summary>
public sealed class PrimaryExchangeRateProvider(HttpClient http, IOptions<ExchangeRateOptions> options, ILogger<PrimaryExchangeRateProvider> logger)
    : HttpExchangeRateProvider(http, logger)
{
    public const string SourceName = "open.er-api.com";

    public override string Name => SourceName;

    protected override string Url => options.Value.PrimaryUrl;

    protected override Dictionary<CurrencyCode, decimal>? ReadPerLkr(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.String
            || result.GetString() != "success"
            || !root.TryGetProperty("base_code", out var baseCode)
            || baseCode.ValueKind != JsonValueKind.String
            || baseCode.GetString() != "LKR"
            || !root.TryGetProperty("rates", out var rates))
        {
            return null;
        }

        return Pick(rates, code => code);
    }
}

/// <summary><c>currency-api</c> on the jsDelivr CDN: <c>{ "date": "...", "lkr": { "usd": 0.0030137899, ... } }</c>.</summary>
public sealed class FallbackExchangeRateProvider(HttpClient http, IOptions<ExchangeRateOptions> options, ILogger<FallbackExchangeRateProvider> logger)
    : HttpExchangeRateProvider(http, logger)
{
    public const string SourceName = "currency-api";

    public override string Name => SourceName;

    protected override string Url => options.Value.FallbackUrl;

    protected override Dictionary<CurrencyCode, decimal>? ReadPerLkr(JsonElement root)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("lkr", out var rates)
            ? Pick(rates, code => code.ToLowerInvariant())
            : null;
}

public static class ExchangeRateHttpClient
{
    public static void Configure(IServiceProvider services, HttpClient client)
    {
        var settings = services.GetRequiredService<IOptions<ExchangeRateOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CeylonRoadsApi", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}
