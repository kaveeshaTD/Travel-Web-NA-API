using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class ExchangeRateRepository(AppDbContext context) : IExchangeRateRepository
{
    /// <summary>The newest row of each currency. Two rows with the same time (two instances fetching together) count once.</summary>
    public async Task<IReadOnlyList<ExchangeRate>> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.ExchangeRates
            .AsNoTracking()
            .Where(rate => rate.FetchedAt == context.ExchangeRates
                .Where(other => other.Currency == rate.Currency)
                .Max(other => other.FetchedAt))
            .ToListAsync(cancellationToken);

        return [.. rows.GroupBy(rate => rate.Currency).Select(group => group.First())];
    }

    public Task AddRangeAsync(IEnumerable<ExchangeRate> rows, CancellationToken cancellationToken = default)
        => context.ExchangeRates.AddRangeAsync(rows, cancellationToken);
}
