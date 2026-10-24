namespace TravelWebAPI.Application.Abstractions;

/// <summary>
/// Basic data access for one entity type. Repositories never save; <see cref="IUnitOfWork"/> does.
/// </summary>
public interface IRepository<TEntity> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    void Remove(TEntity entity);
}
