using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Application.Email;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Application.Bookings;

public interface IBookingRepository : IRepository<Booking>
{
    Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Finds a booking by its id or its <c>BK-</c> reference. Tracked, so callers can change it.</summary>
    Task<Booking?> FindAsync(string idOrReference, CancellationToken cancellationToken = default);

    /// <summary>Bookings newest first, all of them or only those of one user.</summary>
    Task<IReadOnlyList<Booking>> ListAsync(Guid? userId, CancellationToken cancellationToken = default);

    /// <summary>What was paid per booking (paid payments minus refunds, in LKR). Bookings without payments are absent.</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> PaidTotalsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken = default);

    /// <summary>The payments of a booking, newest paid first.</summary>
    Task<IReadOnlyList<Payment>> GetPaymentsAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>When the user's bookings created at or after <paramref name="since"/> were created.</summary>
    Task<IReadOnlyList<DateTimeOffset>> RecentCreatedAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default);
}

/// <summary>The complete admin edit that is validated. A missing price means "keep".</summary>
public sealed record BookingUpdateDraft(
    string Status,
    decimal? PriceLkr,
    string AdminNotes,
    string Date,
    string Time,
    string VehicleId,
    string PickupLocation,
    string DropoffLocation,
    string FlightNumber);

public sealed record BookingEmailDraft(string Subject, string Body);

public sealed class BookingUpdateDraftValidator : AbstractValidator<BookingUpdateDraft>
{
    public static readonly string StatusMessage = $"Status must be one of: {string.Join(", ", Enum.GetValues<BookingStatus>().Select(EnumWire.ToWire))}";

    public BookingUpdateDraftValidator()
    {
        RuleFor(x => x.Status).Must(status => EnumWire.TryFromWire<BookingStatus>(status, out _)).WithMessage(StatusMessage);
        RuleFor(x => x.PriceLkr).Must(price => price is null || ContentRules.IsMoney(price)).WithMessage("Enter a valid price");
        RuleFor(x => x.AdminNotes).MaxLength(4000, "The notes are too long");
        RuleFor(x => x.Date).Must(date => BookingFormats.TryDate(date, out _)).WithMessage("Choose a valid date");
        RuleFor(x => x.Time).Must(time => BookingFormats.TryTime(time, out _)).WithMessage("Choose a valid time");
        RuleFor(x => x.VehicleId).MaxLength(100, "Unknown vehicle");
        RuleFor(x => x.PickupLocation).MaxLength(250, "The pickup place is too long");
        RuleFor(x => x.DropoffLocation).MaxLength(250, "The drop-off place is too long");
        RuleFor(x => x.FlightNumber).MaxLength(20, "The flight number is too long");
    }
}

public sealed class BookingEmailDraftValidator : AbstractValidator<BookingEmailDraft>
{
    public BookingEmailDraftValidator()
    {
        RuleFor(x => x.Subject).MinLength(3, "Enter a subject");
        RuleFor(x => x.Subject).MaxLength(250, "The subject is too long");
        RuleFor(x => x.Body).MinLength(3, "Write a message");
        RuleFor(x => x.Body).MaxLength(10_000, "The message is too long");
    }
}

