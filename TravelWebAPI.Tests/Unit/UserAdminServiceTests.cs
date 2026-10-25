using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Users;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

/// <summary>The user store, the sessions and the unit of work in memory.</summary>
public sealed class UserAdminHarness : IUserRepository, IUserAdminRepository, IRefreshTokenRepository, IUnitOfWork
{
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));

    public FakeCurrentUser CurrentUser { get; } = new();

    public List<User> Users { get; } = [];

    public List<RefreshToken> Tokens { get; } = [];

    public Dictionary<Guid, int> Bookings { get; } = [];

    public int Saves { get; private set; }

    public UserAdminService Create() => new(this, this, this, new FakeHasher(), this, CurrentUser, Clock, new CreateUserDraftValidator(), new UserUpdateDraftValidator());

    public User AddUser(string email = "emma@example.com", UserRole role = UserRole.Customer, UserStatus status = UserStatus.Active)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = "Emma Wilson",
            Email = email,
            PasswordHash = "hash:Customer123",
            Role = role,
            Status = status,
            Phone = "+44 7700 900123",
            Country = "United Kingdom",
            CreatedAt = Clock.Now,
        };
        Users.Add(user);
        return user;
    }

    public RefreshToken AddToken(User user, bool revoked = false)
    {
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = Guid.NewGuid().ToString(),
            ExpiresAt = Clock.Now.AddDays(1),
            RevokedAt = revoked ? Clock.Now.AddMinutes(-5) : null,
        };
        Tokens.Add(token);
        return token;
    }

    public Task<User?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id.Equals(id)));

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Email == normalizedEmail));

    public Task AddAsync(User entity, CancellationToken cancellationToken = default)
    {
        entity.CreatedAt = Clock.Now;
        Users.Add(entity);
        return Task.CompletedTask;
    }

    public void Remove(User entity) => Users.Remove(entity);

    public Task<IReadOnlyList<UserWithBookingCount>> ListWithBookingCountsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UserWithBookingCount>>(
            [.. Users.OrderByDescending(u => u.CreatedAt).Select(u => new UserWithBookingCount(u, Bookings.GetValueOrDefault(u.Id)))]);

    public Task<int> CountBookingsAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult(Bookings.GetValueOrDefault(userId));

    Task<RefreshToken?> IRepository<RefreshToken>.GetByIdAsync(object id, CancellationToken cancellationToken)
        => Task.FromResult(Tokens.FirstOrDefault(t => t.Id.Equals(id)));

    public Task AddAsync(RefreshToken entity, CancellationToken cancellationToken = default)
    {
        Tokens.Add(entity);
        return Task.CompletedTask;
    }

    public void Remove(RefreshToken entity) => Tokens.Remove(entity);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

    public Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RefreshToken>>([.. Tokens.Where(t => t.UserId == userId && t.RevokedAt is null && t.ExpiresAt > now)]);

    public Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Task.FromResult(Tokens.RemoveAll(t => t.ExpiresAt < cutoff));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        => operation(cancellationToken);
}

public sealed class UserAdminServiceTests
{
    private readonly UserAdminHarness _h = new();

    private static IReadOnlyDictionary<string, string> Fields(Func<Task> action)
        => Assert.Throws<ValidationException>(() => action().GetAwaiter().GetResult()).Fields;

