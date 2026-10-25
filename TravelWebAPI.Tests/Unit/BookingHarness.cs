using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Unit;

/// <summary>A generic in-memory repository for the lookup tables the booking service reads.</summary>
public sealed class FakeLookup<T>(Func<T, object> keyOf) : IRepository<T>
    where T : class
{
    public List<T> Items { get; } = [];

    public Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => Task.FromResult(Items.FirstOrDefault(item => keyOf(item).Equals(id)));

    public Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Remove(T entity) => Items.Remove(entity);
}

/// <summary>
/// Everything the booking service talks to, in memory: the booking store, the lookups, the email log and outbox, the
/// unit of work and the current user. Tests set it up through the public members and build the service with <see cref="Create"/>.
/// </summary>
public sealed class BookingHarness : IBookingRepository, IEmailLogRepository, IEmailOutbox, IUnitOfWork
{
    public static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid OtherCustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public BookingHarness()
    {
        Clock = new FakeClock(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));
        CurrentUser = new HarnessUser { UserId = CustomerId };

        Services.Items.Add(new Service { Id = "airport", Title = "Airport pickup" });
        Services.Items.Add(new Service { Id = "private-tour", Title = "Private tour" });
        Packages.Items.Add(new Package { Id = "cultural-triangle", Name = "Cultural Triangle", PriceLkr = 99_000m, Active = true });
        Packages.Items.Add(new Package { Id = "hidden", Name = "Hidden", PriceLkr = 50_000m, Active = false });
        Vehicles.Items.Add(new Vehicle { Id = "toyota-prius", Name = "Toyota Prius" });
        Users.Items.Add(new User { Id = CustomerId, Name = "Emma Wilson", Email = "emma@example.com", Phone = "+44 7700 900123", Country = "United Kingdom" });
        Settings.Items.Add(new SiteSettings { Name = "Ceylon Roads", DriverName = "Nuwan", Email = "hello@ceylonroads.example" });
    }

    public FakeClock Clock { get; }

    public HarnessUser CurrentUser { get; }

    public FakeLookup<Service> Services { get; } = new(s => s.Id);

    public FakeLookup<Package> Packages { get; } = new(p => p.Id);

    public FakeLookup<Vehicle> Vehicles { get; } = new(v => v.Id);

    public FakeLookup<User> Users { get; } = new(u => u.Id);

    public FakeLookup<SiteSettings> Settings { get; } = new(_ => SiteSettings.SingletonId);

    public BookingOptions Options { get; } = new();

    public OwnerEmailOptions Owner { get; } = new();

    public List<Booking> Bookings { get; } = [];

    public List<Payment> Payments { get; } = [];

    public List<EmailLog> Logs { get; } = [];

    public List<Guid> Dispatched { get; } = [];

    public List<string> Events { get; } = [];

    /// <summary>How many reference checks answer "already used" before one answers "free".</summary>
    public int TakenReferenceAnswers { get; set; }

    public int ReferenceChecks { get; private set; }

    public int Saves { get; private set; }

    public bool DeliverEmails { get; set; }

    public BookingService Create() => new(
        this,
        Services,
        Packages,
        Vehicles,
        Users,
        Settings,
        this,
        this,
        this,
        Clock,
        CurrentUser,
        Microsoft.Extensions.Options.Options.Create(Options),
        Microsoft.Extensions.Options.Options.Create(Owner),
        new BookingDraftValidator(Clock),
        new BookingUpdateDraftValidator(),
        new BookingEmailDraftValidator());

    public static BookingRequest ValidRequest() => new(
        ServiceId: "private-tour",
        Date: "2026-12-01",
        Time: "09:30",
        PickupLocation: "Colombo",
        DropoffLocation: "Kandy",
        Adults: 2,
        Children: 1,
        Bags: 3,
        Notes: "Quiet driver please",
        Name: "Emma Wilson",
        Email: "emma@example.com",
        Whatsapp: "+44 7700 900123",
        Country: "United Kingdom");

    public Booking AddBooking(Guid? userId = null, string reference = "BK-AAAAAA", BookingStatus status = BookingStatus.Pending, decimal? price = 100_000m, DateTimeOffset? created = null)
    {
        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            Reference = reference,
            UserId = userId ?? CustomerId,
            ServiceId = "private-tour",
            TravelDate = new DateOnly(2026, 12, 1),
            TravelTime = new TimeOnly(9, 30),
            Adults = 2,
            Name = "Emma Wilson",
            Email = "emma@example.com",
            Whatsapp = "+44 7700 900123",
            Country = "United Kingdom",
            Status = status,
            PriceLkr = price,
            AdminNotes = "internal only",
            CreatedAt = created ?? Clock.UtcNow,
            UpdatedAt = created ?? Clock.UtcNow,
        };
        Bookings.Add(booking);
        return booking;
    }

    // ---------------------------------------------------------------- IBookingRepository

    public Task<Booking?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => Task.FromResult(Bookings.FirstOrDefault(b => b.Id.Equals(id)));

    public Task AddAsync(Booking entity, CancellationToken cancellationToken = default)
    {
        entity.CreatedAt = Clock.UtcNow;
        entity.UpdatedAt = Clock.UtcNow;
        Bookings.Add(entity);
        return Task.CompletedTask;
    }

    public void Remove(Booking entity) => Bookings.Remove(entity);

    public Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default)
    {
        ReferenceChecks++;
        return Task.FromResult(ReferenceChecks <= TakenReferenceAnswers || Bookings.Any(b => b.Reference == reference));
    }

    public Task<Booking?> FindAsync(string idOrReference, CancellationToken cancellationToken = default)
        => Task.FromResult(Bookings.FirstOrDefault(b =>
            string.Equals(b.Reference, idOrReference, StringComparison.OrdinalIgnoreCase) || b.Id.ToString() == idOrReference));

    /// <summary>A plain in-memory page (newest first); the real filtering, sorting and searching are tested against PostgreSQL.</summary>
    public Task<PagedResult<Booking>> PageAsync(Guid? userId, BookingListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var rows = Bookings.Where(b => userId is null || b.UserId == userId).OrderByDescending(b => b.CreatedAt).ToList();
        return Task.FromResult(Paged.Of<Booking>([.. rows.Skip(query.Skip).Take(query.PageSize)], query, rows.Count));
    }

    public Task<IReadOnlyDictionary<Guid, decimal>> PaidTotalsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(
            bookingIds.ToDictionary(id => id, id => BookingMoney.Paid(Payments.Where(p => p.BookingId == id))));

    public Task<IReadOnlyList<Payment>> GetPaymentsAsync(Guid bookingId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Payment>>([.. Payments.Where(p => p.BookingId == bookingId).OrderByDescending(p => p.PaidAt)]);

    public Task<IReadOnlyList<DateTimeOffset>> RecentCreatedAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DateTimeOffset>>([.. Bookings.Where(b => b.UserId == userId && b.CreatedAt >= since).Select(b => b.CreatedAt)]);

    // ---------------------------------------------------------------- IEmailLogRepository

    public Task<PagedResult<EmailLog>> PageAsync(EmailListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var rows = Logs.OrderByDescending(l => l.CreatedAt).ToList();
        return Task.FromResult(Paged.Of<EmailLog>([.. rows.Skip(query.Skip).Take(query.PageSize)], query, rows.Count));
    }

    public Task<EmailLog?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(Logs.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<EmailLog>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<EmailLog>>([.. Logs.Where(l => l.BookingId == bookingId).OrderByDescending(l => l.CreatedAt)]);

    public Task<bool> AcknowledgedSinceAsync(string address, DateTimeOffset since, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    // ---------------------------------------------------------------- IEmailOutbox

    public Task<Guid> QueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var log = new EmailLog
        {
            Id = Guid.CreateVersion7(),
            Kind = message.Kind,
            ToAddress = message.To,
            Subject = message.Subject,
            Body = message.Body,
            BookingId = message.BookingId,
            ContactMessageId = message.ContactMessageId,
            DeliveryStatus = EmailDeliveryStatus.Queued,
            CreatedAt = Clock.UtcNow,
        };
        Logs.Add(log);
        return Task.FromResult(log.Id);
    }

    public Task DispatchAsync(IEnumerable<Guid> emailLogIds, CancellationToken cancellationToken = default)
    {
        Events.Add("dispatched");
        foreach (var id in emailLogIds)
        {
            Dispatched.Add(id);
            var log = Logs.First(l => l.Id == id);
            if (DeliverEmails)
            {
                log.DeliveryStatus = EmailDeliveryStatus.Sent;
                log.SentAt = Clock.UtcNow;
            }
            else
            {
                log.DeliveryStatus = EmailDeliveryStatus.Failed;
                log.Error = "No email provider configured";
            }
        }

        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------- IUnitOfWork

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        Events.Add("saved");
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);

    /// <summary>A current user whose role and id the tests can change.</summary>
    public sealed class HarnessUser : ICurrentUser
    {
        public Guid? UserId { get; set; }

        public UserRole? RoleValue { get; set; } = UserRole.Customer;

        public bool IsAuthenticated => UserId is not null;

        public string? Email => null;

        public UserRole? Role => IsAuthenticated ? RoleValue : null;

        public bool IsAdmin => Role == UserRole.Admin;
    }
}
