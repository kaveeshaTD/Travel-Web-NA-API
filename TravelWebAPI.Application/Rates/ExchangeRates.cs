using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Rates;

/// <summary>Settings of the exchange-rate refresh (<c>ExchangeRates</c> section).</summary>
public sealed class ExchangeRateOptions
{
    public const string Section = "ExchangeRates";

    public bool Enabled { get; set; } = true;

    /// <summary>Stored rates younger than this are not fetched again.</summary>
    public int RefreshHours { get; set; } = 24;

    /// <summary>How long after start-up the first refresh waits, so start-up is never slowed down.</summary>
    public int StartupDelaySeconds { get; set; } = 10;

    /// <summary>An answer is rejected when a rate moves more than this many percent from the last stored one.</summary>
    public decimal MaxChangePercent { get; set; } = 25;

    public int RequestTimeoutSeconds { get; set; } = 10;

    public string PrimaryUrl { get; set; } = "https://open.er-api.com/v6/latest/LKR";

    public string FallbackUrl { get; set; } = "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/lkr.json";

    /// <summary>Used for a currency that has no stored rate yet (a fresh database, or no way to reach the sources).</summary>
    public FallbackRates Fallback { get; set; } = new();
}

/// <summary>LKR per 1 unit, used until the first real rate has been fetched.</summary>
public sealed class FallbackRates
{
    public decimal Usd { get; set; } = 330m;
    public decimal Eur { get; set; } = 360m;
    public decimal Gbp { get; set; } = 420m;

    public decimal For(CurrencyCode currency) => currency switch
    {
        CurrencyCode.Usd => Usd,
        CurrencyCode.Eur => Eur,
        CurrencyCode.Gbp => Gbp,
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, "Only USD, EUR and GBP have a rate."),
    };
}

/// <summary>Rates a source answered with, already as "LKR per 1 unit".</summary>
public sealed record RateQuote(string Source, IReadOnlyDictionary<CurrencyCode, decimal> RatesLkr);

/// <summary>The rates in use now. <c>AsOf</c> is null, and <c>Source</c> is <see cref="FallbackSource"/>, when nothing has been fetched yet.</summary>
public sealed record CurrentRates(IReadOnlyDictionary<CurrencyCode, decimal> RatesLkr, DateTimeOffset? AsOf, string Source)
{
    public const string FallbackSource = "fallback";
}

public enum RefreshStatus
{
    /// <summary>The stored rates are still fresh, so nothing was fetched.</summary>
    Skipped,

    /// <summary>New rates were stored.</summary>
    Updated,

    /// <summary>No source gave usable rates; the old rates stay in use.</summary>
    Failed,
}

/// <summary>What a refresh did. <c>Problems</c> are warnings for the log (a source that failed or was rejected).</summary>
public sealed record RefreshResult(RefreshStatus Status, string? Source, IReadOnlyList<string> Problems);

/// <summary>A place rates can come from. Returns null when it cannot give a complete, readable answer.</summary>
public interface IExchangeRateProvider
{
    string Name { get; }

    Task<RateQuote?> FetchAsync(CancellationToken cancellationToken = default);
}

public interface IExchangeRateRepository
{
    /// <summary>The newest stored row of each currency.</summary>
    Task<IReadOnlyList<ExchangeRate>> GetLatestAsync(CancellationToken cancellationToken = default);

    Task AddRangeAsync(IEnumerable<ExchangeRate> rows, CancellationToken cancellationToken = default);
}

/// <summary>The rates the rest of the application uses (settings now, payments later).</summary>
public interface IExchangeRates
{
    Task<CurrentRates> GetCurrentAsync(CancellationToken cancellationToken = default);
}

public static class RateCheck
{
    /// <summary>The currencies that are fetched. LKR is the base and has no rate.</summary>
    public static readonly IReadOnlyList<CurrencyCode> Currencies = [CurrencyCode.Usd, CurrencyCode.Eur, CurrencyCode.Gbp];