    private static CreateUserRequest Valid(string email = "New.Person@Example.com") => new(
        Name: "  New Person ", Email: email, Password: "Secret123", Role: "customer", Phone: " +94 77 123 4567 ", Country: " Sri Lanka ");

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task The_list_is_newest_first_with_booking_counts_and_no_password_data()
    {
        var older = _h.AddUser("older@example.com");
        _h.Clock.Advance(TimeSpan.FromHours(1));
        var newer = _h.AddUser("newer@example.com", UserRole.Admin);
        _h.Bookings[older.Id] = 3;

        var list = await _h.Create().ListAsync();

        Assert.Equal([newer.Id, older.Id], list.Select(u => u.Id));
        Assert.Equal([0, 3], list.Select(u => u.BookingCount));
        Assert.DoesNotContain(typeof(AdminUserDto).GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Create_trims_lower_cases_and_hashes_the_password()
    {
        var created = await _h.Create().CreateAsync(Valid());

        var stored = Assert.Single(_h.Users);
        Assert.Equal("new.person@example.com", stored.Email);
        Assert.Equal("New Person", stored.Name);
        Assert.Equal("hash:Secret123", stored.PasswordHash);
        Assert.Equal("+94 77 123 4567", stored.Phone);
        Assert.Equal("Sri Lanka", stored.Country);
        Assert.Equal(UserStatus.Active, stored.Status);
        Assert.Equal(UserRole.Customer, stored.Role);
        Assert.Equal(0, created.BookingCount);
        Assert.Equal(1, _h.Saves);
    }

    [Fact]
    public async Task An_admin_account_can_be_created_and_empty_phone_and_country_are_stored_as_null()
    {
        await _h.Create().CreateAsync(new CreateUserRequest("Boss Person", "boss@example.com", "Secret123", "admin"));

        var stored = Assert.Single(_h.Users);
        Assert.Equal(UserRole.Admin, stored.Role);
        Assert.Null(stored.Phone);
        Assert.Null(stored.Country);
    }

    [Fact]
    public async Task Every_wrong_field_gets_its_message_and_nothing_is_saved()
    {
        var fields = Fields(() => _h.Create().CreateAsync(new CreateUserRequest("A", "not-an-email", "short", "owner")));

        Assert.Equal("Enter a name", fields["name"]);
        Assert.Equal("Enter a valid email address", fields["email"]);
        Assert.Equal("Use at least 8 characters with letters and numbers", fields["password"]);
        Assert.Equal("Role must be admin or customer", fields["role"]);
        Assert.Empty(_h.Users);
        Assert.Equal(0, _h.Saves);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("")]
    [InlineData(null)]
    public async Task The_role_is_matched_exactly(string? role)
    {
        var fields = Fields(() => _h.Create().CreateAsync(Valid() with { Role = role }));

        Assert.Equal("Role must be admin or customer", fields["role"]);
    }

    [Fact]
    public async Task Too_long_values_are_refused()
    {
        var fields = Fields(() => _h.Create().CreateAsync(Valid() with { Name = new string('n', 121), Phone = new string('1', 41), Country = new string('c', 81) }));

        Assert.Equal(3, fields.Count);
    }

    [Theory]
    [InlineData("emma@example.com")]
    [InlineData("  EMMA@Example.com ")]
    public async Task An_email_that_exists_in_any_letter_case_is_a_conflict(string email)
    {
        _h.AddUser("emma@example.com");

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => _h.Create().CreateAsync(Valid(email)));

        Assert.Equal("That email is already registered", conflict.Fields["email"]);
        Assert.Single(_h.Users);
    }

    // ---------------------------------------------------------------- edit

    [Fact]
    public async Task Sending_only_the_phone_changes_only_the_phone()
    {
        var user = _h.AddUser();
        _h.Bookings[user.Id] = 2;

        var updated = await _h.Create().UpdateAsync(user.Id.ToString(), new UserUpdateRequest(Phone: " +94 71 000 0000 "));

        Assert.Equal("+94 71 000 0000", user.Phone);
        Assert.Equal("Emma Wilson", user.Name);
        Assert.Equal("United Kingdom", user.Country);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("hash:Customer123", user.PasswordHash);
        Assert.Equal(2, updated.BookingCount);
    }

    [Fact]
    public async Task A_customer_can_be_promoted_and_a_phone_can_be_cleared()
    {
        var user = _h.AddUser();

        await _h.Create().UpdateAsync(user.Id.ToString(), new UserUpdateRequest(Role: "admin", Phone: ""));

        Assert.Equal(UserRole.Admin, user.Role);
        Assert.Null(user.Phone);
    }

    [Fact]
    public async Task Wrong_values_in_an_edit_change_nothing()
    {
        var user = _h.AddUser();

        var fields = Fields(() => _h.Create().UpdateAsync(user.Id.ToString(), new UserUpdateRequest(Name: "E", Role: "owner", Status: "banned")));

        Assert.Equal("Enter a name", fields["name"]);
        Assert.Equal("Role must be admin or customer", fields["role"]);
        Assert.Equal("Status must be active or inactive", fields["status"]);
        Assert.Equal("Emma Wilson", user.Name);
        Assert.Equal(0, _h.Saves);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task An_unknown_user_is_404(string id)
    {
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => _h.Create().UpdateAsync(id, new UserUpdateRequest(Name: "Someone")));

        Assert.Equal("User not found", missing.Message);
    }

    // ---------------------------------------------------------------- own account

    [Theory]
    [InlineData("customer", null)]
    [InlineData(null, "inactive")]
    [InlineData("customer", "inactive")]
    public async Task An_admin_cannot_demote_or_deactivate_themselves(string? role, string? status)
    {
        var me = _h.AddUser("me@example.com", UserRole.Admin);
        _h.CurrentUser.UserId = me.Id;
        var token = _h.AddToken(me);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => _h.Create().UpdateAsync(me.Id.ToString(), new UserUpdateRequest(Role: role, Status: status)));

        Assert.Equal("You cannot remove your own admin access or deactivate yourself", conflict.Message);
        Assert.Equal(UserRole.Admin, me.Role);
        Assert.Equal(UserStatus.Active, me.Status);
        Assert.Null(token.RevokedAt);
        Assert.Equal(0, _h.Saves);
    }

