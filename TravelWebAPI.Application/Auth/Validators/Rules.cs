using System.Text.RegularExpressions;
using FluentValidation;

namespace TravelWebAPI.Application.Auth.Validators;

/// <summary>The exact messages the website already shows, and the shared rules behind them.</summary>
public static partial class Rules
{
    public const string NameMessage = "Enter your full name";
    public const string EmailMessage = "Enter a valid email address";
    public const string PasswordMessage = "Use at least 8 characters with letters and numbers";
    public const string CurrentPasswordMessage = "Current password is incorrect";
    public const string ResetTokenMessage = "This reset link is invalid or has expired";

    public const int MaxName = 120;
    public const int MaxEmail = 254;
    public const int MaxPhone = 40;
    public const int MaxCountry = 80;

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsEmail(string? email)
        => !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= MaxEmail && EmailPattern().IsMatch(email.Trim());

    public static bool IsStrongPassword(string? password)
        => !string.IsNullOrEmpty(password) && password.Length >= 8 && password.Any(char.IsLetter) && password.Any(char.IsDigit);

    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(IsEmail).WithMessage(EmailMessage);

    public static IRuleBuilderOptions<T, string?> StrongPassword<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(IsStrongPassword).WithMessage(PasswordMessage);

    public static IRuleBuilderOptions<T, string?> FullName<T>(this IRuleBuilder<T, string?> rule)
        => rule.Must(name => (name ?? string.Empty).Trim().Length >= 2).WithMessage(NameMessage);

    public static IRuleBuilderOptions<T, string?> MaxTrimmedLength<T>(this IRuleBuilder<T, string?> rule, int max, string message)
        => rule.Must(value => (value ?? string.Empty).Trim().Length <= max).WithMessage(message);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
