using System.Security.Cryptography;
using TravelWebAPI.Application.Abstractions;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>
/// PBKDF2-HMAC-SHA256 with a random salt. Format: <c>v1.{iterations}.{salt}.{hash}</c> (base64),
/// so the cost can be raised later and old hashes upgraded on sign-in.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const string Version = "v1";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int CurrentIterations = 600_000;

    private readonly int _iterations;

    public PasswordHasher() : this(CurrentIterations)
    {
    }

    /// <summary>Lets tests use a cheap cost. Production uses the default.</summary>
    internal PasswordHasher(int iterations)
    {
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Version}.{_iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || !TryParse(hash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string hash)
        => !TryParse(hash, out var iterations, out _, out _) || iterations < CurrentIterations;

    private static bool TryParse(string? hash, out int iterations, out byte[] salt, out byte[] expected)
    {
        iterations = 0;
        salt = [];
        expected = [];

        var parts = hash?.Split('.');
        if (parts is not { Length: 4 } || parts[0] != Version || !int.TryParse(parts[1], out iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && expected.Length > 0;
    }
}
