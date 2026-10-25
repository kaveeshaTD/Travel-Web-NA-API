using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Payments;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

/// <summary>The payment store, the rates and the unit of work in memory; the bookings come from <see cref="BookingHarness"/>.</summary>
public sealed class PaymentHarness : IPaymentRepository, IExchangeRates, IUnitOfWork
{
    public BookingHarness Bookings { get; } = new();

    public List<Payment> Payments => Bookings.Payments;

    public decimal Usd { get; set; } = 330m;

    public decimal Eur { get; set; } = 360m;

    public int Saves { get; private set; }

    public int RateReads { get; private set; }

    public PaymentService Create() => new(this, Bookings, this, this, Bookings.Clock, new PaymentDraftValidator());

    public Payment AddPayment(Guid bookingId, decimal amount = 100m, CurrencyCode currency = CurrencyCode.Usd, decimal rate = 300m, PaymentStatus status = PaymentStatus.Paid)
    {
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            BookingId = bookingId,
            Amount = amount,
            Currency = currency,
            ExchangeRate = rate,
            AmountLkr = amount * rate,
            Method = PaymentMethod.BankTransfer,
            Status = status,
            PaidAt = new DateOnly(2026, 10, 1),
            Notes = "first",
            CreatedAt = Bookings.Clock.UtcNow,
        };
        Payments.Add(payment);
        return payment;
    }

    public Task<Payment?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => Task.FromResult(Payments.FirstOrDefault(p => p.Id.Equals(id)));

    public Task AddAsync(Payment entity, CancellationToken cancellationToken = default)
    {
        Payments.Add(entity);
        return Task.CompletedTask;
    }

    public void Remove(Payment entity) => Payments.Remove(entity);

    public Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Payments.FirstOrDefault(p => p.Id == id));

    /// <summary>A plain in-memory page (newest paid first); the real filtering, sorting and searching are tested against PostgreSQL.</summary>
    public Task<PagedResult<Payment>> PageAsync(PaymentListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var rows = Payments
            .Select(p => { p.Booking = Bookings.Bookings.FirstOrDefault(b => b.Id == p.BookingId); return p; })
            .OrderByDescending(p => p.PaidAt)
            .ThenByDescending(p => p.CreatedAt)
            .ToList();
        return Task.FromResult(Paged.Of<Payment>([.. rows.Skip(query.Skip).Take(query.PageSize)], query, rows.Count));
    }

    public Task<PaymentSummary> SummarizeAsync(PaymentListFilter filter, CancellationToken cancellationToken = default)
        => Task.FromResult(new PaymentSummary(0, 0, 0, 0, Payments.Count));

    public Task<CurrentRates> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        RateReads++;
        return Task.FromResult(new CurrentRates(
            new Dictionary<CurrencyCode, decimal> { [CurrencyCode.Usd] = Usd, [CurrencyCode.Eur] = Eur, [CurrencyCode.Gbp] = 420m },
            null,
            CurrentRates.FallbackSource));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}

public sealed class PaymentServiceTests
{
    private readonly PaymentHarness _h = new();

    private Booking Booking() => _h.Bookings.AddBooking();

    private static IReadOnlyDictionary<string, string> Fields(Func<Task> action)
        => Assert.Throws<ValidationException>(() => action().GetAwaiter().GetResult()).Fields!;

    // ---------------------------------------------------------------- validation messages

    [Fact]
    public async Task Every_wrong_field_gets_its_message_and_nothing_is_saved()
    {
        var booking = Booking();

        var fields = Fields(() => _h.Create().CreateAsync(booking.Reference, new PaymentRequest(
            Amount: 0, Currency: "JPY", Method: "cheque", Status: "done", PaidAt: "2026-02-30", Note: new string('x', 501))));

        Assert.Equal("Enter an amount greater than 0", fields["amount"]);
        Assert.Equal("Currency must be one of: USD, EUR, GBP, LKR", fields["currency"]);
        Assert.Equal("Method must be one of: cash, bank_transfer, card, other", fields["method"]);
        Assert.Equal("Status must be one of: pending, paid, refunded", fields["status"]);
        Assert.Equal("Choose a valid date", fields["paidAt"]);
        Assert.Equal("Note must be 500 characters or fewer", fields["note"]);
        Assert.Equal(0, _h.Saves);
        Assert.Empty(_h.Payments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.001")]
    [InlineData("100000000")]
    public async Task A_bad_amount_gets_the_amount_message(string? amount)
    {
        var booking = Booking();

        var fields = Fields(() => _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture))));

