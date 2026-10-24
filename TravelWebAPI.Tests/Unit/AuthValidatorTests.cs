using TravelWebAPI.Application.Auth;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Email;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Unit;

public class AuthValidatorTests
{
    [Theory]
    [InlineData("emma@example.com", true)]
    [InlineData("  emma@example.com  ", true)]
    [InlineData("a.b+c@sub.example.co.uk", true)]
    [InlineData("emma@example", false)]
    [InlineData("emma.example.com", false)]
    [InlineData("emma @example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Email_format(string? email, bool valid) => Assert.Equal(valid, Rules.IsEmail(email));

    [Fact]
    public void Email_longer_than_254_characters_is_invalid()
        => Assert.False(Rules.IsEmail(new string('a', 250) + "@x.co"));

    [Theory]
    [InlineData("  Emma@Example.COM ", "emma@example.com")]
    [InlineData(null, "")]
    public void Email_is_normalised(string? input, string expected) => Assert.Equal(expected, Rules.NormalizeEmail(input));

    [Theory]
    [InlineData("Customer123", true)]
    [InlineData("abcdefg1", true)]
    [InlineData("short1A", false)]
    [InlineData("alllettersnodigit", false)]
    [InlineData("1234567890", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Password_rule(string? password, bool strong) => Assert.Equal(strong, Rules.IsStrongPassword(password));

    [Fact]
    public void Register_uses_the_contract_messages()
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest { Name = "E", Email = "x", Password = "p" });

        Assert.Equal("Enter your full name", result.Errors.Single(e => e.PropertyName == "Name").ErrorMessage);
        Assert.Equal("Enter a valid email address", result.Errors.Single(e => e.PropertyName == "Email").ErrorMessage);
        Assert.Equal("Use at least 8 characters with letters and numbers", result.Errors.Single(e => e.PropertyName == "Password").ErrorMessage);
    }

    [Fact]
    public void Register_accepts_a_complete_valid_request()
    {
        var result = new RegisterRequestValidator().Validate(
            new RegisterRequest { Name = "Emma Wilson", Email = "emma@example.com", Password = "Customer123", Phone = "+44 7700 900123", Country = "United Kingdom" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Register_limits_the_length_of_free_text_fields_to_the_database_columns()
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest
        {
            Name = new string('n', 121),
            Email = "emma@example.com",
            Password = "Customer123",
            Phone = new string('1', 41),
            Country = new string('c', 81),
        });

        Assert.Equal(["Country", "Name", "Phone"], result.Errors.Select(e => e.PropertyName).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Profile_update_only_checks_what_was_sent()
    {
        var validator = new UpdateProfileRequestValidator();

        Assert.True(validator.Validate(new UpdateProfileRequest()).IsValid);
        Assert.True(validator.Validate(new UpdateProfileRequest { Phone = "" }).IsValid);
        Assert.False(validator.Validate(new UpdateProfileRequest { Name = "E" }).IsValid);
        Assert.False(validator.Validate(new UpdateProfileRequest { NewPassword = "weak" }).IsValid);
        Assert.True(validator.Validate(new UpdateProfileRequest { CurrentPassword = "x", NewPassword = "Brand1New" }).IsValid);
    }

    [Fact]
    public void Forgot_needs_a_valid_email()
    {
        var validator = new ForgotPasswordRequestValidator();

        Assert.True(validator.Validate(new ForgotPasswordRequest { Email = "emma@example.com" }).IsValid);
        Assert.Equal("Enter a valid email address", validator.Validate(new ForgotPasswordRequest { Email = "nope" }).Errors.Single().ErrorMessage);
    }

    [Fact]
    public void Reset_needs_a_token_and_a_strong_password()
    {
        var validator = new ResetPasswordRequestValidator();

        var result = validator.Validate(new ResetPasswordRequest { Token = " ", NewPassword = "weak" });

        Assert.Equal("This reset link is invalid or has expired", result.Errors.Single(e => e.PropertyName == "Token").ErrorMessage);
        Assert.Equal("Use at least 8 characters with letters and numbers", result.Errors.Single(e => e.PropertyName == "NewPassword").ErrorMessage);
        Assert.True(validator.Validate(new ResetPasswordRequest { Token = "abc", NewPassword = "Brand1New" }).IsValid);
    }

    [Fact]
    public void Field_names_in_errors_are_camel_case()
    {
        var ex = Assert.Throws<Domain.Exceptions.ValidationException>(
            () => new ResetPasswordRequestValidator().ThrowIfInvalid(new ResetPasswordRequest { Token = "", NewPassword = "x" }));

        Assert.Contains("token", ex.Fields.Keys);
        Assert.Contains("newPassword", ex.Fields.Keys);
    }
}

public class AccountEmailTests
{
    [Fact]
    public void Welcome_email_text()
    {
        var email = AccountEmails.Welcome("emma@example.com", "Emma Wilson", "Ceylon Roads", "Nuwan Dissanayake");

        Assert.Equal(EmailKind.Welcome, email.Kind);
        Assert.Equal("emma@example.com", email.To);
        Assert.Equal("Welcome to Ceylon Roads", email.Subject);
        Assert.Equal("Hi Emma Wilson,\n\nYour account is ready. You can now sign in to manage your bookings.\n\nNuwan Dissanayake", email.Body);
    }

    [Fact]
    public void Password_reset_email_contains_the_link_and_the_validity()
    {
        var email = AccountEmails.PasswordReset("emma@example.com", "Emma", "https://x.example", "abc", 60);

        Assert.Equal(EmailKind.PasswordReset, email.Kind);
        Assert.Equal("Reset your password", email.Subject);
        Assert.Contains("https://x.example/reset-password?token=abc", email.Body, StringComparison.Ordinal);
        Assert.Contains("60 minutes", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Password_reset_email_keeps_a_copy_for_the_log_without_the_token()
    {
        var email = AccountEmails.PasswordReset("emma@example.com", "Emma", "https://x.example", "super-secret-token", 60);

        Assert.Contains("super-secret-token", email.Body, StringComparison.Ordinal);
        Assert.NotNull(email.LogBody);
        Assert.DoesNotContain("super-secret-token", email.LogBody, StringComparison.Ordinal);
        Assert.Contains("https://x.example/reset-password?token=[REDACTED]", email.LogBody, StringComparison.Ordinal);
        Assert.Contains("60 minutes", email.LogBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Welcome_email_has_no_secret_so_it_is_logged_as_it_is()
        => Assert.Null(AccountEmails.Welcome("emma@example.com", "Emma", "Ceylon Roads", "Nuwan").LogBody);

    [Theory]
    [InlineData("http://localhost:5173", "http://localhost:5173/reset-password?token=t1")]
    [InlineData("https://ceylonroads.example/", "https://ceylonroads.example/reset-password?token=t1")]
    public void Reset_link_joins_the_base_address_without_a_double_slash(string baseUrl, string expected)
        => Assert.Equal(expected, AccountEmails.ResetLink(baseUrl, "t1"));

    [Fact]
    public void Reset_link_escapes_the_token()
        => Assert.Equal("https://x.example/reset-password?token=a%2Bb%26c", AccountEmails.ResetLink("https://x.example", "a+b&c"));
}
