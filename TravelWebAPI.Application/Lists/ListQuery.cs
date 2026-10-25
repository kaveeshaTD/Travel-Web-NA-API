using System.Text.Json.Serialization;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Lists;

/// <summary>The limits and messages every paged list shares.</summary>
public static class ListRules
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;
    public const int MaxSortTerms = 3;

    public const string PageMessage = "Page must be a whole number of 1 or more";
    public const string PageSizeMessage = "Page size must be a whole number from 1 to 100";
    public const string TooManySortTermsMessage = "Sort by at most 3 fields";
    public const string DateMessage = "Choose a valid date";

    public static string SortMessage(IEnumerable<string> fields) => $"Sort by one of: {string.Join(", ", fields)}";

    public static string FacetMessage(IReadOnlyCollection<string> facets)
        => facets.Count == 0 ? "This list has no facets" : $"Facets can be: {string.Join(", ", facets)}";
}

/// <summary>One field of a sort, for example <c>-createdAt</c> is <c>createdAt</c> descending.</summary>
public sealed record SortTerm(string Field, bool Descending);

/// <summary>
/// The paging, search, sort and facet part of a list request after it has been checked. <see cref="Sort"/> is never empty
/// (the list's default is used when the caller sent none).
/// </summary>
public sealed record ListQuery(int Page, int PageSize, string Search, IReadOnlyList<SortTerm> Sort, IReadOnlyList<string> Facets)
{
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>
/// What a list request carries before it is checked. Everything is text so a wrong value (<c>page=abc</c>) reaches the
/// parser and gets the contract's message instead of a generic binding error. Each list derives its own request to add
/// its filters.
/// </summary>
public class ListQueryRequest
{
    /// <summary>Page number, 1 or more (default 1).</summary>
    public string? Page { get; set; }

    /// <summary>Rows per page, 1 to 100 (default 20).</summary>
    public string? PageSize { get; set; }

    /// <summary>Text to find in the list's searchable fields (contains, ignoring letter case).</summary>
    public string? Search { get; set; }

    /// <summary>Fields to sort by, comma separated, a leading <c>-</c> for descending, for example <c>-createdAt,name</c>.</summary>
    public string? Sort { get; set; }

    /// <summary>Fields to count values of, comma separated, to build filter tabs with counts.</summary>
    public string? Facets { get; set; }
}

/// <summary>What a list declares to the parser: the fields it can sort and count by, and its default order.</summary>
public sealed record ListDefinition(IReadOnlyList<string> SortFields, string DefaultSort, IReadOnlyList<string> Facets)
{
    public ListDefinition(IReadOnlyList<string> sortFields, string defaultSort)
        : this(sortFields, defaultSort, [])
    {
    }
}

/// <summary>One page of a list, with the totals the caller needs to draw a pager and, when asked, facet counts.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? Facets = null)
{
    /// <summary>The same page with each item turned into something else (entity to DTO).</summary>
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map)
        => new([.. Items.Select(map)], Page, PageSize, TotalCount, TotalPages, Facets);
}

/// <summary>Builds the answer for a page of rows.</summary>
public static class Paged
{
    public static PagedResult<T> Of<T>(IReadOnlyList<T> items, ListQuery query, int totalCount, IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? facets = null)
        => new(items, query.Page, query.PageSize, totalCount, (int)Math.Ceiling(totalCount / (double)query.PageSize), facets);
}

/// <summary>Field messages collected while a request is checked; the first message of a field wins.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, string> fields = [];

    public bool Any => fields.Count > 0;

    public IReadOnlyDictionary<string, string> Fields => fields;

    public void Add(string field, string message) => fields.TryAdd(field, message);

    /// <summary>Throws one validation error with every wrong field, or does nothing when everything was fine.</summary>
    public void ThrowIfAny()
    {
        if (fields.Count > 0)
        {
            throw new ValidationException(fields);
        }
    }
}
