using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Users;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence.Lists;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class UserAdminRepository(AppDbContext context) : IUserAdminRepository
{
    /// <summary>A user with the number of their bookings, so the count can be sorted on in the database.</summary>
    private sealed class UserRow
    {
        public User User { get; set; } = null!;

        public int BookingCount { get; set; }
    }

    private static readonly ListSpec<UserRow> PageSpec = new ListSpec<UserRow>(
        SortMap.TieBreakBy<UserRow, Guid>(row => row.User.Id)
            .Add("createdAt", row => row.User.CreatedAt)
            .Add("name", row => row.User.Name)
            .Add("email", row => row.User.Email)
            .Add("role", row => row.User.Role)
            .Add("status", row => row.User.Status)
            .Add("bookingCount", row => row.BookingCount),
        [row => row.User.Name, row => row.User.Email, row => row.User.Country])
        .AddFacet(new Facet<UserRow, UserRole>("role", row => row.User.Role, EnumWire.ToWire, [.. Enum.GetValues<UserRole>().Select(EnumWire.ToWire)]))
        .AddFacet(new Facet<UserRow, UserStatus>("status", row => row.User.Status, EnumWire.ToWire, [.. Enum.GetValues<UserStatus>().Select(EnumWire.ToWire)]));

    /// <summary>The sort fields and facets this list implements, for the test that compares them with the definition.</summary>
    public static (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) ListNames => PageSpec.Names;

    public async Task<PagedResult<UserWithBookingCount>> PageAsync(UserListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.Users
            .AsNoTracking()
            .Select(user => new UserRow { User = user, BookingCount = context.Bookings.Count(booking => booking.UserId == user.Id) });

        var role = filter.Role;
        var status = filter.Status;
        var filters = new FilterSet<UserRow>()
            .Add("role", role is null ? null : row => row.User.Role == role)
            .Add("status", status is null ? null : row => row.User.Status == status);

        var page = await PageSpec.RunAsync(source, filters, query, cancellationToken);
        return page.Map(row => new UserWithBookingCount(row.User, row.BookingCount));
    }

    public Task<int> CountBookingsAsync(Guid userId, CancellationToken cancellationToken = default)
        => context.Bookings.CountAsync(booking => booking.UserId == userId, cancellationToken);
}
