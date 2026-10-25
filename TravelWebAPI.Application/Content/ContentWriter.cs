using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Content;

public static class ContentLimits
{
    /// <summary>The most names a "still in use" conflict lists.</summary>
    public const int MaxDependents = 20;
}

/// <summary>Data access shared by every ordered content collection (packages, destinations, vehicles, services, FAQs, gallery).</summary>
public interface IContentRepository<T> : IRepository<T> where T : class, IOrdered
{
    /// <summary>Every record in display order, with its child rows. Tracked, so callers can change them.</summary>
    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One record with its child rows, or null. Tracked.</summary>
    Task<T?> FindAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Ids that start with <paramref name="prefix"/>, used to find a free slug.</summary>
    Task<IReadOnlySet<string>> ExistingIdsAsync(string prefix, CancellationToken cancellationToken = default);

    Task<int> NextSortOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>What still uses the record (at most <see cref="ContentLimits.MaxDependents"/> labels). Empty when it can be deleted.</summary>
    Task<IReadOnlyList<string>> GetDependentsAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// The create, delete and reorder steps every content collection shares. A service owns one writer and keeps
/// its own rules (validation, mapping, children) around it.
/// </summary>
public sealed class ContentWriter<T>(IContentRepository<T> repository, IUnitOfWork unitOfWork, string label)
    where T : class, IOrdered
{
    /// <summary>Lower-case name used in messages, for example "package" or "gallery image".</summary>
    public string Label { get; } = label;

    public string NotFoundMessage => $"{char.ToUpperInvariant(Label[0])}{Label[1..]} not found";

    /// <summary>Gives the entity a free slug id and the last position, then saves it.</summary>
    public async Task AddAsync(T entity, string? slugSource, CancellationToken cancellationToken)
    {
        var taken = await repository.ExistingIdsAsync(SlugGenerator.LookupPrefix(slugSource), cancellationToken);
        entity.Id = SlugGenerator.Unique(slugSource, taken);
        entity.SortOrder = await repository.NextSortOrderAsync(cancellationToken);

        await repository.AddAsync(entity, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<T> GetAsync(string id, CancellationToken cancellationToken)
        => await repository.FindAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await GetAsync(id, cancellationToken);

        var dependents = await repository.GetDependentsAsync(id, cancellationToken);
        if (dependents.Count > 0)
        {
            throw new DependentsExistException(Label, dependents);
        }

        repository.Remove(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Stores the new order in one transaction and returns every record in that order.</summary>
    public async Task<IReadOnlyList<T>> ReorderAsync(ReorderRequest request, CancellationToken cancellationToken)
    {
        var current = await repository.ListAsync(cancellationToken);
        var ordered = Ordering.Reorder(current, request, item => item.Id);

        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            for (var position = 0; position < ordered.Count; position++)
            {
                ordered[position].SortOrder = position;
            }

            await unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return ordered;
    }
}
