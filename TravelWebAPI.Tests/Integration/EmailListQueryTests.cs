using System.Net;
using System.Text.Json;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>
/// The email log on the common list contract, with 45 entries: groups of three share a creation time, the kind and delivery
/// status vary, and every third entry belongs to one booking.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class EmailListQueryTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly EmailKind[] Kinds = [EmailKind.BookingReceived, EmailKind.BookingNotice, EmailKind.ContactAck, EmailKind.Reply, EmailKind.Custom];
    private static readonly EmailDeliveryStatus[] Statuses = [EmailDeliveryStatus.Failed, EmailDeliveryStatus.Sent, EmailDeliveryStatus.Queued];

    private Guid bookingId;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateAsync("services", Service("Private tour"));
        await AddBookingAsync("private-tour");
        bookingId = await Scalar<Guid>("SELECT id FROM bookings");

        await using var db = PostgresFixture.CreateContext(Connection);
        for (var index = 1; index <= 45; index++)
        {
            var status = Statuses[index % 3];
            db.EmailLogs.Add(new EmailLog
            {
                Id = Guid.CreateVersion7(),
                Kind = Kinds[index % 5],
                FromAddress = "hello@ceylonroads.example",
                ToAddress = index == 7 ? "100%@example.com" : index == 8 ? "under_score@example.com" : $"to{index:00}@example.com",
                Subject = $"Subject {index:00}",
                Body = "Body",
                BookingId = index % 3 == 0 ? bookingId : null,
                DeliveryStatus = status,
                Error = status == EmailDeliveryStatus.Failed ? "No email provider configured" : null,
                CreatedAt = Start.AddHours((index - 1) / 3),
                SentAt = status == EmailDeliveryStatus.Sent ? Start.AddHours((index - 1) / 3) : null,
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> ListAsync(string query = "")
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/emails" + query);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static List<string> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task The_log_refuses_guests_and_customers()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsync("/api/emails")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsync("/api/emails", CustomerToken)).StatusCode);
    }

    [Fact]
    public async Task The_first_page_is_newest_first_with_the_documented_fields_and_the_totals()
    {
        var page = await ListAsync();

        Assert.Equal((20, 45, 3), (page.GetProperty("items").GetArrayLength(), page.GetProperty("totalCount").GetInt32(), page.GetProperty("totalPages").GetInt32()));
        var times = page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.Equal(
            ["id", "from", "to", "subject", "body", "kind", "bookingId", "messageId", "status", "error", "createdAt", "sentAt"],
            page.GetProperty("items")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Failed_entries_keep_their_reason_and_sent_ones_their_time()
    {
        var failed = await ListAsync("?status=failed&pageSize=100");
        var sent = await ListAsync("?status=sent&pageSize=100");

        Assert.All(failed.GetProperty("items").EnumerateArray(), e =>
        {
            Assert.Equal("No email provider configured", e.GetProperty("error").GetString());
            Assert.Equal(JsonValueKind.Null, e.GetProperty("sentAt").ValueKind);
        });
        Assert.All(sent.GetProperty("items").EnumerateArray(), e => Assert.NotEqual(JsonValueKind.Null, e.GetProperty("sentAt").ValueKind));
        Assert.Equal(15, failed.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Walking_every_page_shows_each_entry_once_and_a_page_past_the_end_is_empty()
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

    [Theory]
    [InlineData("createdAt")]
    [InlineData("-createdAt")]
    [InlineData("to")]
    [InlineData("-to")]
    [InlineData("subject")]
    [InlineData("-subject")]
    [InlineData("kind")]
    [InlineData("-kind")]
    [InlineData("status")]
    [InlineData("-status")]
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
    public async Task Search_looks_in_the_recipient_the_subject_and_the_kind()
    {
        Assert.Equal(1, (await ListAsync("?search=TO45%40")).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await ListAsync("?search=subject%2012")).GetProperty("totalCount").GetInt32());
        Assert.Equal(9, (await ListAsync("?search=booking-received")).GetProperty("totalCount").GetInt32());
        Assert.Equal(18, (await ListAsync("?search=booking-")).GetProperty("totalCount").GetInt32()); // received and notice
        Assert.Equal(0, (await ListAsync("?search=nobody")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_as_plain_characters()
    {
        Assert.Equal(1, (await ListAsync("?search=" + Uri.EscapeDataString("100%"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await ListAsync("?search=" + Uri.EscapeDataString("%%"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await ListAsync("?search=" + Uri.EscapeDataString("r_s"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await ListAsync("?search=" + Uri.EscapeDataString("o_1"))).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_kind_and_booking_filters_narrow_the_log()
    {
        var kind = await ListAsync("?kind=contact-ack&pageSize=100");
        var booking = await ListAsync($"?booking={bookingId}&pageSize=100");
        var both = await ListAsync($"?kind=reply&booking={bookingId}&status=failed&pageSize=100");

        Assert.Equal(9, kind.GetProperty("totalCount").GetInt32());
        Assert.All(kind.GetProperty("items").EnumerateArray(), e => Assert.Equal("contact-ack", e.GetProperty("kind").GetString()));
        Assert.Equal(15, booking.GetProperty("totalCount").GetInt32());
        Assert.All(booking.GetProperty("items").EnumerateArray(), e => Assert.Equal(bookingId.ToString(), e.GetProperty("bookingId").GetString()));
        Assert.All(both.GetProperty("items").EnumerateArray(), e =>
        {
            Assert.Equal("reply", e.GetProperty("kind").GetString());
            Assert.Equal("failed", e.GetProperty("status").GetString());
        });
    }

    [Fact]
    public async Task Facets_count_per_status_and_per_kind_and_list_every_kind()
    {
        var page = await ListAsync("?status=failed&facets=status,kind&pageSize=1");

        var status = page.GetProperty("facets").GetProperty("status");
        Assert.Equal(15, status.GetProperty("failed").GetInt32());
        Assert.Equal(15, status.GetProperty("sent").GetInt32());
        Assert.Equal(15, status.GetProperty("queued").GetInt32());
        var kinds = page.GetProperty("facets").GetProperty("kind");
        Assert.Equal(Enum.GetValues<EmailKind>().Length, kinds.EnumerateObject().Count());
        Assert.Equal(0, kinds.GetProperty("welcome").GetInt32());
        Assert.Equal(15, kinds.EnumerateObject().Sum(k => k.Value.GetInt32())); // follows the status filter: failed only
    }

    [Fact]
    public async Task Wrong_values_get_their_messages()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/emails?status=done&kind=newsletter&booking=nope&sort=body&facets=bookingId");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Status must be one of: queued, sent, failed", await Field(response, "status"));
        Assert.StartsWith("Kind must be one of: welcome, password-reset", await Field(response, "kind"));
        Assert.Equal("Booking must be a valid id", await Field(response, "booking"));
        Assert.Equal("Sort by one of: createdAt, to, subject, kind, status", await Field(response, "sort"));
        Assert.Equal("Facets can be: status, kind", await Field(response, "facets"));
    }

    [Fact]
    public void The_database_side_implements_exactly_what_the_definition_declares()
    {
        var (sort, facets) = EmailLogRepository.ListNames;

        Assert.Equal(EmailLists.Definition.SortFields.Order(), sort.Order());
        Assert.Equal(EmailLists.Definition.Facets.Order(), facets.Order());
    }
}
