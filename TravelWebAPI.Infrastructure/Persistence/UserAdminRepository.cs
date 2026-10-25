using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Users;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class UserAdminRepository(AppDbContext context) : IUserAdminRepository
{
    public async Task<IReadOnlyList<UserWithBookingCount>> ListWithBookingCountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Users
            .AsNoTracking()
            .OrderByDescending(user => user.CreatedAt)
            .ThenByDescending(user => user.Id)
            .Select(user => new { User = user, Count = context.Bookings.Count(booking => booking.UserId == user.Id) })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new UserWithBookingCount(row.User, row.Count))];
    }

    public Task<int> CountBookingsAsync(Guid userId, CancellationToken cancellationToken = default)
        => context.Bookings.CountAsync(booking => booking.UserId == userId, cancellationToken);
}
