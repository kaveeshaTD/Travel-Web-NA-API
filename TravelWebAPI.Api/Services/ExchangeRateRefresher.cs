using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Rates;

namespace TravelWebAPI.Api.Services;

/// <summary>
/// Keeps the exchange rates up to date without anybody's help. A few seconds after start-up, and then every hour, it asks
/// <see cref="ExchangeRateService"/> to refresh; the service itself does nothing while the stored rates are younger than the
/// refresh interval (24 hours), so restarts and several instances do not call the sources again and again.
/// Nothing that goes wrong in here can stop the application.
/// </summary>
public sealed class ExchangeRateRefresher(
    IServiceScopeFactory scopes,
    IClock clock,
    IOptions<ExchangeRateOptions> options,
    ILogger<ExchangeRateRefresher> logger) : BackgroundService
{
    /// <summary>How often the age of the stored rates is checked.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Exchange rate refresh is switched off (ExchangeRates:Enabled is false)");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds), stoppingToken);

            using var timer = new PeriodicTimer(CheckEvery);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The application is shutting down.
        }
    }

    /// <summary>One check. Public so tests can run it without waiting for the timer. Only a shutdown escapes it.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<ExchangeRateService>();

            var result = await service.RefreshAsync(cancellationToken);

            foreach (var problem in result.Problems)
            {
                logger.LogWarning("Exchange rate source problem: {Problem}", problem);
            }

            switch (result.Status)
            {
                case RefreshStatus.Updated:
                    logger.LogInformation("Exchange rates updated from {Source}", result.Source);
                    break;
                case RefreshStatus.Failed:
                    logger.LogWarning("Exchange rates could not be refreshed; the last stored rates stay in use");
                    await WarnIfStaleAsync(service, cancellationToken);
                    break;
                default:
                    logger.LogDebug("Exchange rates are still fresh");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exchange rate refresh failed; it will be tried again at the next check");
        }
    }

    private async Task WarnIfStaleAsync(ExchangeRateService rates, CancellationToken cancellationToken)
    {
        var current = await rates.GetCurrentAsync(cancellationToken);
        var limit = TimeSpan.FromHours(options.Value.RefreshHours * 3);

        if (current.AsOf is null)
        {
            logger.LogWarning("No exchange rate has ever been stored; the configured fallback rates are in use");
        }
        else if (clock.UtcNow - current.AsOf > limit)
        {
            logger.LogWarning("Exchange rates are stale: the newest were fetched at {FetchedAt:u}", current.AsOf);
        }
    }
}
