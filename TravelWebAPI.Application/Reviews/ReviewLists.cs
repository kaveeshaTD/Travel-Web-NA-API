using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Reviews;

/// <summary>The review list request: the common paging, sorting and search plus the review filters.</summary>
public sealed class ReviewListRequest : ListQueryRequest
{
    /// <summary>Admin only: <c>1</c> lists every review whatever its status. Anyone else still gets the published ones.</summary>
    public string? All { get; set; }

    /// <summary>Only reviews with this rating, 1 to 5.</summary>
    public string? Rating { get; set; }

    /// <summary>Admin only (with <c>all=1</c>): only reviews with this status: pending, published or rejected.</summary>
    public string? Status { get; set; }
}

/// <summary>The filters of a review list after they have been checked. A missing value means "do not filter".</summary>
public sealed record ReviewListFilter(int? Rating, ReviewStatus? Status, bool IncludeEveryStatus);

/// <summary>The rating summary of the published reviews. <c>Distribution</c> has the keys "1" to "5", zeros included.</summary>
public sealed record ReviewSummary(int Count, decimal Average, IReadOnlyDictionary<string, int> Distribution);

public static class ReviewLists
{
    public const string RatingMessage = "Rating must be a whole number from 1 to 5";

    private static readonly string[] SortFields = ["date", "rating", "name"];

    /// <summary>What the public may use: sort by date (default, newest first), rating, name. Facet: rating.</summary>
    public static readonly ListDefinition PublicDefinition = new(SortFields, "-date", ["rating"]);

    /// <summary>What the admin may use (with all=1): the same, plus the facet status.</summary>
    public static readonly ListDefinition AdminDefinition = new(SortFields, "-date", ["rating", "status"]);

    public static (ListQuery Query, ReviewListFilter Filter) Parse(ReviewListRequest request, bool admin)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, admin ? AdminDefinition : PublicDefinition, errors);

        var filter = new ReviewListFilter(
            ListFilters.Number(request.Rating, "rating", 1, 5, RatingMessage, errors),
            // The status filter is for the admin's moderation tabs; for anyone else it is ignored, like all=1.
            admin ? ListFilters.Wire<ReviewStatus>(request.Status, "status", ListFilters.OneOf<ReviewStatus>("Status"), errors) : null,
            admin);

        errors.ThrowIfAny();
        return (query, filter);
    }
}
