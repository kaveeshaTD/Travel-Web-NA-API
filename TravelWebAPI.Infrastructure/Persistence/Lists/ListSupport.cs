using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Lists;

namespace TravelWebAPI.Infrastructure.Persistence.Lists;

/// <summary>One sortable field: how to order by it first and how to add it as a later tie-breaking order.</summary>
internal interface ISortKey<T>
{
    IOrderedQueryable<T> OrderBy(IQueryable<T> source, bool descending);

    IOrderedQueryable<T> ThenBy(IOrderedQueryable<T> source, bool descending);
}

internal sealed class SortKey<T, TKey>(Expression<Func<T, TKey>> key) : ISortKey<T>
{
    public IOrderedQueryable<T> OrderBy(IQueryable<T> source, bool descending)
        => descending ? source.OrderByDescending(key) : source.OrderBy(key);

    public IOrderedQueryable<T> ThenBy(IOrderedQueryable<T> source, bool descending)
        => descending ? source.ThenByDescending(key) : source.ThenBy(key);
}

/// <summary>
/// A sort on a nullable column that always puts the rows without a value last, whichever way the sort goes (PostgreSQL would
/// otherwise put them first when descending).
/// </summary>
internal sealed class NullsLastSortKey<T, TKey>(Expression<Func<T, TKey?>> key) : ISortKey<T>
    where TKey : struct
{
    private readonly Expression<Func<T, bool>> isEmpty = Expression.Lambda<Func<T, bool>>(
        Expression.Equal(key.Body, Expression.Constant(null, typeof(TKey?))),
        key.Parameters);

    public IOrderedQueryable<T> OrderBy(IQueryable<T> source, bool descending)
    {
        var first = source.OrderBy(isEmpty);
        return descending ? first.ThenByDescending(key) : first.ThenBy(key);
    }

    public IOrderedQueryable<T> ThenBy(IOrderedQueryable<T> source, bool descending)
    {
        var first = source.ThenBy(isEmpty);
        return descending ? first.ThenByDescending(key) : first.ThenBy(key);
    }
}

/// <summary>Creates a <see cref="SortMap{T}"/> whose last, always-present order is the given key (the id).</summary>
public static class SortMap
{
    public static SortMap<T> TieBreakBy<T, TKey>(Expression<Func<T, TKey>> tie) => new(new SortKey<T, TKey>(tie));
}

/// <summary>
/// The fields a list can be sorted by, keyed by the API name (<c>createdAt</c>). The key expressions are typed, so the
/// database orders by the real column. A last tie-breaker (the id) always follows, which keeps rows with equal values in one
/// fixed order from page to page.
/// </summary>
public sealed class SortMap<T>
{
    private readonly Dictionary<string, ISortKey<T>> keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISortKey<T> tieBreaker;

    internal SortMap(ISortKey<T> tieBreaker) => this.tieBreaker = tieBreaker;

    public IReadOnlyCollection<string> Names => keys.Keys;

    public SortMap<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
    {
        keys[name] = new SortKey<T, TKey>(key);
        return this;
    }

    /// <summary>Adds a sort on a nullable value column whose empty rows always come last.</summary>
    public SortMap<T> AddNullsLast<TKey>(string name, Expression<Func<T, TKey?>> key)
        where TKey : struct
    {
        keys[name] = new NullsLastSortKey<T, TKey>(key);
        return this;
    }

    public IOrderedQueryable<T> Apply(IQueryable<T> source, IReadOnlyList<SortTerm> terms)
    {
        IOrderedQueryable<T>? ordered = null;
        foreach (var term in terms)
        {
            var key = keys[term.Field];
            ordered = ordered is null ? key.OrderBy(source, term.Descending) : key.ThenBy(ordered, term.Descending);
        }

        var last = terms[^1].Descending;
        return ordered is null ? tieBreaker.OrderBy(source, last) : tieBreaker.ThenBy(ordered, last);
    }
}

/// <summary>"Contains this text, ignoring letter case" over several text fields, as a parameterized <c>ILIKE</c>.</summary>
public static class TextSearch
{
    private sealed record Holder(string Value);

