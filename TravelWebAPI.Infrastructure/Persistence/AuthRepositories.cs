using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class UserRepository(AppDbContext context) : EfRepository<User>(context), IUserRepository
{
    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => Context.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);
}

public sealed class RefreshTokenRepository(AppDbContext context) : EfRepository<RefreshToken>(context), IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => await Context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

    public Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Context.RefreshTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}

public sealed class PasswordResetTokenRepository(AppDbContext context) : EfRepository<PasswordResetToken>(context), IPasswordResetTokenRepository
{
    public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Context.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<PasswordResetToken>> GetUnusedForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Context.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(cancellationToken);
}
