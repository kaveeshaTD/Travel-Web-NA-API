using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Payments;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class PaymentRepository(AppDbContext context) : EfRepository<Payment>(context), IPaymentRepository
{
    public Task<Payment?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => Context.Payments.FirstOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListWithBookingAsync(CancellationToken cancellationToken = default)
        => await Context.Payments
            .AsNoTracking()
            .Include(payment => payment.Booking)
            .OrderByDescending(payment => payment.PaidAt)
            .ThenByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id)
            .ToListAsync(cancellationToken);
}
