namespace TravelWebAPI.Application.Abstractions;

public sealed record GeneratedToken(string Token, string Hash);

/// <summary>Creates random one-time tokens (refresh, password reset) and hashes them for storage.</summary>
public interface ITokenGenerator
{
    GeneratedToken Generate();

    /// <summary>SHA-256 hex of a token. Only this hash is ever stored.</summary>
    string Hash(string token);
}