    [Fact]
    public async Task An_admin_can_edit_their_own_name_and_keep_their_role()
    {
        var me = _h.AddUser("me@example.com", UserRole.Admin);
        _h.CurrentUser.UserId = me.Id;

        await _h.Create().UpdateAsync(me.Id.ToString(), new UserUpdateRequest(Name: "Nuwan Admin", Role: "admin", Status: "active"));

        Assert.Equal("Nuwan Admin", me.Name);
    }

    [Fact]
    public async Task Another_admin_can_be_demoted_and_deactivated()
    {
        var me = _h.AddUser("me@example.com", UserRole.Admin);
        var other = _h.AddUser("other@example.com", UserRole.Admin);
        _h.CurrentUser.UserId = me.Id;

        await _h.Create().UpdateAsync(other.Id.ToString(), new UserUpdateRequest(Role: "customer", Status: "inactive"));

        Assert.Equal(UserRole.Customer, other.Role);
        Assert.Equal(UserStatus.Inactive, other.Status);
    }

    // ---------------------------------------------------------------- sessions

    [Fact]
    public async Task Deactivating_revokes_every_active_token_of_that_user_in_one_save()
    {
        var target = _h.AddUser();
        var bystander = _h.AddUser("other@example.com");
        var one = _h.AddToken(target);
        var two = _h.AddToken(target);
        var alreadyRevoked = _h.AddToken(target, revoked: true);
        var revokedAt = alreadyRevoked.RevokedAt;
        var others = _h.AddToken(bystander);

        await _h.Create().UpdateAsync(target.Id.ToString(), new UserUpdateRequest(Status: "inactive"));

        Assert.Equal(_h.Clock.Now, one.RevokedAt);
        Assert.Equal(_h.Clock.Now, two.RevokedAt);
        Assert.Equal(revokedAt, alreadyRevoked.RevokedAt);
        Assert.Null(others.RevokedAt);
        Assert.Equal(1, _h.Saves);
    }

    [Fact]
    public async Task Reactivating_and_other_edits_leave_tokens_alone()
    {
        var target = _h.AddUser(status: UserStatus.Inactive);
        var token = _h.AddToken(target);

        await _h.Create().UpdateAsync(target.Id.ToString(), new UserUpdateRequest(Status: "active"));
        await _h.Create().UpdateAsync(target.Id.ToString(), new UserUpdateRequest(Name: "Emma W"));

        Assert.Null(token.RevokedAt);
        Assert.Equal(UserStatus.Active, target.Status);
    }

    [Fact]
    public async Task Saving_an_already_inactive_user_again_revokes_nothing_new()
    {
        var target = _h.AddUser(status: UserStatus.Inactive);
        var token = _h.AddToken(target);

        await _h.Create().UpdateAsync(target.Id.ToString(), new UserUpdateRequest(Status: "inactive", Name: "Emma W"));

        Assert.Null(token.RevokedAt);
    }
}
