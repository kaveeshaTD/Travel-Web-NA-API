using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Payments;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence.Lists;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class PaymentRepository(AppDbContext context) : EfRepository<Payment>(context), IPaymentRepository
{
    private static readonly ListSpec<Payment> PageSpec = new ListSpec<Payment>(
        SortMap.TieBreakBy<Payment, Guid>(payment => payment.Id)
            .Add("paidAt", payment => payment.PaidAt)
            .Add("createdAt", payment => payment.CreatedAt)
            .Add("amountLkr", payment => payment.AmountLkr)
            .Add("status", payment => payment.Status)
            .Add("method", payment => payment.Method)
            .Add("bookingReference", payment => payment.Booking!.Reference)
            .Add("customerName", payment => payment.Booking!.Name),
        [payment => payment.Booking!.Reference, payment => payment.Booking!.Name, payment => payment.Notes])
        .AddFacet(new Facet<Payment, PaymentStatus>("status", payment => payment.Status, EnumWire.ToWire, [.. Enum.GetValues<PaymentStatus>().Select(EnumWire.ToWire)]))
        .AddFacet(new Facet<Payment, PaymentMethod>("method", payment => payment.Method, EnumWire.ToWire, [.. Enum.GetValues<PaymentMethod>().Select(EnumWire.ToWire)]));

    /// <summary>The sort fields and facets this list implements, for the test that compares them with the definition.</summary>
    public static (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) ListNames => PageSpec.Names;

    public Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Context.Payments.FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    public async Task<PagedResult<Payment>> PageAsync(PaymentListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
        => await PageSpec.RunAsync(Context.Payments.AsNoTracking().Include(payment => payment.Booking), Filters(filter), query, cancellationToken);

    public async Task<PaymentSummary> SummarizeAsync(PaymentListFilter filter, CancellationToken cancellationToken = default)
    {
        var rows = await Filters(filter).Apply(Context.Payments.AsNoTracking())
            .GroupBy(payment => payment.Status)
            .Select(group => new { Status = group.Key, Total = group.Sum(payment => payment.AmountLkr), Count = group.Count() })
            .ToListAsync(cancellationToken);

        decimal Total(PaymentStatus status) => BookingMoney.Round(rows.Where(row => row.Status == status).Sum(row => row.Total));
        var paid = Total(PaymentStatus.Paid);
        var refunded = Total(PaymentStatus.Refunded);
        return new PaymentSummary(paid, Total(PaymentStatus.Pending), refunded, paid - refunded, rows.Sum(row => row.Count));
    }

    private static FilterSet<Payment> Filters(PaymentListFilter filter)
    {
        var status = filter.Status;
        var method = filter.Method;
        var currency = filter.Currency;
        var from = filter.From;
        var to = filter.To;

        var set = new FilterSet<Payment>()
            .Add("status", status is null ? null : payment => payment.Status == status)
            .Add("method", method is null ? null : payment => payment.Method == method)
            .Add("currency", currency is null ? null : payment => payment.Currency == currency)
            .Add("from", from is null ? null : payment => payment.PaidAt >= from)
            .Add("to", to is null ? null : payment => payment.PaidAt <= to);

        if (filter.Booking is { } booking)
        {
            var (id, reference) = PaymentLists.ReadBooking(booking);
            set.Add("booking", id is not null
                ? payment => payment.BookingId == id
                : reference is not null ? payment => payment.Booking!.Reference == reference : payment => false);
        }

        return set;
    }
}
