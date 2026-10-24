using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Abstractions;

/// <summary>The caller of the current request, without any HTTP types.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Email { get; }

    UserRole? Role { get; }

    bool IsAdmin { get; }
}
