using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Unit;

public sealed class RateCheckTests
{
    private static Dictionary<CurrencyCode, decimal> Rates(decimal usd = 330m, decimal eur = 360m, decimal gbp = 420m)
        => new() { [CurrencyCode.Usd] = usd, [CurrencyCode.Eur] = eur, [CurrencyCode.Gbp] = gbp };

    private static readonly Dictionary<CurrencyCode, decimal> NoPrevious = [];

    [Fact]
    public void A_complete_positive_answer_is_accepted_when_there_is_nothing_to_compare_with()
        => Assert.Null(RateCheck.Validate(Rates(), NoPrevious, 25));

    [Theory]
    [InlineData(CurrencyCode.Usd)]
    [InlineData(CurrencyCode.Eur)]
    [InlineData(CurrencyCode.Gbp)]
    public void A_missing_currency_is_rejected(CurrencyCode missing)
    {
        var answer = Rates();
        answer.Remove(missing);

        var reason = RateCheck.Validate(answer, NoPrevious, 25);

        Assert.Contains(EnumWire(missing), reason, StringComparison.Ordinal);
        Assert.Contains("missing", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_001)]
    public void A_rate_that_is_not_a_usable_positive_number_is_rejected(double rate)
        => Assert.NotNull(RateCheck.Validate(Rates(usd: (decimal)rate), NoPrevious, 25));

    [Fact]
    public void A_jump_above_the_limit_is_rejected_and_the_reason_names_the_currency()
    {
        var reason = RateCheck.Validate(Rates(usd: 464m), Rates(), 25);

        Assert.Contains("USD", reason, StringComparison.Ordinal);
        Assert.Contains("40", reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(412.5)]
    [InlineData(247.5)]
    public void A_move_of_exactly_the_limit_is_accepted(double usd)
        => Assert.Null(RateCheck.Validate(Rates(usd: (decimal)usd), Rates(), 25));

    [Fact]
    public void A_drop_above_the_limit_is_rejected_too()
        => Assert.NotNull(RateCheck.Validate(Rates(usd: 200m), Rates(), 25));

    [Fact]
    public void Only_currencies_with_an_earlier_rate_are_compared()
    {
        var previous = new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = 330m };

        Assert.Null(RateCheck.Validate(Rates(eur: 10_000m), previous, 25));
    }

    [Fact]
    public void Extra_currencies_in_the_answer_are_ignored()
    {
        var answer = Rates();
        answer[CurrencyCode.Lkr] = 1m;

        Assert.Null(RateCheck.Validate(answer, NoPrevious, 25));
    }

    private static string EnumWire(CurrencyCode code) => TravelWebAPI.Domain.Common.EnumWire.ToWire(code);
}

