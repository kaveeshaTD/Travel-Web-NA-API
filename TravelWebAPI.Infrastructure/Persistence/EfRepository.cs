using TravelWebAPI.Application.Abstractions;

namespace TravelWebAPI.Infrastructure.Persistence;

public class EfRepository<TEntity>(AppDbContext context) : IRepository<TEntity>
    where TEntity : class
{
    protected AppDbContext Context { get; } = context;

    public async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => await Context.Set<TEntity>().FindAsync([id], cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await Context.Set<TEntity>().AddAsync(entity, cancellationToken);

    public void Remove(TEntity entity) => Context.Set<TEntity>().Remove(entity);
}
