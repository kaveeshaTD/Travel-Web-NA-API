using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelWebAPI.Api.Services;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Unit;

/// <summary>The background job on its own: it must never throw, never block start-up and stop quickly on shutdown.</summary>
public sealed class ExchangeRateRefresherTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
    private readonly ListLogger _log = new();
    private readonly ExchangeRateOptions _options = new() { StartupDelaySeconds = 0 };
    private readonly TestProvider _provider = new();
    private readonly TestRepository _repository = new();

    private ExchangeRateRefresher CreateRefresher()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExchangeRateProvider>(_provider);
        services.AddSingleton<IExchangeRateRepository>(_repository);
        services.AddSingleton<IUnitOfWork>(_repository);
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton(Options.Create(_options));
        services.AddScoped<ExchangeRateService>();

        var provider = services.BuildServiceProvider();
        return new ExchangeRateRefresher(provider.GetRequiredService<IServiceScopeFactory>(), _clock, Options.Create(_options), _log);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "Timed out waiting for the refresher");
    }

    // ---------------------------------------------------------------- one run

    [Fact]
    public async Task One_run_stores_rates_and_logs_the_source()
    {
        await CreateRefresher().RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, _repository.Rows.Count);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("updated from test-source", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_database_error_is_logged_and_does_not_escape()
    {
        _repository.FailWith = new InvalidOperationException("database is down");

        await CreateRefresher().RunOnceAsync(CancellationToken.None);

        var error = Assert.Single(_log.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal("database is down", error.Exception?.Message);
    }

    [Fact]
    public async Task A_failing_source_is_a_warning_and_nothing_is_stored()
    {
        _provider.Quote = null;

        await CreateRefresher().RunOnceAsync(CancellationToken.None);

        Assert.Empty(_repository.Rows);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("test-source: no usable answer", StringComparison.Ordinal));
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("could not be refreshed", StringComparison.Ordinal));
        Assert.Contains(_log.Entries, e => e.Message.Contains("has ever been stored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Old_rates_that_cannot_be_refreshed_are_reported_as_stale_after_three_intervals()
    {
        _provider.Quote = null;
        _repository.Rows.AddRange(
        [
            new ExchangeRate { Currency = CurrencyCode.Usd, RateLkr = 330m, Source = "old", FetchedAt = _clock.UtcNow.AddHours(-80) },
            new ExchangeRate { Currency = CurrencyCode.Eur, RateLkr = 360m, Source = "old", FetchedAt = _clock.UtcNow.AddHours(-80) },
            new ExchangeRate { Currency = CurrencyCode.Gbp, RateLkr = 420m, Source = "old", FetchedAt = _clock.UtcNow.AddHours(-80) },
        ]);

        await CreateRefresher().RunOnceAsync(CancellationToken.None);

        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("are stale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rates_that_are_only_a_day_old_are_not_called_stale()
    {
        _provider.Quote = null;
        _repository.Rows.Add(new ExchangeRate { Currency = CurrencyCode.Usd, RateLkr = 330m, Source = "old", FetchedAt = _clock.UtcNow.AddHours(-30) });

        await CreateRefresher().RunOnceAsync(CancellationToken.None);

        Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("are stale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_shutdown_during_a_fetch_ends_the_run_promptly()
    {
        _provider.BlockUntilCancelled = true;
        using var cancel = new CancellationTokenSource();
        var run = CreateRefresher().RunOnceAsync(cancel.Token);
        await WaitUntilAsync(() => _provider.Calls == 1);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Error);
    }

    // ---------------------------------------------------------------- the hosted service

    [Fact]
    public async Task When_switched_off_it_finishes_at_once_and_never_calls_a_source()
    {
        _options.Enabled = false;
        var refresher = CreateRefresher();

        await refresher.StartAsync(CancellationToken.None);
        await refresher.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await refresher.StopAsync(CancellationToken.None);

        Assert.Equal(0, _provider.Calls);
        Assert.Contains(_log.Entries, e => e.Message.Contains("switched off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task When_switched_on_it_refreshes_after_start_and_stops_promptly()
    {
        var refresher = CreateRefresher();

        await refresher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _repository.Rows.Count == 3);
        await refresher.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task Stopping_during_the_start_up_delay_does_not_call_a_source()
    {
        _options.StartupDelaySeconds = 60;
        var refresher = CreateRefresher();

        await refresher.StartAsync(CancellationToken.None);
        await refresher.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task Stopping_while_a_source_is_slow_ends_the_job_without_an_error()
    {
        _provider.BlockUntilCancelled = true;
        var refresher = CreateRefresher();

        await refresher.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _provider.Calls == 1);
        await refresher.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(refresher.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(_log.Entries, e => e.Level == LogLevel.Error);
    }

    // ---------------------------------------------------------------- fakes

    private sealed class TestProvider : IExchangeRateProvider
    {
        private int _calls;

        public string Name => "test-source";

        public int Calls => _calls;

        public bool BlockUntilCancelled { get; set; }

        public RateQuote? Quote { get; set; } = new("test-source", new Dictionary<CurrencyCode, decimal>
        {
            [CurrencyCode.Usd] = 332m,
            [CurrencyCode.Eur] = 372m,
            [CurrencyCode.Gbp] = 444m,
        });

        public async Task<RateQuote?> FetchAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (BlockUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Quote;
        }
    }

    private sealed class TestRepository : IExchangeRateRepository, IUnitOfWork
    {
        public List<ExchangeRate> Rows { get; } = [];

        public Exception? FailWith { get; set; }

        public Task<IReadOnlyList<ExchangeRate>> GetLatestAsync(CancellationToken cancellationToken = default)
            => FailWith is null
                ? Task.FromResult<IReadOnlyList<ExchangeRate>>([.. Rows.GroupBy(r => r.Currency).Select(g => g.MaxBy(r => r.FetchedAt)!)])
                : Task.FromException<IReadOnlyList<ExchangeRate>>(FailWith);

        public Task AddRangeAsync(IEnumerable<ExchangeRate> rows, CancellationToken cancellationToken = default)
        {
            Rows.AddRange(rows);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLogger : ILogger<ExchangeRateRefresher>
    {
        private readonly List<LogEntry> _entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }
    }
}