    public static IQueryable<T> Where<T>(
        IQueryable<T> source,
        string text,
        IReadOnlyList<Expression<Func<T, string?>>> fields,
        IReadOnlyList<Expression<Func<T, bool>>>? alsoMatching = null)
    {
        if (text.Length == 0 || (fields.Count == 0 && (alsoMatching is null || alsoMatching.Count == 0)))
        {
            return source;
        }

        var row = Expression.Parameter(typeof(T), "row");
        // The pattern is read from a property of a captured object so EF sends it as a parameter, never as SQL text.
        var pattern = Expression.Property(Expression.Constant(new Holder(LikePattern.Contains(text))), nameof(Holder.Value));
        var escape = Expression.Constant(LikePattern.Escape);
        var functions = Expression.Constant(EF.Functions);
        var ilike = typeof(NpgsqlDbFunctionsExtensions).GetMethod(
            nameof(NpgsqlDbFunctionsExtensions.ILike),
            [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

        Expression? any = null;
        foreach (var field in fields)
        {
            var value = new ParameterReplacer(field.Parameters[0], row).Visit(field.Body);
            var match = Expression.Call(ilike, functions, value, pattern, escape);
            any = any is null ? match : Expression.OrElse(any, match);
        }

        // Extra ways a row can match (for example an enum column whose written form contains the text).
        foreach (var extra in alsoMatching ?? [])
        {
            var body = new ParameterReplacer(extra.Parameters[0], row).Visit(extra.Body);
            any = any is null ? body : Expression.OrElse(any, body);
        }

        return source.Where(Expression.Lambda<Func<T, bool>>(any!, row));
    }

    private sealed class ParameterReplacer(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}

/// <summary>
/// The named filters of one request. A filter that was not asked for is simply not added. <see cref="Apply"/> can leave one
/// out, which is how a facet counts the rows its own filter would show.
/// </summary>
public sealed class FilterSet<T>
{
    private readonly List<(string Name, Expression<Func<T, bool>> Predicate)> filters = [];

    public FilterSet<T> Add(string name, Expression<Func<T, bool>>? predicate)
    {
        if (predicate is not null)
        {
            filters.Add((name, predicate));
        }

        return this;
    }

    public IQueryable<T> Apply(IQueryable<T> source, string? except = null)
    {
        foreach (var (name, predicate) in filters)
        {
            if (!string.Equals(name, except, StringComparison.OrdinalIgnoreCase))
            {
                source = source.Where(predicate);
            }
        }

        return source;
    }
}

/// <summary>Counts the rows per value of one field.</summary>
public interface IFacet<T>
{
    string Name { get; }

    Task<IReadOnlyDictionary<string, int>> CountAsync(IQueryable<T> source, CancellationToken cancellationToken);
}

public sealed class Facet<T, TKey>(string name, Expression<Func<T, TKey>> key, Func<TKey, string> label, IReadOnlyList<string>? allValues = null) : IFacet<T>
{
    private sealed class Row
    {
        public TKey Key { get; set; } = default!;

        public int Count { get; set; }
    }

    public string Name => name;

    public async Task<IReadOnlyDictionary<string, int>> CountAsync(IQueryable<T> source, CancellationToken cancellationToken)
    {
        var rows = await source
            .GroupBy(key)
            .Select(group => new Row { Key = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, int>();
        // Values that exist for the field but have no rows are listed with 0, so a tab can always show its number.
        foreach (var value in allValues ?? [])
        {
            counts[value] = 0;
        }

        foreach (var row in rows)
        {
            counts[label(row.Key)] = row.Count;
        }

        return counts;
    }
}

/// <summary>What a list declares once: its text fields, its sortable fields and its facets.</summary>
public sealed class ListSpec<T>
{
    private readonly Dictionary<string, IFacet<T>> facets = new(StringComparer.OrdinalIgnoreCase);

    public ListSpec(SortMap<T> sort, IReadOnlyList<Expression<Func<T, string?>>> searchFields)
    {
        Sort = sort;
        SearchFields = searchFields;
    }

    public SortMap<T> Sort { get; }

    public IReadOnlyList<Expression<Func<T, string?>>> SearchFields { get; }

    public IReadOnlyCollection<string> FacetNames => facets.Keys;

    private Func<string, Expression<Func<T, bool>>?>? extraSearch;

    /// <summary>Adds a way to match the search text that is not a plain text column; return null when the text cannot match.</summary>
    public ListSpec<T> AlsoMatch(Func<string, Expression<Func<T, bool>>?> build)
    {
        extraSearch = build;
        return this;
    }

    public ListSpec<T> AddFacet(IFacet<T> facet)
    {
        facets[facet.Name] = facet;
        return this;
    }

    /// <summary>The sort and facet names as the parser expects them. A test checks this against the list's definition.</summary>
    public (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) Names => (Sort.Names, facets.Keys);

    /// <summary>
    /// Runs the list: search, filters, the total, the sorted page, and the facet counts that were asked for. Facet counts
    /// follow the search and every filter except the facet's own.
    /// </summary>
    public async Task<PagedResult<T>> RunAsync(IQueryable<T> source, FilterSet<T> filters, ListQuery query, CancellationToken cancellationToken)
    {
        var extra = query.Search.Length > 0 ? extraSearch?.Invoke(query.Search) : null;
        var searched = TextSearch.Where(source, query.Search, SearchFields, extra is null ? null : [extra]);
        var filtered = filters.Apply(searched);

        var total = await filtered.CountAsync(cancellationToken);
        var items = await Sort.Apply(filtered, query.Sort).Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        Dictionary<string, IReadOnlyDictionary<string, int>>? counted = null;
        foreach (var name in query.Facets)
        {
            counted ??= [];
            var facet = facets[name];
            counted[facet.Name] = await facet.CountAsync(filters.Apply(searched, except: facet.Name), cancellationToken);
        }

        return Paged.Of(items, query, total, counted);
    }
}
