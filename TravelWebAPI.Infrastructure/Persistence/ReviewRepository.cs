using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class ReviewRepository(AppDbContext context) : EfRepository<Review>(context), IReviewRepository
{
    public async Task<IReadOnlyList<Review>> ListAsync(bool publishedOnly, CancellationToken cancellationToken = default)
    {
        var query = Context.Reviews.AsNoTracking();
        if (publishedOnly)
        {
            query = query.Where(review => review.Status == ReviewStatus.Published);
        }

        return await query
            .OrderByDescending(review => review.ReviewDate)
            .ThenByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .ToListAsync(cancellationToken);
    }
}
