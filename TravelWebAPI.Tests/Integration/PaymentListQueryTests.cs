using System.Net;
using System.Text.Json;
using TravelWebAPI.Application.Payments;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>
/// The payment list and its summary on the common list contract, with 45 payments on three bookings: groups of three share a
/// payment date and creation time, and the status, method and currency vary.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PaymentListQueryTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly PaymentStatus[] Statuses = [PaymentStatus.Paid, PaymentStatus.Paid, PaymentStatus.Pending, PaymentStatus.Refunded, PaymentStatus.Paid];
    private static readonly PaymentMethod[] Methods = [PaymentMethod.Cash, PaymentMethod.BankTransfer, PaymentMethod.Card, PaymentMethod.Other];

    private readonly List<Payment> seeded = [];
    private readonly Dictionary<string, Guid> bookingIds = [];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateAsync("services", Service("Private tour"));

        await using var db = PostgresFixture.CreateContext(Connection);
        var customer = TestData.User("payer@example.com");
        db.Users.Add(customer);
        await db.SaveChangesAsync();

        foreach (var (reference, name) in new[] { ("BK-AAA111", "Anna Petrova"), ("BK-BBB222", "Markus Weber"), ("BK-CCC333", "Emma Wilson") })
        {
            var booking = TestData.Booking(customer.Id, "private-tour", reference);
            booking.Name = name;
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            bookingIds[reference] = booking.Id;
        }

        var references = bookingIds.Keys.Order().ToArray();
        for (var index = 1; index <= 45; index++)
        {
            var dollars = index % 4 == 0;
            var payment = new Payment
            {
                BookingId = bookingIds[references[index % 3]],
                Amount = dollars ? index * 10m : index * 1000m,
                Currency = dollars ? CurrencyCode.Usd : CurrencyCode.Lkr,
                ExchangeRate = dollars ? 330m : 1m,
                AmountLkr = dollars ? index * 3300m : index * 1000m,
                Method = Methods[index % 4],
                Status = Statuses[index % 5],
                PaidAt = new DateOnly(2026, 9, 1).AddDays((index - 1) / 3),
                Notes = index == 7 ? "100% upfront" : index == 8 ? "under_score" : $"Payment note {index:00}",
                CreatedAt = Created.AddHours((index - 1) / 3),
                UpdatedAt = Created.AddHours((index - 1) / 3),
            };
            db.Payments.Add(payment);
            seeded.Add(payment);
        }

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> ListAsync(string query = "")
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/payments" + query);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private async Task<JsonElement> SummaryAsync(string query = "")
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/payments/summary" + query);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static List<string> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToList();

    private static List<string> Notes(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("note").GetString()!).ToList();

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task The_list_and_the_summary_refuse_guests_and_customers()
    {
        foreach (var path in new[] { "/api/payments", "/api/payments/summary" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsync(path, CustomerToken)).StatusCode);
        }
    }

    // ---------------------------------------------------------------- paging and default order

    [Fact]
    public async Task The_first_page_is_newest_paid_first_with_the_booking_and_customer_and_the_totals()
    {
        var page = await ListAsync();

        Assert.Equal(20, page.GetProperty("items").GetArrayLength());
        Assert.Equal((45, 3), (page.GetProperty("totalCount").GetInt32(), page.GetProperty("totalPages").GetInt32()));
        var dates = page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("paidAt").GetString()!).ToList();
        Assert.Equal(dates.OrderByDescending(d => d, StringComparer.Ordinal), dates);
        Assert.All(page.GetProperty("items").EnumerateArray(), p =>
        {
            Assert.StartsWith("BK-", p.GetProperty("bookingReference").GetString());
            Assert.False(string.IsNullOrEmpty(p.GetProperty("customerName").GetString()));
            Assert.True(p.TryGetProperty("exchangeRate", out _));
            Assert.True(p.TryGetProperty("amountLkr", out _));
        });
    }

    [Fact]
    public async Task Walking_every_page_shows_each_payment_once_and_a_page_past_the_end_is_empty()
    {
        var seen = new List<string>();
        for (var number = 1; number <= 9; number++)
        {
            seen.AddRange(Ids(await ListAsync($"?page={number}&pageSize=5")));
        }

        Assert.Equal(45, seen.Distinct().Count());
        var beyond = await ListAsync("?page=10&pageSize=5");
        Assert.Empty(Ids(beyond));
        Assert.Equal(45, beyond.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Wrong_paging_and_sort_values_get_their_messages()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/payments?page=x&pageSize=0&sort=secret");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Page must be a whole number of 1 or more", await Field(response, "page"));
        Assert.Equal("Page size must be a whole number from 1 to 100", await Field(response, "pageSize"));
        Assert.Equal("Sort by one of: paidAt, createdAt, amountLkr, status, method, bookingReference, customerName", await Field(response, "sort"));
    }

    // ---------------------------------------------------------------- sorting

    [Theory]
    [InlineData("paidAt")]
    [InlineData("-paidAt")]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("amountLkr")]
    [InlineData("-amountLkr")]
    [InlineData("status")]
    [InlineData("-status")]
    [InlineData("method")]
    [InlineData("-method")]
    [InlineData("bookingReference")]
    [InlineData("-bookingReference")]
    [InlineData("customerName")]
    [InlineData("-customerName")]
    public async Task Every_sort_field_works_in_both_directions_with_stable_pages(string sort)
    {
        var seen = new List<string>();
        for (var number = 1; number <= 3; number++)
        {
            seen.AddRange(Ids(await ListAsync($"?sort={sort}&page={number}&pageSize=20")));
        }

        Assert.Equal(45, seen.Distinct().Count());
        Assert.Equal(seen, Ids(await ListAsync($"?sort={sort}&pageSize=100")));
    }

    [Fact]
    public async Task Sorting_by_amount_orders_by_the_rupee_value()
    {
        var amounts = (await ListAsync("?sort=-amountLkr&pageSize=100")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("amountLkr").GetDecimal()).ToList();

        Assert.Equal(amounts.OrderByDescending(a => a), amounts);
        Assert.Equal(seeded.Max(p => p.AmountLkr), amounts[0]);
    }

    [Fact]
    public async Task Sorting_by_customer_and_by_reference_follows_the_booking()
    {
        var names = (await ListAsync("?sort=customerName&pageSize=100")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("customerName").GetString()!).ToList();
        var references = (await ListAsync("?sort=-bookingReference&pageSize=100")).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("bookingReference").GetString()!).ToList();

        Assert.Equal(["Anna Petrova", "Emma Wilson", "Markus Weber"], names.Distinct());
        Assert.Equal(["BK-CCC333", "BK-BBB222", "BK-AAA111"], references.Distinct());
    }

    // ---------------------------------------------------------------- search

    [Theory]
    [InlineData("bk-aaa", 15)]
    [InlineData("MARKUS", 15)]
    [InlineData("payment note 2", 10)]
    [InlineData("nobody", 0)]
    public async Task Search_looks_in_the_booking_reference_the_customer_name_and_the_note(string text, int expected)
    {
        var page = await ListAsync($"?search={Uri.EscapeDataString(text)}&pageSize=100");

        Assert.Equal(expected, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_as_plain_characters()
    {
        Assert.Equal(["100% upfront"], Notes(await ListAsync("?search=" + Uri.EscapeDataString("100%"))));
        Assert.Empty(Notes(await ListAsync("?search=" + Uri.EscapeDataString("%%"))));
        Assert.Equal(["under_score"], Notes(await ListAsync("?search=" + Uri.EscapeDataString("r_s"))));
        Assert.Empty(Notes(await ListAsync("?search=" + Uri.EscapeDataString("e_0"))));
    }

    // ---------------------------------------------------------------- filters and facets

    [Fact]
    public async Task The_status_method_and_currency_filters_narrow_the_list()
    {
        var paid = await ListAsync("?status=paid&pageSize=100");
        var card = await ListAsync("?method=card&pageSize=100");
        var dollars = await ListAsync("?currency=USD&pageSize=100");
        var combined = await ListAsync("?status=paid&method=bank_transfer&currency=LKR&pageSize=100");

        Assert.Equal(seeded.Count(p => p.Status == PaymentStatus.Paid), paid.GetProperty("totalCount").GetInt32());
        Assert.Equal(seeded.Count(p => p.Method == PaymentMethod.Card), card.GetProperty("totalCount").GetInt32());
        Assert.Equal(11, dollars.GetProperty("totalCount").GetInt32());
        Assert.Equal(seeded.Count(p => p is { Status: PaymentStatus.Paid, Method: PaymentMethod.BankTransfer, Currency: CurrencyCode.Lkr }), combined.GetProperty("totalCount").GetInt32());
        Assert.All(combined.GetProperty("items").EnumerateArray(), p => Assert.Equal("LKR", p.GetProperty("currency").GetString()));
    }

    [Fact]
    public async Task The_booking_filter_takes_an_id_or_a_reference_in_any_case_and_unknown_ones_give_nothing()
    {
        var byReference = await ListAsync("?booking=bk-aaa111&pageSize=100");
        var byId = await ListAsync($"?booking={bookingIds["BK-AAA111"]}&pageSize=100");
        var unknown = await ListAsync("?booking=BK-ZZZZZZ");
        var junk = await ListAsync("?booking=whatever");

        Assert.Equal(15, byReference.GetProperty("totalCount").GetInt32());
        Assert.Equal(Ids(byReference).Order(), Ids(byId).Order());
        Assert.All(byReference.GetProperty("items").EnumerateArray(), p => Assert.Equal("BK-AAA111", p.GetProperty("bookingReference").GetString()));
        Assert.Equal(0, unknown.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, junk.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_date_range_includes_both_ends()
    {
        var page = await ListAsync("?from=2026-09-03&to=2026-09-05&pageSize=100");

        Assert.Equal(9, page.GetProperty("totalCount").GetInt32()); // three days, three payments each
        Assert.All(page.GetProperty("items").EnumerateArray(), p => Assert.InRange(p.GetProperty("paidAt").GetString()!, "2026-09-03", "2026-09-05"));
    }

    [Fact]
    public async Task Wrong_filter_values_get_their_messages()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/payments?status=done&method=cheque&currency=JPY&from=soon&to=2026-02-30");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Status must be one of: pending, paid, refunded", await Field(response, "status"));
        Assert.Equal("Method must be one of: cash, bank_transfer, card, other", await Field(response, "method"));
        Assert.Equal("Currency must be one of: USD, EUR, GBP, LKR", await Field(response, "currency"));
        Assert.Equal("Choose a valid date", await Field(response, "from"));
        Assert.Equal("Choose a valid date", await Field(response, "to"));
    }

    [Fact]
    public async Task Facets_count_the_payments_per_status_and_per_method()
    {
        var page = await ListAsync("?status=paid&facets=status,method&pageSize=5");

        var status = page.GetProperty("facets").GetProperty("status");
        var method = page.GetProperty("facets").GetProperty("method");
        Assert.Equal(seeded.Count(p => p.Status == PaymentStatus.Paid), status.GetProperty("paid").GetInt32());
        Assert.Equal(seeded.Count(p => p.Status == PaymentStatus.Pending), status.GetProperty("pending").GetInt32());
        Assert.Equal(seeded.Count(p => p.Status == PaymentStatus.Refunded), status.GetProperty("refunded").GetInt32());
        // the method counts follow the status filter (paid only)
        Assert.Equal(seeded.Count(p => p is { Status: PaymentStatus.Paid, Method: PaymentMethod.Cash }), method.GetProperty("cash").GetInt32());
        Assert.Equal(method.EnumerateObject().Sum(m => m.Value.GetInt32()), page.GetProperty("totalCount").GetInt32());
    }

    // ---------------------------------------------------------------- summary

    [Fact]
    public async Task The_summary_adds_up_every_payment_not_just_a_page()
    {
        var summary = await SummaryAsync();

        decimal Sum(PaymentStatus status) => seeded.Where(p => p.Status == status).Sum(p => p.AmountLkr);
        Assert.Equal(Sum(PaymentStatus.Paid), summary.GetProperty("paidLkr").GetDecimal());
        Assert.Equal(Sum(PaymentStatus.Pending), summary.GetProperty("pendingLkr").GetDecimal());
        Assert.Equal(Sum(PaymentStatus.Refunded), summary.GetProperty("refundedLkr").GetDecimal());
        Assert.Equal(Sum(PaymentStatus.Paid) - Sum(PaymentStatus.Refunded), summary.GetProperty("netLkr").GetDecimal());
        Assert.Equal(45, summary.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task The_summary_follows_the_method_currency_and_date_filters()
    {
        var cash = await SummaryAsync("?method=cash");
        var dollars = await SummaryAsync("?currency=USD");
        var range = await SummaryAsync("?from=2026-09-01&to=2026-09-02");

        Assert.Equal(seeded.Count(p => p.Method == PaymentMethod.Cash), cash.GetProperty("count").GetInt32());
        Assert.Equal(11, dollars.GetProperty("count").GetInt32());
        Assert.Equal(6, range.GetProperty("count").GetInt32());
        Assert.Equal(
            seeded.Where(p => p.PaidAt <= new DateOnly(2026, 9, 2) && p.Status == PaymentStatus.Paid).Sum(p => p.AmountLkr),
            range.GetProperty("paidLkr").GetDecimal());
    }

    [Fact]
    public async Task The_summary_of_nothing_is_all_zeros_and_wrong_filters_get_their_messages()
    {
        var empty = await SummaryAsync("?from=2030-01-01");

        Assert.All(new[] { "paidLkr", "pendingLkr", "refundedLkr", "netLkr", "count" }, name => Assert.Equal(0m, empty.GetProperty(name).GetDecimal()));

        var wrong = await AsAdmin(HttpMethod.Get, "/api/payments/summary?method=cheque&from=soon");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("Method must be one of: cash, bank_transfer, card, other", await Field(wrong, "method"));
        Assert.Equal("Choose a valid date", await Field(wrong, "from"));
    }

    [Fact]
    public async Task The_summary_rounds_money_to_two_decimals()
    {
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            var odd = new Payment
            {
                BookingId = bookingIds["BK-AAA111"], Amount = 1.01m, Currency = CurrencyCode.Usd, ExchangeRate = 332.115576m,
                AmountLkr = 335.44m, Method = PaymentMethod.Card, Status = PaymentStatus.Paid, PaidAt = new DateOnly(2030, 5, 5),
            };
            db.Payments.Add(odd);
            await db.SaveChangesAsync();
        }

        var summary = await SummaryAsync("?from=2030-05-05&to=2030-05-05");

        Assert.Equal(335.44m, summary.GetProperty("paidLkr").GetDecimal());
        Assert.Equal(335.44m, summary.GetProperty("netLkr").GetDecimal());
    }

    // ---------------------------------------------------------------- the declaration

    [Fact]
    public void The_database_side_implements_exactly_what_the_definition_declares()
    {
        var (sort, facets) = PaymentRepository.ListNames;

        Assert.Equal(PaymentLists.Definition.SortFields.Order(), sort.Order());
        Assert.Equal(PaymentLists.Definition.Facets.Order(), facets.Order());
    }
}
