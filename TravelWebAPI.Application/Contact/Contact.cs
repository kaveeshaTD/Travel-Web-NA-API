using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Application.Email;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Application.Contact;

/// <summary>What the contact form sends.</summary>
public sealed record ContactRequest(string? Name = null, string? Email = null, string? Message = null);

public sealed record ContactDraft(string Name, string Email, string Message);

/// <summary>A message in the admin inbox.</summary>
public sealed record MessageDto(Guid Id, string Name, string Email, string Message, string Status, DateTimeOffset CreatedAt, DateTimeOffset? RepliedAt)
{
    public static MessageDto Of(ContactMessage m)
        => new(m.Id, m.Name, m.Email, m.Message, EnumWire.ToWire(m.Status), m.CreatedAt, m.RepliedAt);
}

public sealed record MessageStatusRequest(string? Status = null);

/// <summary>An admin's answer to a message. The subject is optional.</summary>
public sealed record ReplyRequest(string? Subject = null, string? Body = null);

public sealed record ReplyDraft(string Subject, string Body);

public interface IContactMessageRepository : IRepository<ContactMessage>
{
    /// <summary>One page of messages, searched, filtered and sorted in the database.</summary>
    Task<PagedResult<ContactMessage>> PageAsync(MessageListFilter filter, ListQuery query, CancellationToken cancellationToken = default);
}

public sealed class ContactDraftValidator : AbstractValidator<ContactDraft>
{
    public const int MaxMessage = 5000;

    public ContactDraftValidator()
    {
        RuleFor(x => x.Name).MinLength(2, "Enter your name");
        RuleFor(x => x.Name).MaxLength(120, "Your name is too long");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Message).MinLength(10, "Please write at least 10 characters");
        RuleFor(x => x.Message).MaxLength(MaxMessage, "The message is too long");
    }
}

public sealed class ReplyDraftValidator : AbstractValidator<ReplyDraft>
{
    public const int MaxBody = 10_000;
    public const int MaxSubject = 250;

    public ReplyDraftValidator()
    {
        RuleFor(x => x.Body).MinLength(3, "Write a reply");
        RuleFor(x => x.Body).MaxLength(MaxBody, "The reply is too long");
        RuleFor(x => x.Subject).MaxLength(MaxSubject, "The subject is too long");
    }
}

/// <summary>The contact form and the admin inbox.</summary>
public sealed class ContactService(
    IContactMessageRepository messages,
    IEmailLogRepository emailLogs,
    IRepository<SiteSettings> siteSettings,
    IEmailOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    IValidator<ContactDraft> contactValidator,
    IValidator<ReplyDraft> replyValidator)
{
    public const string NotFoundMessage = "Message not found";
    public const string DefaultSiteName = "Ceylon Roads";

    /// <summary>The same address is not acknowledged again within this time, so the form cannot be used to flood somebody else's inbox.</summary>
    public static readonly TimeSpan AcknowledgementPause = TimeSpan.FromHours(1);

    public static readonly string StatusMessage = $"Status must be one of: {string.Join(", ", Enum.GetValues<MessageStatus>().Select(EnumWire.ToWire))}";

    /// <summary>Saves a visitor's message and records an acknowledgement (sent after the save). A failed email never fails the request.</summary>
    public async Task<SubmissionReceipt> SubmitAsync(ContactRequest request, CancellationToken cancellationToken = default)
    {
        var draft = new ContactDraft(
            Merge.Text(request.Name, string.Empty),
            Rules.NormalizeEmail(request.Email),
            Merge.Text(request.Message, string.Empty));
        contactValidator.ThrowIfInvalid(draft);

        var message = new ContactMessage
        {
            Id = Guid.CreateVersion7(),
            Name = draft.Name,
            Email = draft.Email,
            Message = draft.Message,
            Status = MessageStatus.New,
        };
        await messages.AddAsync(message, cancellationToken);

        Guid? emailId = null;
        if (!await emailLogs.AcknowledgedSinceAsync(draft.Email, clock.UtcNow - AcknowledgementPause, cancellationToken))
        {
            var settings = await siteSettings.GetByIdAsync(SiteSettings.SingletonId, cancellationToken);
            emailId = await outbox.QueueAsync(
                ContactEmails.Acknowledgement(
                    draft.Email,
                    draft.Name,
                    SiteName(settings),
                    settings?.DriverName ?? string.Empty,
                    draft.Message,
                    message.Id),
                cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (emailId is { } id)
        {
            await outbox.DispatchAsync([id], cancellationToken);
        }

        return new SubmissionReceipt(References.For("MSG", message.Id));
    }

    /// <summary>One page of the inbox, newest first unless sorted otherwise.</summary>
    public async Task<PagedResult<MessageDto>> ListAsync(MessageListRequest request, CancellationToken cancellationToken = default)
    {
        var (query, filter) = MessageLists.Parse(request);
        return (await messages.PageAsync(filter, query, cancellationToken)).Map(ToDto);
    }

    public async Task<MessageDto> SetStatusAsync(Guid id, MessageStatusRequest request, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);

        var status = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!EnumWire.TryFromWire<MessageStatus>(status, out var parsed))
        {
            throw new ValidationException("status", StatusMessage);
        }

        message.Status = parsed;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(message);
    }

    /// <summary>
    /// Records the reply email, marks the message replied and returns the email log entry. The message is marked even when the
    /// email cannot be delivered (the admin did answer); the returned entry shows what happened to the email.
    /// </summary>
    public async Task<EmailLogDto> ReplyAsync(Guid id, ReplyRequest request, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        var settings = await siteSettings.GetByIdAsync(SiteSettings.SingletonId, cancellationToken);

        var subject = Merge.Text(request.Subject, string.Empty);
        var draft = new ReplyDraft(
            subject.Length == 0 ? ContactEmails.DefaultReplySubject(SiteName(settings)) : subject,
            Merge.Text(request.Body, string.Empty));
        replyValidator.ThrowIfInvalid(draft);

        var emailId = await outbox.QueueAsync(
            new EmailMessage(EmailKind.Reply, message.Email, draft.Subject, draft.Body, ContactMessageId: message.Id),
            cancellationToken);

        message.Status = MessageStatus.Replied;
        message.RepliedAt = clock.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await outbox.DispatchAsync([emailId], cancellationToken);

        var logged = await emailLogs.GetAsync(emailId, cancellationToken) ?? throw new NotFoundException("Email not found");
        return EmailLogDto.Of(logged);
    }

    /// <summary>Deletes the message. Its email log entries stay, with the link cleared (a database rule).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var message = await messages.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        messages.Remove(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string SiteName(SiteSettings? settings)
        => string.IsNullOrWhiteSpace(settings?.Name) ? DefaultSiteName : settings.Name;

    private static MessageDto ToDto(ContactMessage m) => MessageDto.Of(m);
}
