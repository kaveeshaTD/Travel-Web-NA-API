using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Abstractions;

public interface IJwtTokenService
{
    /// <summary>Creates a short-lived signed access token with the user's id, email, name and role.</summary>
    string CreateAccessToken(User user);
}