        Assert.Equal("Enter an amount greater than 0", fields["amount"]);
        Assert.Single(fields);
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("Usd")]
    public async Task The_currency_is_matched_exactly(string currency)
    {
        var booking = Booking();

        var fields = Fields(() => _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 5, Currency: currency)));

        Assert.True(fields.ContainsKey("currency"));
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("bank transfer")]
    [InlineData("")]
    public async Task The_method_is_matched_exactly(string method)
    {
        var booking = Booking();

        var fields = Fields(() => _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 5, Method: method)));

        Assert.True(fields.ContainsKey("method"));
    }

    [Theory]
    [InlineData("2026-10-25", true)]
    [InlineData("2030-01-01", true)]
    [InlineData("2026-02-30", false)]
    [InlineData("25/10/2026", false)]
    [InlineData("2026-1-5", false)]
    public async Task The_date_must_be_a_real_calendar_date_and_may_be_in_the_future(string date, bool valid)
    {
        var booking = Booking();
        var service = _h.Create();

        if (valid)
        {
            var payment = await service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 5, PaidAt: date));
            Assert.Equal(date, payment.PaidAt);
        }
        else
        {
            Assert.Equal("Choose a valid date", Fields(() => service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 5, PaidAt: date)))["paidAt"]);
        }
    }

    [Fact]
    public async Task A_note_of_500_characters_is_fine_and_is_trimmed()
    {
        var booking = Booking();

        var ok = await _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 5, Note: "  " + new string('x', 500) + "  "));

        Assert.Equal(500, ok.Note.Length);
    }

    [Fact]
    public async Task The_largest_amount_is_accepted_but_a_value_that_does_not_fit_in_lkr_is_not()
    {
        var booking = Booking();
        var service = _h.Create();

        await service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 99_999_999.99m, Currency: "LKR"));
        var fields = Fields(() => service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 99_999_999.99m, Currency: "USD")));

        Assert.Equal("Enter an amount greater than 0", fields["amount"]);
        Assert.Single(_h.Payments);
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Defaults_are_paid_cash_usd_and_today_in_sri_lanka_and_the_rate_is_copied()
    {
        var booking = Booking();

        var payment = await _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 100));

        Assert.Equal("paid", payment.Status);
        Assert.Equal("cash", payment.Method);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal("2026-10-25", payment.PaidAt);
        Assert.Equal(330m, payment.ExchangeRate);
        Assert.Equal(33_000m, payment.AmountLkr);
        Assert.Equal(booking.Id, payment.BookingId);
        Assert.Equal(string.Empty, payment.Note);
        Assert.Equal(1, _h.Saves);
        var stored = Assert.Single(_h.Payments);
        Assert.Null(stored.Gateway);
        Assert.Null(stored.GatewayReference);
        Assert.Null(stored.Notes);
    }

    [Fact]
    public async Task An_empty_paid_at_means_today_too()
    {
        var booking = Booking();

        var payment = await _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 5, PaidAt: "  "));

        Assert.Equal("2026-10-25", payment.PaidAt);
    }

    [Fact]
    public async Task Lkr_has_rate_1_and_does_not_ask_for_rates()
    {
        var booking = Booking();

        var payment = await _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 50_000, Currency: "LKR"));

        Assert.Equal(1m, payment.ExchangeRate);
        Assert.Equal(50_000m, payment.AmountLkr);
        Assert.Equal(0, _h.RateReads);
    }

    [Fact]
    public async Task The_lkr_value_is_rounded_to_two_decimals()
    {
        var booking = Booking();
        _h.Eur = 361.234567m;

        var payment = await _h.Create().CreateAsync(booking.Reference, new PaymentRequest(Amount: 10.55m, Currency: "EUR"));

        Assert.Equal(361.234567m, payment.ExchangeRate);
        Assert.Equal(3811.02m, payment.AmountLkr);
    }

    [Fact]
    public async Task The_booking_can_be_given_by_id_or_reference_and_an_unknown_one_is_404()
    {
        var booking = Booking();
        var service = _h.Create();

        await service.CreateAsync(booking.Id.ToString(), new PaymentRequest(Amount: 5));
        await service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 5));
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => service.CreateAsync("BK-ZZZZZZ", new PaymentRequest(Amount: 5)));

        Assert.Equal("Booking not found", missing.Message);
        Assert.Equal(2, _h.Payments.Count);
    }

    // ---------------------------------------------------------------- update

    [Fact]
    public async Task Sending_only_the_status_changes_only_the_status()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id);

        var updated = await _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Status: "refunded"));

        Assert.Equal("refunded", updated.Status);
        Assert.Equal(100m, updated.Amount);
        Assert.Equal("USD", updated.Currency);
        Assert.Equal(300m, updated.ExchangeRate);
        Assert.Equal(30_000m, updated.AmountLkr);
        Assert.Equal("bank_transfer", updated.Method);
        Assert.Equal("2026-10-01", updated.PaidAt);
        Assert.Equal("first", updated.Note);
        Assert.Equal(0, _h.RateReads);
    }

    [Fact]
    public async Task A_new_amount_keeps_the_stored_rate_even_when_the_market_rate_moved()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id, amount: 100m, rate: 300m);
        _h.Usd = 340m;

        var updated = await _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Amount: 120));

        Assert.Equal(300m, updated.ExchangeRate);
        Assert.Equal(36_000m, updated.AmountLkr);
        Assert.Equal(0, _h.RateReads);
    }

    [Fact]
    public async Task Sending_the_same_currency_again_keeps_the_rate()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id, rate: 300m);

        var updated = await _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Currency: "USD"));

        Assert.Equal(300m, updated.ExchangeRate);
    }

    [Fact]
    public async Task A_new_currency_copies_the_current_rate_of_that_currency()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id, amount: 100m, rate: 300m);

        var updated = await _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Currency: "EUR"));

        Assert.Equal("EUR", updated.Currency);
        Assert.Equal(360m, updated.ExchangeRate);
        Assert.Equal(36_000m, updated.AmountLkr);
        Assert.Equal(1, _h.RateReads);
    }

    [Fact]
    public async Task Changing_to_lkr_gives_rate_1()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id, amount: 100m, rate: 300m);

        var updated = await _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Currency: "LKR", Amount: 25_000));

        Assert.Equal(1m, updated.ExchangeRate);
        Assert.Equal(25_000m, updated.AmountLkr);
    }

    [Fact]
    public async Task An_edit_with_a_wrong_value_changes_nothing()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id);

        var fields = Fields(() => _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Amount: -1, Method: "gold")));

        Assert.Equal(2, fields.Count);
        Assert.Equal(100m, payment.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, payment.Method);
        Assert.Equal(0, _h.Saves);
    }

    [Fact]
    public async Task The_gateway_fields_are_not_touched_by_an_edit()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id);
        payment.Gateway = "payhere";
        payment.GatewayReference = "abc123";

        await _h.Create().UpdateAsync(payment.Id.ToString(), new PaymentRequest(Note: "changed"));

        Assert.Equal("payhere", payment.Gateway);
        Assert.Equal("abc123", payment.GatewayReference);
        Assert.Equal("changed", payment.Notes);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task An_unknown_payment_is_404_for_edit_and_delete(string id)
    {
        var service = _h.Create();

        var edit = await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(id, new PaymentRequest(Amount: 5)));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(id));

        Assert.Equal("Payment not found", edit.Message);
        Assert.Equal(0, _h.Saves);
    }

    // ---------------------------------------------------------------- delete and list

    [Fact]
    public async Task Delete_removes_the_payment_and_saves_once()
    {
        var booking = Booking();
        var payment = _h.AddPayment(booking.Id);

        await _h.Create().DeleteAsync(payment.Id.ToString());

        Assert.Empty(_h.Payments);
        Assert.Equal(1, _h.Saves);
    }

    [Fact]
    public async Task The_list_has_the_booking_reference_and_customer_name_newest_paid_first()
    {
        var booking = Booking();
        var older = _h.AddPayment(booking.Id);
        var newer = _h.AddPayment(booking.Id);
        newer.PaidAt = new DateOnly(2026, 10, 20);

        var list = await _h.Create().ListAsync(new PaymentListRequest());

        Assert.Equal([newer.Id, older.Id], list.Items.Select(p => p.Id));
        Assert.All(list.Items, p =>
        {
            Assert.Equal(booking.Reference, p.BookingReference);
            Assert.Equal("Emma Wilson", p.CustomerName);
        });
    }

    [Fact]
    public async Task The_booking_totals_follow_create_edit_and_delete()
    {
        var booking = Booking();
        var service = _h.Create();
        decimal Paid() => TravelWebAPI.Application.Bookings.BookingMoney.Paid(_h.Payments);

        var one = await service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 40_000, Currency: "LKR"));
        var two = await service.CreateAsync(booking.Reference, new PaymentRequest(Amount: 10_000, Currency: "LKR", Status: "pending"));
        Assert.Equal(40_000m, Paid());

        await service.UpdateAsync(two.Id.ToString(), new PaymentRequest(Status: "paid"));
        Assert.Equal(50_000m, Paid());

        await service.UpdateAsync(two.Id.ToString(), new PaymentRequest(Status: "refunded"));
        Assert.Equal(30_000m, Paid());

        await service.DeleteAsync(one.Id.ToString());
        Assert.Equal(-10_000m, Paid());
    }
}
