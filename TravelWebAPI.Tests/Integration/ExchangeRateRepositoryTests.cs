using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The rate repository against a real PostgreSQL database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ExchangeRateRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ExchangeRate Row(CurrencyCode currency, decimal rate, TimeSpan age, string source = "test")
        => new() { Currency = currency, RateLkr = rate, Source = source, FetchedAt = Noon - age };

    private async Task<(string Connection, ExchangeRateRepository Repository, AppDbContext Db)> OpenAsync()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var db = PostgresFixture.CreateContext(connection);
        return (connection, new ExchangeRateRepository(db), db);
    }

    [Fact]
    public async Task An_empty_table_has_no_latest_rates()
    {
        var (_, repository, db) = await OpenAsync();
        await using var _ = db;

        Assert.Empty(await repository.GetLatestAsync());
    }

    [Fact]
    public async Task The_newest_row_of_each_currency_is_returned_and_history_is_kept()
    {
        var (connection, repository, db) = await OpenAsync();
        await using var _ = db;
        await repository.AddRangeAsync(
        [
            Row(CurrencyCode.Usd, 300m, TimeSpan.FromDays(2)),
            Row(CurrencyCode.Usd, 331m, TimeSpan.FromDays(1)),
            Row(CurrencyCode.Usd, 332m, TimeSpan.FromHours(1), "newest"),
            Row(CurrencyCode.Eur, 360m, TimeSpan.FromDays(1)),
            Row(CurrencyCode.Gbp, 420m, TimeSpan.FromHours(2)),
        ]);
        await db.SaveChangesAsync();

        var latest = await repository.GetLatestAsync();

        Assert.Equal(3, latest.Count);
        Assert.Equal(332m, latest.Single(r => r.Currency == CurrencyCode.Usd).RateLkr);
        Assert.Equal("newest", latest.Single(r => r.Currency == CurrencyCode.Usd).Source);
        Assert.Equal(360m, latest.Single(r => r.Currency == CurrencyCode.Eur).RateLkr);
        Assert.Equal(420m, latest.Single(r => r.Currency == CurrencyCode.Gbp).RateLkr);
        Assert.Equal(5L, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM exchange_rates"));
    }

    [Fact]
    public async Task Rows_are_stored_with_wire_currency_codes_exact_rates_and_the_given_time()
    {
        var (connection, repository, db) = await OpenAsync();
        await using var _ = db;
        await repository.AddRangeAsync([Row(CurrencyCode.Usd, 332.115609m, TimeSpan.Zero, "open.er-api.com")]);
        await db.SaveChangesAsync();

        Assert.Equal("USD", await PostgresFixture.ScalarAsync<string>(connection, "SELECT currency FROM exchange_rates"));
        Assert.Equal(332.115609m, await PostgresFixture.ScalarAsync<decimal>(connection, "SELECT rate_lkr FROM exchange_rates"));
        Assert.Equal("open.er-api.com", await PostgresFixture.ScalarAsync<string>(connection, "SELECT source FROM exchange_rates"));
        Assert.Equal(Noon, new DateTimeOffset(await PostgresFixture.ScalarAsync<DateTime>(connection, "SELECT fetched_at FROM exchange_rates"), TimeSpan.Zero));
    }

    [Fact]
    public async Task Two_rows_with_the_same_time_count_once()
    {
        var (_, repository, db) = await OpenAsync();
        await using var _ = db;
        await repository.AddRangeAsync(
        [
            Row(CurrencyCode.Usd, 332m, TimeSpan.Zero),
            Row(CurrencyCode.Usd, 332.1m, TimeSpan.Zero),
        ]);
        await db.SaveChangesAsync();

        Assert.Single(await repository.GetLatestAsync());
    }

    [Fact]
    public async Task The_service_works_on_top_of_the_real_repository()
    {
        var (connection, repository, db) = await OpenAsync();
        await using var _ = db;
        var clock = new Unit.FakeClock(Noon);
        var provider = new FixedProvider(new RateQuote("fixed", new Dictionary<CurrencyCode, decimal>
        {
            [CurrencyCode.Usd] = 332m,
            [CurrencyCode.Eur] = 361m,
            [CurrencyCode.Gbp] = 421m,
        }));
        var service = new ExchangeRateService(
            [provider], repository, new UnitOfWork(db), clock, Microsoft.Extensions.Options.Options.Create(new ExchangeRateOptions()));

        var first = await service.RefreshAsync();
        var second = await service.RefreshAsync();
        clock.Advance(TimeSpan.FromHours(25));
        var third = await service.RefreshAsync();
        var current = await service.GetCurrentAsync();

        Assert.Equal([RefreshStatus.Updated, RefreshStatus.Skipped, RefreshStatus.Updated], [first.Status, second.Status, third.Status]);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(6L, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM exchange_rates"));
        Assert.Equal(332m, current.RatesLkr[CurrencyCode.Usd]);
        Assert.Equal(clock.UtcNow, current.AsOf);
        Assert.Equal("fixed", current.Source);
    }

    private sealed class FixedProvider(RateQuote quote) : IExchangeRateProvider
    {
        public string Name => quote.Source;

        public int Calls { get; private set; }

        public Task<RateQuote?> FetchAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<RateQuote?>(quote);
        }
    }
}
