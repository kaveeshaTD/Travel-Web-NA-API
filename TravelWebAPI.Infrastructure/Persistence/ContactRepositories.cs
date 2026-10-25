using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class ContactMessageRepository(AppDbContext context) : EfRepository<ContactMessage>(context), IContactMessageRepository
{
    public async Task<IReadOnlyList<ContactMessage>> ListAsync(CancellationToken cancellationToken = default)
        => await Context.ContactMessages
            .AsNoTracking()
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .ToListAsync(cancellationToken);
}

public sealed class EmailLogRepository(AppDbContext context) : IEmailLogRepository
{
    public async Task<IReadOnlyList<EmailLog>> ListAsync(int limit, CancellationToken cancellationToken = default)
        => await context.EmailLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>Read from the database, not from the change tracker, so the delivery result written by the outbox is included.</summary>
    public Task<EmailLog?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => context.EmailLogs.AsNoTracking().FirstOrDefaultAsync(log => log.Id == id, cancellationToken);

    public Task<bool> AcknowledgedSinceAsync(string address, DateTimeOffset since, CancellationToken cancellationToken = default)
        => context.EmailLogs.AnyAsync(
            log => log.Kind == EmailKind.ContactAck && log.ToAddress == address && log.CreatedAt > since,
            cancellationToken);
}
