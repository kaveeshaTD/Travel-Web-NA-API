using System.Security.Cryptography;
using System.Text;
using TravelWebAPI.Application.Abstractions;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>32 random bytes as base64url, hashed with SHA-256 (hex) for storage.</summary>
public sealed class TokenGenerator : ITokenGenerator
{
    private const int TokenBytes = 32;

    public GeneratedToken Generate()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        return new GeneratedToken(token, Hash(token));
    }

    public string Hash(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
