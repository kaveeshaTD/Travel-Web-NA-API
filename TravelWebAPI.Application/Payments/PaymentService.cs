using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Application.Payments;

/// <summary>What the admin sends. A field that is not sent is <c>null</c>; the amount is a decimal so text and odd numbers reach the validator.</summary>
public sealed record PaymentRequest(
    decimal? Amount = null,
    string? Currency = null,
    string? Method = null,
    string? Status = null,
    string? PaidAt = null,
    string? Note = null);

/// <summary>The trimmed, complete payment that is validated (defaults and stored values already applied).</summary>
public sealed record PaymentDraft(decimal? Amount, string Currency, string Method, string Status, string PaidAt, string Note);

/// <summary>A payment in the list: the payment fields plus the booking it belongs to.</summary>
public sealed record PaymentListItemDto(
    Guid Id,
    Guid BookingId,
    decimal Amount,
    string Currency,
    decimal ExchangeRate,
    decimal AmountLkr,
    string Method,
    string Status,
    string PaidAt,
    string Note,
    DateTimeOffset CreatedAt,
    string? BookingReference,
    string? CustomerName)
{
    public static PaymentListItemDto Of(Payment p)
    {
        var dto = PaymentDto.Of(p);
        return new PaymentListItemDto(
            dto.Id, dto.BookingId, dto.Amount, dto.Currency, dto.ExchangeRate, dto.AmountLkr, dto.Method, dto.Status, dto.PaidAt, dto.Note, dto.CreatedAt,
            p.Booking?.Reference,
            p.Booking?.Name);
    }
}

public interface IPaymentRepository : IRepository<Payment>
{
    /// <summary>Finds a payment by id. Tracked, so callers can change it.</summary>
    Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>One page of payments with their booking, searched, filtered and sorted in the database.</summary>
    Task<PagedResult<Payment>> PageAsync(PaymentListFilter filter, ListQuery query, CancellationToken cancellationToken = default);

    /// <summary>The sums per status over every payment matching the filter (no paging).</summary>
    Task<PaymentSummary> SummarizeAsync(PaymentListFilter filter, CancellationToken cancellationToken = default);
}

public static class PaymentRules
{
    public const int MaxNote = 500;

    public const string AmountMessage = "Enter an amount greater than 0";
    public const string DateMessage = "Choose a valid date";
    public const string NoteMessage = "Note must be 500 characters or fewer";

    /// <summary>Largest LKR value the <c>numeric(12,2)</c> column holds.</summary>
    public const decimal MaxAmountLkr = 9_999_999_999.99m;

    public const string DefaultCurrency = "USD";
    public const string DefaultMethod = "cash";
    public const string DefaultStatus = "paid";

    public static string OneOf<TEnum>(string field) where TEnum : struct, Enum
        => $"{field} must be one of: {string.Join(", ", Enum.GetValues<TEnum>().Select(EnumWire.ToWire))}";

