using System.Text.RegularExpressions;
using FluentValidation;

namespace TravelWebAPI.Application.Content;

/// <summary>What counts as an image value in content: a web link, an uploaded file path or a website photo key.</summary>
public static partial class ImageReference
{
    public const int MaxLength = 500;
    public const string UploadsPrefix = "/uploads/";
    public const string PhotoPrefix = "img:";

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || value != value.Trim())
        {
            return false;
        }

        if (value.StartsWith(UploadsPrefix, StringComparison.Ordinal))
        {
            return UploadName().IsMatch(value[UploadsPrefix.Length..]);
        }

        if (value.StartsWith(PhotoPrefix, StringComparison.Ordinal))
        {
            return PhotoKey().IsMatch(value[PhotoPrefix.Length..]);
        }

        return value.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrEmpty(uri.Host);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$")]
    private static partial Regex UploadName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$")]
    private static partial Regex PhotoKey();
}

/// <summary>Shared validation rules of the content resources. Values are already trimmed when they are checked.</summary>
public static class ContentRules
{
    public const decimal MaxMoney = 99_999_999.99m;
    public const int MaxListItems = 50;
    public const int MaxLine = 300;

    public static IRuleBuilderOptions<T, string?> MinLength<T>(this IRuleBuilder<T, string?> rule, int min, string message)
        => rule.Must(value => (value ?? string.Empty).Length >= min).WithMessage(message);

    public static IRuleBuilderOptions<T, string?> MaxLength<T>(this IRuleBuilder<T, string?> rule, int max, string message)
        => rule.Must(value => (value ?? string.Empty).Length <= max).WithMessage(message);

    /// <summary>An image value: an empty or unsafe value gives <paramref name="message"/>.</summary>
    public static IRuleBuilderOptions<T, string?> Image<T>(this IRuleBuilder<T, string?> rule, string message)
        => rule.Must(ImageReference.IsValid).WithMessage(message);

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> Images<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule, string message)
        => rule.Must(list => list.Count <= MaxListItems && list.All(ImageReference.IsValid)).WithMessage(message);

    /// <summary>A list of short lines (highlights, details…): not too many and none too long.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<string>> Lines<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule, string message)
        => rule.Must(list => list.Count <= MaxListItems && list.All(line => line.Length <= MaxLine)).WithMessage(message);

    /// <summary>Money in LKR: not negative, at most two decimals and small enough for the database column.</summary>
    public static bool IsMoney(decimal? value)
        => value is >= 0 and <= MaxMoney && decimal.Round(value.Value, 2) == value.Value;
}
