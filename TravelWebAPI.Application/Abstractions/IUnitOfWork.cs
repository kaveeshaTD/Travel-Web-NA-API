namespace TravelWebAPI.Application.Abstractions;

/// <summary>Commits everything the repositories changed as one transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one database transaction. The transaction is committed
    /// when the operation completes and rolled back when it throws.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}
