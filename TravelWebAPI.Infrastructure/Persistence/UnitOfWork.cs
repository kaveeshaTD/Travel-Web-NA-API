using TravelWebAPI.Application.Abstractions;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        // Join the caller's transaction when there already is one.
        if (context.Database.CurrentTransaction is not null)
        {
            await operation(cancellationToken);
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
