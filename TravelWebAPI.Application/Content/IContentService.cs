namespace TravelWebAPI.Application.Content;

/// <summary>A content record as returned by the API. Every one has a slug id.</summary>
public interface IContentDto
{
    string Id { get; }
}

/// <summary>The operations every content collection offers, so one controller shape serves all of them.</summary>
public interface IContentService<in TRequest, TDto> where TDto : IContentDto
{
    /// <summary>
    /// All records in display order. <paramref name="includeUnpublished"/> only matters for collections that can
    /// hide records (packages) and only for admins; it is ignored otherwise.
    /// </summary>
    Task<IReadOnlyList<TDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default);

    Task<TDto> GetAsync(string id, CancellationToken ct = default);

    Task<TDto> CreateAsync(TRequest request, CancellationToken ct = default);

    Task<TDto> UpdateAsync(string id, TRequest request, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<TDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default);
}