    /// <summary>Largest rate the <c>numeric(14,6)</c> column can hold, kept well below its limit.</summary>
    public const decimal MaxRate = 10_000_000m;

    /// <summary>Why an answer cannot be used, or null when it can. Nothing is stored unless every currency passes.</summary>
    public static string? Validate(
        IReadOnlyDictionary<CurrencyCode, decimal> candidate,
        IReadOnlyDictionary<CurrencyCode, decimal> previous,
        decimal maxChangePercent)
    {
        foreach (var currency in Currencies)
        {
            if (!candidate.TryGetValue(currency, out var rate))
            {
                return $"{EnumWire.ToWire(currency)} is missing";
            }

            if (rate <= 0 || rate > MaxRate)
            {
                return $"{EnumWire.ToWire(currency)} rate {rate} is not a usable positive number";
            }

            if (previous.TryGetValue(currency, out var last) && last > 0)
            {
                var change = Math.Abs(rate - last) / last * 100m;
                if (change > maxChangePercent)
                {
                    return $"{EnumWire.ToWire(currency)} moved {change:0.#} percent from {last} to {rate}, more than the allowed {maxChangePercent} percent";
                }
            }
        }

        return null;
    }
}

/// <summary>Fetches, checks and stores the market rates, and tells the application which rates are in use.</summary>
public sealed class ExchangeRateService(
    IEnumerable<IExchangeRateProvider> providers,
    IExchangeRateRepository rates,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<ExchangeRateOptions> options) : IExchangeRates
{
    private ExchangeRateOptions Settings => options.Value;

    /// <summary>
    /// Does nothing while the stored rates are fresh. Otherwise asks each source in turn and stores the first answer that
    /// passes the checks, all three currencies together with one fetch time. Never throws because a source failed.
    /// </summary>
    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var interval = TimeSpan.FromHours(Settings.RefreshHours);
        var latest = (await rates.GetLatestAsync(cancellationToken)).ToDictionary(row => row.Currency);

        if (RateCheck.Currencies.All(currency => latest.TryGetValue(currency, out var row) && now - row.FetchedAt < interval))
        {
            return new RefreshResult(RefreshStatus.Skipped, null, []);
        }

        var previous = latest.ToDictionary(pair => pair.Key, pair => pair.Value.RateLkr);
        var problems = new List<string>();

        foreach (var provider in providers)
        {
            RateQuote? quote;
            try
            {
                quote = await provider.FetchAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                problems.Add($"{provider.Name}: {exception.Message}");
                continue;
            }

            if (quote is null)
            {
                problems.Add($"{provider.Name}: no usable answer");
                continue;
            }

            var rejection = RateCheck.Validate(quote.RatesLkr, previous, Settings.MaxChangePercent);
            if (rejection is not null)
            {
                problems.Add($"{provider.Name}: answer rejected, {rejection}");
                continue;
            }

            await rates.AddRangeAsync(
                RateCheck.Currencies.Select(currency => new ExchangeRate
                {
                    Currency = currency,
                    RateLkr = decimal.Round(quote.RatesLkr[currency], 6),
                    Source = quote.Source,
                    FetchedAt = now,
                }),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new RefreshResult(RefreshStatus.Updated, quote.Source, problems);
        }

        return new RefreshResult(RefreshStatus.Failed, null, problems);
    }

    public async Task<CurrentRates> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var latest = (await rates.GetLatestAsync(cancellationToken)).ToDictionary(row => row.Currency);

        var result = new Dictionary<CurrencyCode, decimal>();
        foreach (var currency in RateCheck.Currencies)
        {
            result[currency] = latest.TryGetValue(currency, out var row) ? row.RateLkr : Settings.Fallback.For(currency);
        }

        if (latest.Count == 0)
        {
            return new CurrentRates(result, null, CurrentRates.FallbackSource);
        }

        var newest = latest.Values.MaxBy(row => row.FetchedAt)!;
        return new CurrentRates(result, newest.FetchedAt, newest.Source);
    }
}