/// <summary>Creating and reading bookings as a customer, and managing them as the admin.</summary>
public sealed class BookingService(
    IBookingRepository bookings,
    IRepository<Service> services,
    IRepository<Package> packages,
    IRepository<Vehicle> vehicles,
    IRepository<User> users,
    IRepository<SiteSettings> siteSettings,
    IEmailLogRepository emailLogs,
    IEmailOutbox outbox,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IOptions<BookingOptions> bookingOptions,
    IOptions<OwnerEmailOptions> ownerEmailOptions,
    IValidator<BookingDraft> bookingValidator,
    IValidator<BookingUpdateDraft> updateValidator,
    IValidator<BookingEmailDraft> emailValidator)
{
    public const string NotFoundMessage = "Booking not found";
    public const string TooManyMessage = "You have made a lot of bookings in the last hour, please try again a little later";

    private static readonly TimeSpan LimitWindow = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------- customer

    /// <summary>Creates a booking for the signed-in user and records the "received" and "notice" emails (sent after the save).</summary>
    public async Task<SubmissionReceipt> CreateAsync(BookingRequest request, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var draft = ToDraft(request);
        var fields = bookingValidator.Check(draft);

        var service = await FindIfGivenAsync(services, draft.ServiceId, fields, "serviceId", "Please choose a service", cancellationToken);
        var package = await FindIfGivenAsync(packages, draft.PackageId, fields, "packageId", "Unknown package", cancellationToken, p => p.Active);
        await FindIfGivenAsync(vehicles, draft.VehicleId, fields, "vehicleId", "Unknown vehicle", cancellationToken);

        if (fields.Count > 0)
        {
            throw new ValidationException(fields);
        }

        await EnsureUnderLimitAsync(userId, cancellationToken);

        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            Reference = await NewReferenceAsync(cancellationToken),
            UserId = userId,
            ServiceId = draft.ServiceId,
            PackageId = NullIfEmpty(draft.PackageId),
            VehicleId = NullIfEmpty(draft.VehicleId),
            TravelDate = DateOnly.ParseExact(draft.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            TravelTime = TimeOnly.ParseExact(draft.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            FlightNumber = NullIfEmpty(draft.FlightNumber),
            PickupLocation = NullIfEmpty(draft.PickupLocation),
            DropoffLocation = NullIfEmpty(draft.DropoffLocation),
            Adults = (int)draft.Adults,
            Children = (int)draft.Children,
            Bags = (int)draft.Bags,
            Notes = NullIfEmpty(draft.Notes),
            Name = draft.Name,
            Email = draft.Email,
            Whatsapp = draft.Whatsapp,
            Country = draft.Country,
            TripPlanJson = draft.TripPlan is null ? null : JsonSerializer.Serialize(draft.TripPlan, Json),
            Status = BookingStatus.Pending,
            PriceLkr = BookingMoney.DefaultPrice(package?.PriceLkr, draft.TripPlan),
        };
        await bookings.AddAsync(booking, cancellationToken);

        var settings = await siteSettings.GetByIdAsync(SiteSettings.SingletonId, cancellationToken);
        var emailIds = new List<Guid>
        {
            await outbox.QueueAsync(BookingEmails.Received(booking, service!.Title, settings?.DriverName ?? string.Empty), cancellationToken),
        };

        var owner = OwnerAddress(settings);
        if (owner.Length > 0)
        {
            emailIds.Add(await outbox.QueueAsync(BookingEmails.Notice(owner, booking, service.Title), cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await outbox.DispatchAsync(emailIds, cancellationToken);

        return new SubmissionReceipt(booking.Reference);
    }

    /// <summary>The signed-in user's bookings, newest first, without admin notes.</summary>
    public async Task<IReadOnlyList<BookingDto>> ListMineAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        return await ToDtosAsync(await bookings.ListAsync(userId, cancellationToken), admin: false, cancellationToken);
    }

    /// <summary>
    /// One booking by id or reference. A customer sees only their own, and a booking of somebody else looks exactly like one
    /// that does not exist. The admin also gets the payments, the emails and the customer.
    /// </summary>
    public async Task<BookingDto> GetAsync(string idOrReference, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.FindAsync(idOrReference.Trim(), cancellationToken) ?? throw new NotFoundException(NotFoundMessage);

        if (!currentUser.IsAdmin)
        {
            if (booking.UserId != currentUser.UserId)
            {
                throw new NotFoundException(NotFoundMessage);
            }

            var totals = await bookings.PaidTotalsAsync([booking.Id], cancellationToken);
            return ToDto(booking, totals.GetValueOrDefault(booking.Id), admin: false);
        }

        return await ToDetailAsync(booking, cancellationToken);
    }

    // ---------------------------------------------------------------- admin

    public async Task<IReadOnlyList<BookingDto>> ListAllAsync(CancellationToken cancellationToken = default)
        => await ToDtosAsync(await bookings.ListAsync(userId: null, cancellationToken), admin: true, cancellationToken);

    /// <summary>
    /// Edits a booking: what is not sent keeps its value, then the whole edit is validated. A real change of status to
    /// confirmed, cancelled or completed records one email to the customer in the same save.
    /// </summary>
    public async Task<BookingDto> UpdateAsync(string id, BookingUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.FindAsync(id.Trim(), cancellationToken) ?? throw new NotFoundException(NotFoundMessage);

        var draft = new BookingUpdateDraft(
            Merge.Text(request.Status, EnumWire.ToWire(booking.Status)).ToLowerInvariant(),
            request.PriceLkr is null ? booking.PriceLkr : request.PriceLkr.Value,
            Merge.Text(request.AdminNotes, booking.AdminNotes ?? string.Empty),
            Merge.Text(request.Date, BookingFormats.Date(booking.TravelDate)),
            Merge.Text(request.Time, BookingFormats.Time(booking.TravelTime)),
            Merge.Text(request.VehicleId, booking.VehicleId ?? string.Empty),
            Merge.Text(request.PickupLocation, booking.PickupLocation ?? string.Empty),
            Merge.Text(request.DropoffLocation, booking.DropoffLocation ?? string.Empty),
            Merge.Text(request.FlightNumber, booking.FlightNumber ?? string.Empty));

        var fields = updateValidator.Check(draft);
        await FindIfGivenAsync(vehicles, draft.VehicleId, fields, "vehicleId", "Unknown vehicle", cancellationToken);
        if (fields.Count > 0)
        {
            throw new ValidationException(fields);
        }

        var previous = booking.Status;
        booking.Status = EnumWire.FromWire<BookingStatus>(draft.Status);
        booking.PriceLkr = draft.PriceLkr;
        booking.AdminNotes = NullIfEmpty(draft.AdminNotes);
        booking.TravelDate = DateOnly.ParseExact(draft.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        booking.TravelTime = TimeOnly.ParseExact(draft.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        booking.VehicleId = NullIfEmpty(draft.VehicleId);
        booking.PickupLocation = NullIfEmpty(draft.PickupLocation);
        booking.DropoffLocation = NullIfEmpty(draft.DropoffLocation);
        booking.FlightNumber = NullIfEmpty(draft.FlightNumber);

        Guid? emailId = null;
        if (booking.Status != previous)
        {
            var settings = await siteSettings.GetByIdAsync(SiteSettings.SingletonId, cancellationToken);
            var driver = settings?.DriverName ?? string.Empty;

            EmailMessage? message = booking.Status switch
            {
                BookingStatus.Confirmed => BookingEmails.Confirmed(booking, await ServiceTitleAsync(booking, cancellationToken), driver),
                BookingStatus.Cancelled => BookingEmails.Cancelled(booking, driver),
                BookingStatus.Completed => BookingEmails.Completed(booking, driver),
                _ => null,
            };

            if (message is not null)
            {
                emailId = await outbox.QueueAsync(message, cancellationToken);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (emailId is { } queued)
        {
            await outbox.DispatchAsync([queued], cancellationToken);
        }

        return await ToDetailAsync(booking, cancellationToken);
    }

    /// <summary>Deletes the booking. Its payments go with it and its email log entries stay with the link cleared (database rules).</summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.FindAsync(id.Trim(), cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        bookings.Remove(booking);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The admin's own email to the booking's customer. The answer is the log entry with its real delivery status.</summary>
    public async Task<EmailLogDto> SendEmailAsync(string id, BookingEmailRequest request, CancellationToken cancellationToken = default)
    {
        var booking = await bookings.FindAsync(id.Trim(), cancellationToken) ?? throw new NotFoundException(NotFoundMessage);

        var draft = new BookingEmailDraft(Merge.Text(request.Subject, string.Empty), Merge.Text(request.Body, string.Empty));
        emailValidator.ThrowIfInvalid(draft);

        var emailId = await outbox.QueueAsync(BookingEmails.Custom(booking, draft.Subject, draft.Body), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await outbox.DispatchAsync([emailId], cancellationToken);

        var logged = await emailLogs.GetAsync(emailId, cancellationToken) ?? throw new NotFoundException("Email not found");
        return EmailLogDto.Of(logged);
    }

    // ---------------------------------------------------------------- helpers

    private static BookingDraft ToDraft(BookingRequest request)
    {
        var plan = request.TripPlan;

        return new BookingDraft(
            Merge.Text(request.ServiceId, string.Empty),
            Merge.Text(request.PackageId, string.Empty),
            Merge.Text(request.VehicleId, string.Empty),
            Merge.Text(request.Date, string.Empty),
            Merge.Text(request.Time, string.Empty),
            Merge.Text(request.FlightNumber, string.Empty),
            Merge.Text(request.PickupLocation, string.Empty),
            Merge.Text(request.DropoffLocation, string.Empty),
            request.Adults ?? 0,
            request.Children ?? 0,
            request.Bags ?? 0,
            Merge.Text(request.Notes, string.Empty),
            Merge.Text(request.Name, string.Empty),
            Rules.NormalizeEmail(request.Email),
            Merge.Text(request.Whatsapp, string.Empty),
            Merge.Text(request.Country, string.Empty),
            plan is null
                ? null
                : new TripPlanDto(
                    Merge.Lines(plan.DestinationIds, []),
                    WholeOrZero(plan.Days),
                    WholeOrZero(plan.Travelers),
                    Merge.Text(plan.VehicleId, string.Empty),
                    [.. (plan.Stops ?? []).Select(stop => new TripStopDto(
                        Merge.Text(stop?.Id, string.Empty), Merge.Text(stop?.Name, string.Empty), stop?.Lat ?? 0, stop?.Lng ?? 0))],
                    plan.DistanceKm ?? 0,
                    plan.EstimateLowLkr ?? 0,
                    plan.EstimateHighLkr ?? 0));
    }

    /// <summary>A trip plan count that is not a whole number cannot be valid, so it becomes 0 and fails the plan check.</summary>
    private static int WholeOrZero(decimal? value)
        => value is { } number && number == decimal.Truncate(number) && number is >= 0 and <= 1000 ? (int)number : 0;

    /// <summary>Looks up the record named by <paramref name="id"/> when one is given. A missing or unacceptable one is a field error.</summary>
    private static async Task<T?> FindIfGivenAsync<T>(
        IRepository<T> repository,
        string id,
        Dictionary<string, string> fields,
        string field,
        string message,
        CancellationToken cancellationToken,
        Func<T, bool>? accept = null)
        where T : class
    {
        if (id.Length == 0 || fields.ContainsKey(field))
        {
            return null;
        }

        var found = await repository.GetByIdAsync(id, cancellationToken);
        if (found is null || accept?.Invoke(found) == false)
        {
            fields[field] = message;
            return null;
        }

        return found;
    }

    private async Task EnsureUnderLimitAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var recent = await bookings.RecentCreatedAsync(userId, now - LimitWindow, cancellationToken);

        if (recent.Count >= bookingOptions.Value.MaxPerUserPerHour)
        {
            var retryAfter = recent.Min() + LimitWindow - now;
            throw new TooManyRequestsException(TooManyMessage, retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1));
        }
    }

    private async Task<string> NewReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var reference = BookingReference.New();
            if (!await bookings.ReferenceExistsAsync(reference, cancellationToken))
            {
                return reference;
            }
        }

        throw new ConflictException("Could not create a booking reference, please try again");
    }

    private string OwnerAddress(SiteSettings? settings)
    {
        var configured = ownerEmailOptions.Value.OwnerEmail.Trim();
        return configured.Length > 0 ? configured : settings?.Email.Trim() ?? string.Empty;
    }

    private async Task<string> ServiceTitleAsync(Booking booking, CancellationToken cancellationToken)
        => (await services.GetByIdAsync(booking.ServiceId, cancellationToken))?.Title ?? "trip";

    private async Task<IReadOnlyList<BookingDto>> ToDtosAsync(IReadOnlyList<Booking> list, bool admin, CancellationToken cancellationToken)
    {
        var totals = await bookings.PaidTotalsAsync([.. list.Select(b => b.Id)], cancellationToken);
        return [.. list.Select(b => ToDto(b, totals.GetValueOrDefault(b.Id), admin))];
    }

    private async Task<BookingDto> ToDetailAsync(Booking booking, CancellationToken cancellationToken)
    {
        var payments = await bookings.GetPaymentsAsync(booking.Id, cancellationToken);
        var emails = await emailLogs.ListForBookingAsync(booking.Id, cancellationToken);
        var customer = await users.GetByIdAsync(booking.UserId, cancellationToken);

        return ToDto(booking, BookingMoney.Paid(payments), admin: true) with
        {
            Payments = [.. payments.Select(ToPaymentDto)],
            Emails = [.. emails.Select(EmailLogDto.Of)],
            Customer = customer is null ? null : new CustomerDto(customer.Id, customer.Name, customer.Email, customer.Phone ?? string.Empty, customer.Country ?? string.Empty),
        };
    }

    private static BookingDto ToDto(Booking b, decimal paid, bool admin)
    {
        var paidLkr = BookingMoney.Round(paid);

        return new BookingDto(
            b.Id,
            b.Reference,
            b.UserId,
            b.ServiceId,
            b.PackageId,
            b.VehicleId,
            BookingFormats.Date(b.TravelDate),
            BookingFormats.Time(b.TravelTime),
            b.FlightNumber ?? string.Empty,
            b.PickupLocation ?? string.Empty,
            b.DropoffLocation ?? string.Empty,
            b.Adults,
            b.Children,
            b.Bags,
            b.Notes ?? string.Empty,
            b.Name,
            b.Email,
            b.Whatsapp,
            b.Country,
            b.TripPlanJson is null ? null : JsonSerializer.Deserialize<TripPlanDto>(b.TripPlanJson, Json),
            EnumWire.ToWire(b.Status),
            b.PriceLkr,
            paidLkr,
            BookingMoney.Balance(b.PriceLkr, paidLkr),
            b.CreatedAt,
            b.UpdatedAt)
        {
            AdminNotes = admin ? b.AdminNotes ?? string.Empty : null,
        };
    }

    private static PaymentDto ToPaymentDto(Payment p) => new(
        p.Id,
        p.BookingId,
        p.Amount,
        EnumWire.ToWire(p.Currency),
        p.ExchangeRate,
        p.AmountLkr,
        EnumWire.ToWire(p.Method),
        EnumWire.ToWire(p.Status),
        BookingFormats.Date(p.PaidAt),
        p.Notes ?? string.Empty,
        p.CreatedAt);

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
