using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>Settings of the access token (<c>Jwt</c> section).</summary>
public sealed class JwtSettings
{
    public const string Section = "Jwt";
    public const int MinimumKeyLength = 32;

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "TravelWebAPI";
    public string Audience { get; set; } = "TravelWebAPI";
    public int AccessTokenMinutes { get; set; } = 15;

    public SymmetricSecurityKey CreateSigningKey()
        => Key.Length >= MinimumKeyLength
            ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
            : throw new InvalidOperationException($"{Section}:Key must be at least {MinimumKeyLength} characters.");
}

/// <summary>Creates HS256-signed access tokens with the claims <c>sub</c>, <c>email</c>, <c>name</c>, <c>role</c> and <c>jti</c>.</summary>
public sealed class JwtTokenService(IOptions<JwtSettings> options, IClock clock) : IJwtTokenService
{
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    public string CreateAccessToken(User user)
    {
        var settings = options.Value;
        var now = clock.UtcNow.UtcDateTime;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(NameClaim, user.Name),
                new Claim(RoleClaim, EnumWire.ToWire(user.Role)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(settings.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(settings.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
