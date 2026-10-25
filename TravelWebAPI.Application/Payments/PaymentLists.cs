using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Payments;

/// <summary>The payment list request: the common paging, sorting and search plus the payment filters.</summary>
public sealed class PaymentListRequest : ListQueryRequest
{
    /// <summary>Only payments with this status: pending, paid or refunded.</summary>
    public string? Status { get; set; }

    /// <summary>Only payments made this way: cash, bank_transfer, card or other.</summary>
    public string? Method { get; set; }

    /// <summary>Only payments in this currency: USD, EUR, GBP or LKR.</summary>
    public string? Currency { get; set; }

    /// <summary>Only the payments of this booking (its id or its <c>BK-</c> reference).</summary>
    public string? Booking { get; set; }

    /// <summary>Only payments dated on or after this day (<c>YYYY-MM-DD</c>).</summary>
    public string? From { get; set; }

    /// <summary>Only payments dated on or before this day (<c>YYYY-MM-DD</c>).</summary>
    public string? To { get; set; }
}

/// <summary>The request of the payment totals: the filters of the list that make sense for a sum (no paging, no status).</summary>
public sealed class PaymentSummaryRequest
{
    /// <summary>Only payments made this way: cash, bank_transfer, card or other.</summary>
    public string? Method { get; set; }

    /// <summary>Only payments in this currency: USD, EUR, GBP or LKR.</summary>
    public string? Currency { get; set; }

    /// <summary>Only payments dated on or after this day (<c>YYYY-MM-DD</c>).</summary>
    public string? From { get; set; }

    /// <summary>Only payments dated on or before this day (<c>YYYY-MM-DD</c>).</summary>
    public string? To { get; set; }
}

/// <summary>The filters of a payment list after they have been checked. A missing value means "do not filter".</summary>
public sealed record PaymentListFilter(PaymentStatus? Status, PaymentMethod? Method, CurrencyCode? Currency, string? Booking, DateOnly? From, DateOnly? To);

/// <summary>The totals over all matching payments, in LKR, rounded to two decimals.</summary>
public sealed record PaymentSummary(decimal PaidLkr, decimal PendingLkr, decimal RefundedLkr, decimal NetLkr, int Count);

public static class PaymentLists
{
    /// <summary>Sort by: paidAt then createdAt (default, newest first), createdAt, amountLkr, status, method, bookingReference, customerName. Facets: status, method.</summary>
    public static readonly ListDefinition Definition = new(
        ["paidAt", "createdAt", "amountLkr", "status", "method", "bookingReference", "customerName"],
        "-paidAt,-createdAt",
        ["status", "method"]);

    public static (ListQuery Query, PaymentListFilter Filter) Parse(PaymentListRequest request)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, Definition, errors);

        var filter = new PaymentListFilter(
            ListFilters.Wire<PaymentStatus>(request.Status, "status", ListFilters.OneOf<PaymentStatus>("Status"), errors),
            ListFilters.Wire<PaymentMethod>(request.Method, "method", ListFilters.OneOf<PaymentMethod>("Method"), errors),
            ListFilters.Wire<CurrencyCode>(request.Currency, "currency", ListFilters.OneOf<CurrencyCode>("Currency"), errors),
            ListFilters.Text(request.Booking),
            ListFilters.Date(request.From, "from", errors),
            ListFilters.Date(request.To, "to", errors));

        errors.ThrowIfAny();
        return (query, filter);
    }

    public static PaymentListFilter ParseSummary(PaymentSummaryRequest request)
    {
        var errors = new FieldErrors();
        var filter = new PaymentListFilter(
            null,
            ListFilters.Wire<PaymentMethod>(request.Method, "method", ListFilters.OneOf<PaymentMethod>("Method"), errors),
            ListFilters.Wire<CurrencyCode>(request.Currency, "currency", ListFilters.OneOf<CurrencyCode>("Currency"), errors),
            null,
            ListFilters.Date(request.From, "from", errors),
            ListFilters.Date(request.To, "to", errors));

        errors.ThrowIfAny();
        return filter;
    }

    /// <summary>How a booking filter is read: an id, a <c>BK-</c> reference, or something that can match nothing.</summary>
    public static (Guid? Id, string? Reference) ReadBooking(string booking)
    {
        if (Guid.TryParse(booking, out var id))
        {
            return (id, null);
        }

        return BookingReference.Looks(booking) ? (null, booking.ToUpperInvariant()) : (null, null);
    }
}
