using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence.Lists;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class ReviewRepository(AppDbContext context) : EfRepository<Review>(context), IReviewRepository
{
    private static readonly string[] RatingValues = ["1", "2", "3", "4", "5"];

    private static readonly ListSpec<Review> PageSpec = new ListSpec<Review>(
        SortMap.TieBreakBy<Review, Guid>(review => review.Id)
            .Add("date", review => review.ReviewDate)
            .Add("rating", review => review.Rating)
            .Add("name", review => review.Name),
        [review => review.Name, review => review.Country, review => review.TripType, review => review.Comment])
        .AddFacet(new Facet<Review, short>("rating", review => review.Rating, rating => rating.ToString(System.Globalization.CultureInfo.InvariantCulture), RatingValues))
        .AddFacet(new Facet<Review, ReviewStatus>("status", review => review.Status, EnumWire.ToWire, [.. Enum.GetValues<ReviewStatus>().Select(EnumWire.ToWire)]));

    /// <summary>The sort fields and facets this list implements (the admin's, which is the larger set), for the test that compares them with the definition.</summary>
    public static (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) ListNames => PageSpec.Names;

    public async Task<PagedResult<Review>> PageAsync(ReviewListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var source = Context.Reviews.AsNoTracking();
        if (!filter.IncludeEveryStatus)
        {
            // The public list never shows a review that is not published, whatever else is asked.
            source = source.Where(review => review.Status == ReviewStatus.Published);
        }

        var rating = filter.Rating is { } value ? (short?)value : null;
        var status = filter.Status;
        var filters = new FilterSet<Review>()
            .Add("rating", rating is null ? null : review => review.Rating == rating)
            .Add("status", status is null ? null : review => review.Status == status);

        return await PageSpec.RunAsync(source, filters, query, cancellationToken);
    }

    public async Task<ReviewSummary> SummarizeAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Context.Reviews
            .AsNoTracking()
            .Where(review => review.Status == ReviewStatus.Published)
            .GroupBy(review => review.Rating)
            .Select(group => new { Rating = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var distribution = RatingValues.ToDictionary(key => key, key => rows.Where(row => row.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture) == key).Sum(row => row.Count));
        var count = distribution.Values.Sum();
        var total = rows.Sum(row => row.Rating * row.Count);
        var average = count == 0 ? 0m : decimal.Round(total / (decimal)count, 1, MidpointRounding.AwayFromZero);
        return new ReviewSummary(count, average, distribution);
    }
}