public sealed class ExchangeRateServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeRateRepository _repository = new();
    private readonly ExchangeRateOptions _options = new();
    private readonly List<FakeProvider> _providers = [];

    private ExchangeRateService CreateService() => new(_providers, _repository, _repository, _clock, Options.Create(_options));

    private FakeProvider AddProvider(string name, decimal usd = 330m, decimal eur = 360m, decimal gbp = 420m)
    {
        var provider = new FakeProvider(name, new RateQuote(name, new Dictionary<CurrencyCode, decimal>
        {
            [CurrencyCode.Usd] = usd,
            [CurrencyCode.Eur] = eur,
            [CurrencyCode.Gbp] = gbp,
        }));
        _providers.Add(provider);
        return provider;
    }

    private void Store(CurrencyCode currency, decimal rate, TimeSpan age, string source = "old-source")
        => _repository.Rows.Add(new ExchangeRate { Currency = currency, RateLkr = rate, Source = source, FetchedAt = _clock.UtcNow - age });

    private void StoreAll(TimeSpan age, decimal usd = 330m, decimal eur = 360m, decimal gbp = 420m)
    {
        Store(CurrencyCode.Usd, usd, age);
        Store(CurrencyCode.Eur, eur, age);
        Store(CurrencyCode.Gbp, gbp, age);
    }

    // ---------------------------------------------------------------- refresh

    [Fact]
    public async Task The_first_refresh_stores_three_rows_with_one_time_and_the_source()
    {
        AddProvider("primary", usd: 332.123456789m);

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Updated, result.Status);
        Assert.Equal("primary", result.Source);
        Assert.Equal(3, _repository.Rows.Count);
        Assert.All(_repository.Rows, row =>
        {
            Assert.Equal(_clock.UtcNow, row.FetchedAt);
            Assert.Equal("primary", row.Source);
        });
        Assert.Equal(332.123457m, _repository.Rows.Single(r => r.Currency == CurrencyCode.Usd).RateLkr);
        Assert.Equal([CurrencyCode.Usd, CurrencyCode.Eur, CurrencyCode.Gbp], _repository.Rows.Select(r => r.Currency));
        Assert.Equal(1, _repository.Saves);
    }

    [Fact]
    public async Task Fresh_rates_skip_every_source()
    {
        var provider = AddProvider("primary");
        StoreAll(TimeSpan.FromHours(2));

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Skipped, result.Status);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(3, _repository.Rows.Count);
    }

    [Fact]
    public async Task Rates_older_than_the_interval_are_refreshed_and_history_is_kept()
    {
        var provider = AddProvider("primary", usd: 335m);
        StoreAll(TimeSpan.FromHours(25));

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Updated, result.Status);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(6, _repository.Rows.Count);
    }

    [Fact]
    public async Task One_stale_currency_is_enough_to_refresh_all_three()
    {
        var provider = AddProvider("primary");
        Store(CurrencyCode.Usd, 330m, TimeSpan.FromHours(1));
        Store(CurrencyCode.Eur, 360m, TimeSpan.FromHours(1));
        Store(CurrencyCode.Gbp, 420m, TimeSpan.FromHours(30));

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Updated, result.Status);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task The_refresh_interval_comes_from_the_options()
    {
        _options.RefreshHours = 1;
        AddProvider("primary");
        StoreAll(TimeSpan.FromHours(2));

        Assert.Equal(RefreshStatus.Updated, (await CreateService().RefreshAsync()).Status);
    }

    [Fact]
    public async Task When_the_primary_source_fails_the_second_one_is_used_and_named_as_the_source()
    {
        var primary = AddProvider("primary");
        primary.Quote = null;
        var second = AddProvider("second", usd: 331m);

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Updated, result.Status);
        Assert.Equal("second", result.Source);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, second.Calls);
        Assert.All(_repository.Rows, row => Assert.Equal("second", row.Source));
        Assert.Contains(result.Problems, p => p.StartsWith("primary:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_source_that_throws_is_treated_like_one_that_fails()
    {
        var primary = AddProvider("primary");
        primary.Throw = new HttpRequestException("connection refused");
        AddProvider("second");

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Updated, result.Status);
        Assert.Contains(result.Problems, p => p.Contains("connection refused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_rejected_answer_falls_through_to_the_next_source()
    {
        StoreAll(TimeSpan.FromHours(30));
        AddProvider("primary", usd: 600m);
        AddProvider("second", usd: 335m);

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Updated, result.Status);
        Assert.Equal("second", result.Source);
        Assert.Contains(result.Problems, p => p.Contains("rejected", StringComparison.Ordinal));
        Assert.DoesNotContain(_repository.Rows, row => row.RateLkr == 600m);
    }

    [Fact]
    public async Task When_every_source_fails_nothing_is_stored_and_the_problems_are_reported()
    {
        StoreAll(TimeSpan.FromHours(30));
        AddProvider("primary").Quote = null;
        AddProvider("second", gbp: 0m);

        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Failed, result.Status);
        Assert.Null(result.Source);
        Assert.Equal(2, result.Problems.Count);
        Assert.Equal(3, _repository.Rows.Count);
        Assert.Equal(0, _repository.Saves);
    }

    [Fact]
    public async Task With_no_sources_at_all_the_refresh_fails_quietly()
    {
        var result = await CreateService().RefreshAsync();

        Assert.Equal(RefreshStatus.Failed, result.Status);
        Assert.Empty(_repository.Rows);
    }

    [Fact]
    public async Task A_cancelled_fetch_is_not_swallowed()
    {
        AddProvider("primary").Throw = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService().RefreshAsync());
    }

    [Fact]
    public async Task The_jump_check_uses_the_newest_stored_rate()
    {
        Store(CurrencyCode.Usd, 100m, TimeSpan.FromHours(100));
        StoreAll(TimeSpan.FromHours(30));
        AddProvider("primary", usd: 340m);

        Assert.Equal(RefreshStatus.Updated, (await CreateService().RefreshAsync()).Status);
    }

    // ---------------------------------------------------------------- current rates

    [Fact]
    public async Task The_newest_row_of_each_currency_is_the_current_rate()
    {
        Store(CurrencyCode.Usd, 300m, TimeSpan.FromDays(3), "older");
        Store(CurrencyCode.Usd, 332m, TimeSpan.FromHours(3), "newest");
        Store(CurrencyCode.Eur, 360m, TimeSpan.FromHours(3), "newest");
        Store(CurrencyCode.Gbp, 420m, TimeSpan.FromHours(3), "newest");

        var current = await CreateService().GetCurrentAsync();

        Assert.Equal(332m, current.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(_clock.UtcNow - TimeSpan.FromHours(3), current.AsOf);
        Assert.Equal("newest", current.Source);
    }

    [Fact]
    public async Task A_failed_refresh_leaves_the_old_rates_and_their_time_in_use()
    {
        StoreAll(TimeSpan.FromHours(30), usd: 331m);
        AddProvider("primary").Quote = null;
        var service = CreateService();

        await service.RefreshAsync();
        var current = await service.GetCurrentAsync();

        Assert.Equal(331m, current.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(_clock.UtcNow - TimeSpan.FromHours(30), current.AsOf);
    }

    [Fact]
    public async Task With_nothing_stored_the_configured_fallback_rates_are_used_and_marked()
    {
        _options.Fallback = new FallbackRates { Usd = 301m, Eur = 351m, Gbp = 401m };

        var current = await CreateService().GetCurrentAsync();

        Assert.Equal(301m, current.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(351m, current.RatesLkr[CurrencyCode.Eur]);
        Assert.Equal(401m, current.RatesLkr[CurrencyCode.Gbp]);
        Assert.Null(current.AsOf);
        Assert.Equal(CurrentRates.FallbackSource, current.Source);
    }

    [Fact]
    public async Task A_currency_without_a_row_takes_its_fallback_while_others_use_stored_rates()
    {
        _options.Fallback = new FallbackRates { Gbp = 401m };
        Store(CurrencyCode.Usd, 332m, TimeSpan.FromHours(1), "newest");
        Store(CurrencyCode.Eur, 361m, TimeSpan.FromHours(1), "newest");

        var current = await CreateService().GetCurrentAsync();

        Assert.Equal(332m, current.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(401m, current.RatesLkr[CurrencyCode.Gbp]);
        Assert.Equal("newest", current.Source);
        Assert.NotNull(current.AsOf);
    }

    [Fact]
    public async Task The_current_rates_never_contain_lkr()
    {
        var current = await CreateService().GetCurrentAsync();

        Assert.Equal(3, current.RatesLkr.Count);
        Assert.False(current.RatesLkr.ContainsKey(CurrencyCode.Lkr));
    }

    // ---------------------------------------------------------------- fakes

    private sealed class FakeProvider(string name, RateQuote? quote) : IExchangeRateProvider
    {
        public string Name { get; } = name;

        public RateQuote? Quote { get; set; } = quote;

        public Exception? Throw { get; set; }

        public int Calls { get; private set; }

        public Task<RateQuote?> FetchAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Throw is null ? Task.FromResult(Quote) : Task.FromException<RateQuote?>(Throw);
        }
    }

    private sealed class FakeRateRepository : IExchangeRateRepository, IUnitOfWork
    {
        public List<ExchangeRate> Rows { get; } = [];

        public int Saves { get; private set; }

        public Task<IReadOnlyList<ExchangeRate>> GetLatestAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExchangeRate>>([.. Rows.GroupBy(r => r.Currency).Select(g => g.MaxBy(r => r.FetchedAt)!)]);

        public Task AddRangeAsync(IEnumerable<ExchangeRate> rows, CancellationToken cancellationToken = default)
        {
            Rows.AddRange(rows);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
