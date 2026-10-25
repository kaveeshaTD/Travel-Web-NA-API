using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Users;

/// <summary>What the admin sends to create an account.</summary>
public sealed record CreateUserRequest(
    string? Name = null,
    string? Email = null,
    string? Password = null,
    string? Role = null,
    string? Phone = null,
    string? Country = null);

/// <summary>The admin's edit. A field that is not sent keeps its value; the email and password cannot be changed here.</summary>
public sealed record UserUpdateRequest(
    string? Name = null,
    string? Phone = null,
    string? Country = null,
    string? Role = null,
    string? Status = null);

public sealed record CreateUserDraft(string Name, string Email, string Password, string Role, string Phone, string Country);

public sealed record UserUpdateDraft(string Name, string Phone, string Country, string Role, string Status);

/// <summary>A user as the admin sees them. Never contains password data.</summary>
public sealed record AdminUserDto(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    string Phone,
    string Country,
    UserStatus Status,
    DateTimeOffset CreatedAt,
    int BookingCount)
{
    public static AdminUserDto Of(User user, int bookingCount) => new(
        user.Id,
        user.Name,
        user.Email,
        user.Role,
        user.Phone ?? string.Empty,
        user.Country ?? string.Empty,
        user.Status,
        user.CreatedAt,
        bookingCount);
}

public sealed record UserWithBookingCount(User User, int BookingCount);

public interface IUserAdminRepository
{
    /// <summary>Every user, newest first, with the number of their bookings (one grouped query).</summary>
    Task<IReadOnlyList<UserWithBookingCount>> ListWithBookingCountsAsync(CancellationToken cancellationToken = default);

    Task<int> CountBookingsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public static class UserRules
{
    public const string NameMessage = "Enter a name";
    public const string RoleMessage = "Role must be admin or customer";
    public const string StatusMessage = "Status must be active or inactive";
    public const string SelfMessage = "You cannot remove your own admin access or deactivate yourself";
    public const string NotFoundMessage = "User not found";
}

public sealed class CreateUserDraftValidator : AbstractValidator<CreateUserDraft>
{
    public CreateUserDraftValidator()
    {
        RuleFor(x => x.Name).Must(name => name.Length >= 2).WithMessage(UserRules.NameMessage);
        RuleFor(x => x.Name).MaxLength(Rules.MaxName, "Name is too long");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Role).Must(role => EnumWire.TryFromWire<UserRole>(role, out _)).WithMessage(UserRules.RoleMessage);
        RuleFor(x => x.Phone).MaxLength(Rules.MaxPhone, "Phone number is too long");
        RuleFor(x => x.Country).MaxLength(Rules.MaxCountry, "Country is too long");
    }
}

public sealed class UserUpdateDraftValidator : AbstractValidator<UserUpdateDraft>
{
    public UserUpdateDraftValidator()
    {
        RuleFor(x => x.Name).Must(name => name.Length >= 2).WithMessage(UserRules.NameMessage);
        RuleFor(x => x.Name).MaxLength(Rules.MaxName, "Name is too long");
        RuleFor(x => x.Role).Must(role => EnumWire.TryFromWire<UserRole>(role, out _)).WithMessage(UserRules.RoleMessage);
        RuleFor(x => x.Status).Must(status => EnumWire.TryFromWire<UserStatus>(status, out _)).WithMessage(UserRules.StatusMessage);
        RuleFor(x => x.Phone).MaxLength(Rules.MaxPhone, "Phone number is too long");
        RuleFor(x => x.Country).MaxLength(Rules.MaxCountry, "Country is too long");
    }
}

/// <summary>The admin's view and control of accounts: list, create, edit, deactivate.</summary>
public sealed class UserAdminService(
    IUserRepository users,
    IUserAdminRepository userQueries,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    IValidator<CreateUserDraft> createValidator,
    IValidator<UserUpdateDraft> updateValidator)
{
    public async Task<IReadOnlyList<AdminUserDto>> ListAsync(CancellationToken cancellationToken = default)
        => [.. (await userQueries.ListWithBookingCountsAsync(cancellationToken)).Select(row => AdminUserDto.Of(row.User, row.BookingCount))];

    /// <summary>Creates an active account. The email is lower-cased and must be new; the password is stored as a hash.</summary>
    public async Task<AdminUserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var draft = new CreateUserDraft(
            Merge.Text(request.Name, string.Empty),
            Rules.NormalizeEmail(request.Email),
            request.Password ?? string.Empty,
            Merge.Text(request.Role, string.Empty),
            Merge.Text(request.Phone, string.Empty),
            Merge.Text(request.Country, string.Empty));
        createValidator.ThrowIfInvalid(draft);

        if (await users.GetByEmailAsync(draft.Email, cancellationToken) is not null)
        {
            throw new ConflictException(AuthService.DuplicateEmailMessage, new Dictionary<string, string> { ["email"] = AuthService.DuplicateEmailMessage });
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = draft.Name,
            Email = draft.Email,
            PasswordHash = passwordHasher.Hash(draft.Password),
            Role = EnumWire.FromWire<UserRole>(draft.Role),
            Status = UserStatus.Active,
            Phone = NullIfEmpty(draft.Phone),
            Country = NullIfEmpty(draft.Country),
        };

        await users.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AdminUserDto.Of(user, 0);
    }

    /// <summary>
    /// Edits an account: what is not sent keeps its value, then the whole edit is validated. An admin cannot remove their own
    /// admin role or deactivate themselves. Deactivating revokes every refresh token of the user in the same save.
    /// </summary>
    public async Task<AdminUserDto> UpdateAsync(string id, UserUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var user = Guid.TryParse(id.Trim(), out var guid)
            ? await users.GetByIdAsync(guid, cancellationToken) ?? throw new NotFoundException(UserRules.NotFoundMessage)
            : throw new NotFoundException(UserRules.NotFoundMessage);

        var draft = new UserUpdateDraft(
            Merge.Text(request.Name, user.Name),
            Merge.Text(request.Phone, user.Phone ?? string.Empty),
            Merge.Text(request.Country, user.Country ?? string.Empty),
            Merge.Text(request.Role, EnumWire.ToWire(user.Role)),
            Merge.Text(request.Status, EnumWire.ToWire(user.Status)));
        updateValidator.ThrowIfInvalid(draft);

        var role = EnumWire.FromWire<UserRole>(draft.Role);
        var status = EnumWire.FromWire<UserStatus>(draft.Status);

        if (user.Id == currentUser.UserId && (role != UserRole.Admin || status != UserStatus.Active))
        {
            throw new ConflictException(UserRules.SelfMessage);
        }

        var deactivated = user.Status != UserStatus.Inactive && status == UserStatus.Inactive;

        user.Name = draft.Name;
        user.Phone = NullIfEmpty(draft.Phone);
        user.Country = NullIfEmpty(draft.Country);
        user.Role = role;
        user.Status = status;

        if (deactivated)
        {
            var now = clock.UtcNow;
            foreach (var token in await refreshTokens.GetActiveForUserAsync(user.Id, now, cancellationToken))
            {
                token.RevokedAt = now;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AdminUserDto.Of(user, await userQueries.CountBookingsAsync(user.Id, cancellationToken));
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
