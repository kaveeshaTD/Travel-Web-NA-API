using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Bookings;

/// <summary>The booking list request: the common paging, sorting and search plus the booking filters.</summary>
public sealed class BookingListRequest : ListQueryRequest
{
    /// <summary>Only bookings with this status: pending, confirmed, completed or cancelled.</summary>
    public string? Status { get; set; }

    /// <summary>Only bookings of this service (its id, for example <c>airport</c>).</summary>
    public string? ServiceId { get; set; }

    /// <summary>Only bookings travelling on or after this date (<c>YYYY-MM-DD</c>).</summary>
    public string? From { get; set; }

    /// <summary>Only bookings travelling on or before this date (<c>YYYY-MM-DD</c>).</summary>
    public string? To { get; set; }
}

/// <summary>The filters of a booking list after they have been checked. A missing value means "do not filter".</summary>
public sealed record BookingListFilter(BookingStatus? Status, string? ServiceId, DateOnly? From, DateOnly? To);

public static class BookingLists
{
    /// <summary>Sort by: createdAt (default, newest first), date (travel date), reference, name, status, priceLkr. Facet: status.</summary>
    public static readonly ListDefinition Definition = new(
        ["createdAt", "date", "reference", "name", "status", "priceLkr"],
        "-createdAt",
        ["status"]);

    /// <summary>Checks the request and returns the query and the filters, or throws one validation error with every wrong field.</summary>
    public static (ListQuery Query, BookingListFilter Filter) Parse(BookingListRequest request)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, Definition, errors);

        var filter = new BookingListFilter(
            ListFilters.Wire<BookingStatus>(request.Status, "status", ListFilters.OneOf<BookingStatus>("Status"), errors),
            ListFilters.Text(request.ServiceId),
            ListFilters.Date(request.From, "from", errors),
            ListFilters.Date(request.To, "to", errors));

        errors.ThrowIfAny();
        return (query, filter);
    }
}