    /// <summary>The LKR value of an amount at a rate, rounded to 2 decimals; null when it would not fit the column.</summary>
    public static decimal? ToLkr(decimal amount, decimal rate)
    {
        try
        {
            var lkr = BookingMoney.Round(amount * rate);
            return lkr <= MaxAmountLkr ? lkr : null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}

public sealed class PaymentDraftValidator : AbstractValidator<PaymentDraft>
{
    public PaymentDraftValidator()
    {
        RuleFor(x => x.Amount)
            .Must(amount => amount is > 0 and <= ContentRules.MaxMoney && decimal.Round(amount.Value, 2) == amount.Value)
            .WithMessage(PaymentRules.AmountMessage);
        RuleFor(x => x.Currency).Must(value => EnumWire.TryFromWire<CurrencyCode>(value, out _)).WithMessage(PaymentRules.OneOf<CurrencyCode>("Currency"));
        RuleFor(x => x.Method).Must(value => EnumWire.TryFromWire<PaymentMethod>(value, out _)).WithMessage(PaymentRules.OneOf<PaymentMethod>("Method"));
        RuleFor(x => x.Status).Must(value => EnumWire.TryFromWire<PaymentStatus>(value, out _)).WithMessage(PaymentRules.OneOf<PaymentStatus>("Status"));
        RuleFor(x => x.PaidAt).Must(value => BookingFormats.TryDate(value, out _)).WithMessage(PaymentRules.DateMessage);
        RuleFor(x => x.Note).MaxLength(PaymentRules.MaxNote, PaymentRules.NoteMessage);
    }
}

/// <summary>The money the admin has received for bookings: recorded, listed, corrected and removed.</summary>
public sealed class PaymentService(
    IPaymentRepository payments,
    IBookingRepository bookings,
    IExchangeRates exchangeRates,
    IUnitOfWork unitOfWork,
    IClock clock,
    IValidator<PaymentDraft> validator)
{
    public const string BookingNotFoundMessage = "Booking not found";
    public const string NotFoundMessage = "Payment not found";

    /// <summary>Records a payment for a booking (id or reference). The current rate is copied into the payment.</summary>
    public async Task<PaymentDto> CreateAsync(string bookingIdOrReference, PaymentRequest request, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.FindAsync(bookingIdOrReference.Trim(), cancellationToken)
            ?? throw new NotFoundException(BookingNotFoundMessage);

        var draft = new PaymentDraft(
            request.Amount,
            OrDefault(Merge.Text(request.Currency, PaymentRules.DefaultCurrency), PaymentRules.DefaultCurrency),
            Merge.Text(request.Method, PaymentRules.DefaultMethod),
            Merge.Text(request.Status, PaymentRules.DefaultStatus),
            OrDefault(Merge.Text(request.PaidAt, string.Empty), BookingFormats.Date(clock.TodayInSriLanka)),
            Merge.Text(request.Note, string.Empty));
        validator.ThrowIfInvalid(draft);

        var currency = EnumWire.FromWire<CurrencyCode>(draft.Currency);
        var rate = await RateOfAsync(currency, cancellationToken);

        var payment = new Payment { Id = Guid.CreateVersion7(), BookingId = booking.Id };
        Apply(payment, draft, rate);

        await payments.AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PaymentDto.Of(payment);
    }

    /// <summary>One page of payments with their booking reference and customer name, newest paid first unless sorted otherwise.</summary>
    public async Task<PagedResult<PaymentListItemDto>> ListAsync(PaymentListRequest request, CancellationToken cancellationToken = default)
    {
        var (query, filter) = PaymentLists.Parse(request);
        return (await payments.PageAsync(filter, query, cancellationToken)).Map(PaymentListItemDto.Of);
    }

    /// <summary>The totals of all matching payments (not just one page): paid, pending, refunded and net, in LKR.</summary>
    public async Task<PaymentSummary> SummaryAsync(PaymentSummaryRequest request, CancellationToken cancellationToken = default)
        => await payments.SummarizeAsync(PaymentLists.ParseSummary(request), cancellationToken);

    /// <summary>
    /// Edits a payment: what is not sent keeps its value, then the whole payment is validated. The stored rate is kept unless
    /// the currency changes; then the current rate of the new currency is copied.
    /// </summary>
    public async Task<PaymentDto> UpdateAsync(string id, PaymentRequest request, CancellationToken cancellationToken = default)
    {
        var payment = await FindAsync(id, cancellationToken);

        var draft = new PaymentDraft(
            request.Amount ?? payment.Amount,
            Merge.Text(request.Currency, EnumWire.ToWire(payment.Currency)),
            Merge.Text(request.Method, EnumWire.ToWire(payment.Method)),
            Merge.Text(request.Status, EnumWire.ToWire(payment.Status)),
            OrDefault(Merge.Text(request.PaidAt, BookingFormats.Date(payment.PaidAt)), BookingFormats.Date(clock.TodayInSriLanka)),
            Merge.Text(request.Note, payment.Notes ?? string.Empty));
        validator.ThrowIfInvalid(draft);

        var currency = EnumWire.FromWire<CurrencyCode>(draft.Currency);
        var rate = currency == payment.Currency ? payment.ExchangeRate : await RateOfAsync(currency, cancellationToken);

        Apply(payment, draft, rate);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return PaymentDto.Of(payment);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var payment = await FindAsync(id, cancellationToken);
        payments.Remove(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Payment> FindAsync(string id, CancellationToken cancellationToken)
        => Guid.TryParse(id.Trim(), out var guid)
            ? await payments.FindAsync(guid, cancellationToken) ?? throw new NotFoundException(NotFoundMessage)
            : throw new NotFoundException(NotFoundMessage);

    /// <summary>LKR per 1 unit of the currency right now (1 for LKR).</summary>
    private async Task<decimal> RateOfAsync(CurrencyCode currency, CancellationToken cancellationToken)
    {
        if (currency == CurrencyCode.Lkr)
        {
            return 1m;
        }

        return (await exchangeRates.GetCurrentAsync(cancellationToken)).RatesLkr[currency];
    }

    /// <summary>Copies the valid draft onto the payment with the given rate. The gateway fields are never touched.</summary>
    private static void Apply(Payment payment, PaymentDraft draft, decimal rate)
    {
        var amount = draft.Amount!.Value;
        var lkr = PaymentRules.ToLkr(amount, rate)
            ?? throw new ValidationException("amount", PaymentRules.AmountMessage);

        payment.Amount = amount;
        payment.Currency = EnumWire.FromWire<CurrencyCode>(draft.Currency);
        payment.ExchangeRate = rate;
        payment.AmountLkr = lkr;
        payment.Method = EnumWire.FromWire<PaymentMethod>(draft.Method);
        payment.Status = EnumWire.FromWire<PaymentStatus>(draft.Status);
        payment.PaidAt = DateOnly.ParseExact(draft.PaidAt, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        payment.Notes = draft.Note.Length == 0 ? null : draft.Note;
    }

    private static string OrDefault(string value, string fallback) => value.Length == 0 ? fallback : value;
}
