using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Abstractions;

public interface IUserRepository : IRepository<User>
{
    /// <summary>Finds a user by email (already trimmed and lower-cased). The result is tracked for updates.</summary>
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
}

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Refresh tokens of a user that are not revoked and not expired. Tracked for updates.</summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Deletes tokens that expired before <paramref name="cutoff"/>. Runs immediately.</summary>
    Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

public interface IPasswordResetTokenRepository : IRepository<PasswordResetToken>
{
    Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Reset tokens of a user that are not used yet. Tracked for updates.</summary>
    Task<IReadOnlyList<PasswordResetToken>> GetUnusedForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
