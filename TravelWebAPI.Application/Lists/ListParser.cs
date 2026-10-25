using System.Globalization;
using TravelWebAPI.Domain.Common;

namespace TravelWebAPI.Application.Lists;

/// <summary>Checks the common part of a list request and turns it into a <see cref="ListQuery"/>.</summary>
public static class ListParser
{
    /// <summary>
    /// Reads page, page size, search, sort and facets. Wrong values are added to <paramref name="errors"/> (all of them, not
    /// just the first); the returned query then uses the defaults for what was wrong, so the caller can go on checking its own
    /// filters and throw once at the end.
    /// </summary>
    public static ListQuery Parse(ListQueryRequest request, ListDefinition definition, FieldErrors errors)
    {
        var page = ReadNumber(request.Page, "page", 1, int.MaxValue, 1, ListRules.PageMessage, errors);
        var pageSize = ReadNumber(request.PageSize, "pageSize", 1, ListRules.MaxPageSize, ListRules.DefaultPageSize, ListRules.PageSizeMessage, errors);

        return new ListQuery(
            page,
            pageSize,
            CleanSearch(request.Search),
            ReadSort(request.Sort, definition, errors),
            ReadFacets(request.Facets, definition, errors));
    }

    /// <summary>The trimmed search text cut to the maximum length; empty means "no search".</summary>
    public static string CleanSearch(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length > ListRules.MaxSearchLength ? trimmed[..ListRules.MaxSearchLength].Trim() : trimmed;
    }

    /// <summary>Parses a list's own default sort, for example <c>-paidAt,-createdAt</c>.</summary>
    public static IReadOnlyList<SortTerm> ParseDefaultSort(string defaultSort)
        => [.. defaultSort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ToTerm)];

    private static int ReadNumber(string? text, string field, int min, int max, int fallback, string message, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors.Add(field, message);
        return fallback;
    }

    private static IReadOnlyList<SortTerm> ReadSort(string? text, ListDefinition definition, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ParseDefaultSort(definition.DefaultSort);
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > ListRules.MaxSortTerms)
        {
            errors.Add("sort", ListRules.TooManySortTermsMessage);
            return ParseDefaultSort(definition.DefaultSort);
        }

        var terms = new List<SortTerm>();
        foreach (var part in parts)
        {
            var term = ToTerm(part);
            var canonical = definition.SortFields.FirstOrDefault(field => string.Equals(field, term.Field, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                errors.Add("sort", ListRules.SortMessage(definition.SortFields));
                return ParseDefaultSort(definition.DefaultSort);
            }

            // Sorting by the same field twice means nothing; the first one counts.
            if (terms.All(existing => existing.Field != canonical))
            {
                terms.Add(term with { Field = canonical });
            }
        }

        return terms.Count == 0 ? ParseDefaultSort(definition.DefaultSort) : terms;
    }

    private static List<string> ReadFacets(string? text, ListDefinition definition, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var asked = new List<string>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var canonical = definition.Facets.FirstOrDefault(facet => string.Equals(facet, part, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                errors.Add("facets", ListRules.FacetMessage(definition.Facets.ToList()));
                return [];
            }

            if (!asked.Contains(canonical))
            {
                asked.Add(canonical);
            }
        }

        return asked;
    }

    private static SortTerm ToTerm(string text)
        => text.StartsWith('-') ? new SortTerm(text[1..].Trim(), true) : new SortTerm(text, false);
}

/// <summary>Turns the text of a filter into a typed value, or adds the filter's message to the errors.</summary>
public static class ListFilters
{
    /// <summary>An enum written the way the API writes it (<c>bank_transfer</c>); null when empty or wrong (wrong adds <paramref name="message"/>).</summary>
    public static TEnum? Wire<TEnum>(string? text, string field, string message, FieldErrors errors)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (EnumWire.TryFromWire<TEnum>(text.Trim(), out var value))
        {
            return value;
        }

        errors.Add(field, message);
        return null;
    }

    /// <summary>The usual "X must be one of: a, b, c" message for a filter on an enum.</summary>
    public static string OneOf<TEnum>(string label) where TEnum : struct, Enum
        => $"{label} must be one of: {string.Join(", ", Enum.GetValues<TEnum>().Select(EnumWire.ToWire))}";

    /// <summary>A calendar date as <c>YYYY-MM-DD</c>; null when empty or wrong.</summary>
    public static DateOnly? Date(string? text, string field, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors.Add(field, ListRules.DateMessage);
        return null;
    }

    /// <summary>A whole number from <paramref name="min"/> to <paramref name="max"/>; null when empty or wrong.</summary>
    public static int? Number(string? text, string field, int min, int max, string message, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= min && value <= max)
        {
            return value;
        }

        errors.Add(field, message);
        return null;
    }

    /// <summary>An id (a GUID); null when empty or wrong (wrong adds <paramref name="message"/>).</summary>
    public static Guid? Id(string? text, string field, string message, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Guid.TryParse(text.Trim(), out var id))
        {
            return id;
        }

        errors.Add(field, message);
        return null;
    }

    /// <summary>Text with the ends trimmed; null when empty.</summary>
    public static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>The <c>LIKE</c> pattern for "contains this text" with the wildcard characters of the text made plain.</summary>
public static class LikePattern
{
    public const string Escape = "\\";

    public static string Contains(string text)
        => "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
