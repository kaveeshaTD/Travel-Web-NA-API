using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class ContactServiceTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly Store _store;
    private readonly ContactService _service;

    public ContactServiceTests()
    {
        _store = new Store(_clock);
        _service = new ContactService(
            _store, _store, _store, _store, _store, _clock, new ContactDraftValidator(), new ReplyDraftValidator());
    }

    private static ContactRequest Valid(string email = "visitor@example.com") => new("Sam Visitor", email, "Do you drive to Ella from the airport?");

    private static ValidationException Invalid(Func<Task> action) => Assert.Throws<ValidationException>(() => action().GetAwaiter().GetResult());

    // ---------------------------------------------------------------- submit

    [Fact]
    public async Task A_message_is_saved_new_with_a_reference_and_an_acknowledgement_that_is_sent_after_the_save()
    {
        var receipt = await _service.SubmitAsync(Valid());

        var saved = Assert.Single(_store.Messages);
        Assert.Matches("^MSG-[0-9A-F]{8}$", receipt.Reference);
        Assert.Equal(MessageStatus.New, saved.Status);
        Assert.Null(saved.RepliedAt);

        var email = Assert.Single(_store.Logs);
        Assert.Equal(EmailKind.ContactAck, email.Kind);
        Assert.Equal("visitor@example.com", email.ToAddress);
        Assert.Equal(saved.Id, email.ContactMessageId);
        Assert.Contains("Sam Visitor", email.Body, StringComparison.Ordinal);
        Assert.Contains("Do you drive to Ella from the airport?", email.Body, StringComparison.Ordinal);
        Assert.Contains("Nuwan", email.Body, StringComparison.Ordinal);
        Assert.Equal(["saved", "dispatched"], _store.Events.Where(e => e is "saved" or "dispatched").Distinct());
    }

    [Fact]
    public async Task The_email_is_trimmed_and_lower_cased_and_text_is_trimmed()
    {
        await _service.SubmitAsync(new ContactRequest("  Sam  ", "  Visitor@Example.COM ", "   Do you drive to Ella from the airport?  "));

        var saved = _store.Messages.Single();
        Assert.Equal("visitor@example.com", saved.Email);
        Assert.Equal("Sam", saved.Name);
        Assert.Equal("Do you drive to Ella from the airport?", saved.Message);
        Assert.Equal("visitor@example.com", _store.Logs.Single().ToAddress);
    }

    [Fact]
    public async Task Invalid_messages_report_the_contract_messages_and_store_nothing()
    {
        var error = Invalid(() => _service.SubmitAsync(new ContactRequest("S", "nope", "short")));

        Assert.Equal("Enter your name", error.Fields["name"]);
        Assert.Equal("Enter a valid email address", error.Fields["email"]);
        Assert.Equal("Please write at least 10 characters", error.Fields["message"]);
        Assert.Empty(_store.Messages);
        Assert.Empty(_store.Logs);
    }

    [Fact]
    public async Task Over_long_text_is_a_field_message()
    {
        var error = Invalid(() => _service.SubmitAsync(Valid() with { Name = new string('n', 121), Message = new string('m', 5001) }));

        Assert.Equal("Your name is too long", error.Fields["name"]);
        Assert.Equal("The message is too long", error.Fields["message"]);
    }

    [Fact]
    public async Task An_unconfigured_email_provider_marks_the_log_failed_but_the_message_is_kept()
    {
        await _service.SubmitAsync(Valid());

        var email = _store.Logs.Single();
        Assert.Equal(EmailDeliveryStatus.Failed, email.DeliveryStatus);
        Assert.Equal("No email provider configured", email.Error);
        Assert.Single(_store.Messages);
    }

    // ---------------------------------------------------------------- acknowledgement throttle

    [Fact]
    public async Task The_same_address_is_acknowledged_only_once_within_an_hour()
    {
        await _service.SubmitAsync(Valid());
        _clock.Advance(TimeSpan.FromMinutes(30));

        await _service.SubmitAsync(Valid());

        Assert.Equal(2, _store.Messages.Count);
        Assert.Single(_store.Logs);
        Assert.Equal(1, _store.DispatchCalls);
    }

    [Fact]
    public async Task Different_addresses_each_get_an_acknowledgement()
    {
        await _service.SubmitAsync(Valid("one@example.com"));
        await _service.SubmitAsync(Valid("two@example.com"));

        Assert.Equal(["one@example.com", "two@example.com"], _store.Logs.Select(l => l.ToAddress));
    }

    [Fact]
    public async Task After_an_hour_the_same_address_is_acknowledged_again()
    {
        await _service.SubmitAsync(Valid());
        _clock.Advance(TimeSpan.FromHours(1));

        await _service.SubmitAsync(Valid());

        Assert.Equal(2, _store.Logs.Count);
    }

    [Fact]
    public async Task The_throttle_looks_at_the_address_with_any_letter_case()
    {
        await _service.SubmitAsync(Valid("visitor@example.com"));

        await _service.SubmitAsync(Valid("VISITOR@Example.com"));

        Assert.Single(_store.Logs);
    }

    // ---------------------------------------------------------------- inbox and status

    [Fact]
    public async Task The_inbox_lists_what_the_repository_returns_as_dtos()
    {
        await _service.SubmitAsync(Valid());

        var inbox = await _service.ListAsync();

        var message = Assert.Single(inbox);
        Assert.Equal("new", message.Status);
        Assert.Equal("Sam Visitor", message.Name);
        Assert.Null(message.RepliedAt);
    }

    [Theory]
    [InlineData("new", MessageStatus.New)]
    [InlineData("read", MessageStatus.Read)]
    [InlineData("replied", MessageStatus.Replied)]
    [InlineData(" READ ", MessageStatus.Read)]
    public async Task A_valid_status_is_stored(string status, MessageStatus expected)
    {
        await _service.SubmitAsync(Valid());
        var id = _store.Messages.Single().Id;

        var updated = await _service.SetStatusAsync(id, new MessageStatusRequest(status));

        Assert.Equal(expected, _store.Messages.Single().Status);
        Assert.Equal(status.Trim().ToLowerInvariant(), updated.Status);
    }

    [Theory]
    [InlineData("archived")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Any_other_status_gets_the_contract_message(string? status)
    {
        await _service.SubmitAsync(Valid());
        var id = _store.Messages.Single().Id;

        var error = Invalid(() => _service.SetStatusAsync(id, new MessageStatusRequest(status)));

        Assert.Equal("Status must be one of: new, read, replied", error.Fields["status"]);
        Assert.Equal(MessageStatus.New, _store.Messages.Single().Status);
    }

    [Fact]
    public async Task An_unknown_message_is_not_found_for_status_reply_and_delete()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SetStatusAsync(Guid.NewGuid(), new MessageStatusRequest("read")));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ReplyAsync(Guid.NewGuid(), new ReplyRequest(Body: "Hello there")));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- reply

    [Fact]
    public async Task A_reply_is_recorded_with_the_default_subject_and_the_message_becomes_replied()
    {
        await _service.SubmitAsync(Valid());
        var message = _store.Messages.Single();
        _clock.Advance(TimeSpan.FromHours(2));

        var entry = await _service.ReplyAsync(message.Id, new ReplyRequest(Body: "  Yes, we can pick you up from the airport.  "));

        Assert.Equal("reply", entry.Kind);
        Assert.Equal("visitor@example.com", entry.To);
        Assert.Equal("Re: your message to Ceylon Roads", entry.Subject);
        Assert.Equal("Yes, we can pick you up from the airport.", entry.Body);
        Assert.Equal(message.Id, entry.MessageId);
        Assert.Equal(MessageStatus.Replied, message.Status);
        Assert.Equal(_clock.UtcNow, message.RepliedAt);
    }

    [Fact]
    public async Task A_custom_subject_is_used()
    {
        await _service.SubmitAsync(Valid());

        var entry = await _service.ReplyAsync(_store.Messages.Single().Id, new ReplyRequest("About Ella", "Yes, of course."));

        Assert.Equal("About Ella", entry.Subject);
    }

    [Fact]
    public async Task The_reply_shows_that_the_email_could_not_be_delivered_without_undoing_the_reply()
    {
        await _service.SubmitAsync(Valid());
        var message = _store.Messages.Single();

        var entry = await _service.ReplyAsync(message.Id, new ReplyRequest(Body: "Yes, of course."));

        Assert.Equal("failed", entry.Status);
        Assert.Equal("No email provider configured", entry.Error);
        Assert.Null(entry.SentAt);
        Assert.Equal(MessageStatus.Replied, message.Status);
    }

    [Fact]
    public async Task A_reply_that_is_delivered_shows_sent()
    {
        _store.DeliverEmails = true;
        await _service.SubmitAsync(Valid());

        var entry = await _service.ReplyAsync(_store.Messages.Single().Id, new ReplyRequest(Body: "Yes, of course."));

        Assert.Equal("sent", entry.Status);
        Assert.Null(entry.Error);
        Assert.Equal(_clock.UtcNow, entry.SentAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ab  ")]
    public async Task A_reply_needs_at_least_three_characters_and_nothing_changes_otherwise(string? body)
    {
        await _service.SubmitAsync(Valid());
        var message = _store.Messages.Single();
        var logsBefore = _store.Logs.Count;

        var error = Invalid(() => _service.ReplyAsync(message.Id, new ReplyRequest(Body: body)));

        Assert.Equal("Write a reply", error.Fields["body"]);
        Assert.Equal(MessageStatus.New, message.Status);
        Assert.Null(message.RepliedAt);
        Assert.Equal(logsBefore, _store.Logs.Count);
    }

    [Fact]
    public async Task An_over_long_subject_or_body_is_a_field_message()
    {
        await _service.SubmitAsync(Valid());

        var error = Invalid(() => _service.ReplyAsync(_store.Messages.Single().Id, new ReplyRequest(new string('s', 251), new string('b', 10_001))));

        Assert.Equal("The subject is too long", error.Fields["subject"]);
        Assert.Equal("The reply is too long", error.Fields["body"]);
    }

    // ---------------------------------------------------------------- delete and log

    [Fact]
    public async Task Delete_removes_the_message()
    {
        await _service.SubmitAsync(Valid());

        await _service.DeleteAsync(_store.Messages.Single().Id);

        Assert.Empty(_store.Messages);
    }

    [Fact]
    public async Task The_email_log_defaults_to_500_and_takes_a_limit_between_1_and_2000()
    {
        var logs = new EmailLogService(_store);

        await logs.ListAsync(null);
        Assert.Equal(500, _store.LastLimit);

        await logs.ListAsync(2000);
        Assert.Equal(2000, _store.LastLimit);

        await logs.ListAsync(1);
        Assert.Equal(1, _store.LastLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2001)]
    [InlineData(5000)]
    public async Task A_limit_outside_the_range_is_a_validation_error(int limit)
    {
        var error = Invalid(() => new EmailLogService(_store).ListAsync(limit));

        Assert.Equal(EmailLogService.LimitMessage, error.Fields["limit"]);
    }

    // ---------------------------------------------------------------- fakes

    /// <summary>One in-memory stand-in for the message and log repositories, the outbox, the settings and the unit of work.</summary>
    private sealed class Store : IContactMessageRepository, IEmailLogRepository, IRepository<SiteSettings>, IEmailOutbox, IUnitOfWork
    {
        private readonly FakeClock _clock;

        public Store(FakeClock clock) => _clock = clock;

        public List<ContactMessage> Messages { get; } = [];

        public List<EmailLog> Logs { get; } = [];

        public List<string> Events { get; } = [];

        public int DispatchCalls { get; private set; }

        public int LastLimit { get; private set; }

        public bool DeliverEmails { get; set; }

        public Task<IReadOnlyList<ContactMessage>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContactMessage>>([.. Messages.OrderByDescending(m => m.CreatedAt)]);

        Task<ContactMessage?> IRepository<ContactMessage>.GetByIdAsync(object id, CancellationToken cancellationToken)
            => Task.FromResult(Messages.FirstOrDefault(m => m.Id == (Guid)id));

        Task IRepository<ContactMessage>.AddAsync(ContactMessage entity, CancellationToken cancellationToken)
        {
            entity.CreatedAt = _clock.UtcNow;
            Messages.Add(entity);
            return Task.CompletedTask;
        }

        void IRepository<ContactMessage>.Remove(ContactMessage entity) => Messages.Remove(entity);

        Task<SiteSettings?> IRepository<SiteSettings>.GetByIdAsync(object id, CancellationToken cancellationToken)
            => Task.FromResult<SiteSettings?>(new SiteSettings { Name = "Ceylon Roads", DriverName = "Nuwan" });

        Task IRepository<SiteSettings>.AddAsync(SiteSettings entity, CancellationToken cancellationToken) => Task.CompletedTask;

        void IRepository<SiteSettings>.Remove(SiteSettings entity)
        {
        }

        public Task<IReadOnlyList<EmailLog>> ListAsync(int limit, CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            return Task.FromResult<IReadOnlyList<EmailLog>>([.. Logs.OrderByDescending(l => l.CreatedAt).Take(limit)]);
        }

        public Task<EmailLog?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Logs.FirstOrDefault(l => l.Id == id));

        public Task<IReadOnlyList<EmailLog>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmailLog>>([.. Logs.Where(l => l.BookingId == bookingId)]);

        public Task<bool> AcknowledgedSinceAsync(string address, DateTimeOffset since, CancellationToken cancellationToken = default)
            => Task.FromResult(Logs.Any(l => l.Kind == EmailKind.ContactAck
                && string.Equals(l.ToAddress, address, StringComparison.OrdinalIgnoreCase)
                && l.CreatedAt > since));

        public Task<Guid> QueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var log = new EmailLog
            {
                Id = Guid.CreateVersion7(),
                Kind = message.Kind,
                ToAddress = message.To,
                Subject = message.Subject,
                Body = message.Body,
                ContactMessageId = message.ContactMessageId,
                BookingId = message.BookingId,
                DeliveryStatus = EmailDeliveryStatus.Queued,
                CreatedAt = _clock.UtcNow,
            };
            Logs.Add(log);
            return Task.FromResult(log.Id);
        }

        public Task DispatchAsync(IEnumerable<Guid> emailLogIds, CancellationToken cancellationToken = default)
        {
            DispatchCalls++;
            Events.Add("dispatched");
            foreach (var log in Logs.Where(l => emailLogIds.Contains(l.Id)))
            {
                if (DeliverEmails)
                {
                    log.DeliveryStatus = EmailDeliveryStatus.Sent;
                    log.SentAt = _clock.UtcNow;
                }
                else
                {
                    log.DeliveryStatus = EmailDeliveryStatus.Failed;
                    log.Error = "No email provider configured";
                }
            }

            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("saved");
            return Task.FromResult(1);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
