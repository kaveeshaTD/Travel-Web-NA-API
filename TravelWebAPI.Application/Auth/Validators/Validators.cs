using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Name).FullName();
        RuleFor(x => x.Name).MaxTrimmedLength(Rules.MaxName, "Name is too long");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.Phone).MaxTrimmedLength(Rules.MaxPhone, "Phone number is too long");
        RuleFor(x => x.Country).MaxTrimmedLength(Rules.MaxCountry, "Country is too long");
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        When(x => x.Name is not null, () =>
        {
            RuleFor(x => x.Name).FullName();
            RuleFor(x => x.Name).MaxTrimmedLength(Rules.MaxName, "Name is too long");
        });

        RuleFor(x => x.Phone).MaxTrimmedLength(Rules.MaxPhone, "Phone number is too long");
        RuleFor(x => x.Country).MaxTrimmedLength(Rules.MaxCountry, "Country is too long");
        When(x => x.NewPassword is not null, () => RuleFor(x => x.NewPassword).StrongPassword());
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).ValidEmail();
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage(Rules.ResetTokenMessage);
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public static class ValidationExtensions
{
    /// <summary>Throws a <see cref="Domain.Exceptions.ValidationException"/> with the first message of each invalid field (camelCase names).</summary>
    public static void ThrowIfInvalid<T>(this IValidator<T> validator, T instance)
    {
        var result = validator.Validate(instance);
        if (result.IsValid)
        {
            return;
        }

        throw new Domain.Exceptions.ValidationException(ToFields(result));
    }

    private static Dictionary<string, string> ToFields(ValidationResult result)
        => result.Errors
            .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.First().ErrorMessage);
}
