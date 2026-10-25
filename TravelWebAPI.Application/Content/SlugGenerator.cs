using System.Globalization;
using System.Text;

namespace TravelWebAPI.Application.Content;

/// <summary>Builds the readable, URL-safe ids (slugs) of content records.</summary>
public static class SlugGenerator
{
    public const int MaxLength = 100;
    public const string Fallback = "item";

    /// <summary>Not allowed as an id, because <c>POST /api/{resource}/reorder</c> uses that path.</summary>
    public const string Reserved = "reorder";

    /// <summary>Longest suffix (<c>-9999999</c>) the generator can append, which sets how much of the base is always kept.</summary>
    private const int SuffixRoom = 8;

    private const int MaxAttempts = 10_000;

    /// <summary>Lower-case ASCII letters and digits separated by single dashes, at most <see cref="MaxLength"/> characters.</summary>
    public static string Slugify(string? text)
    {
        var builder = new StringBuilder();
        var pendingDash = false;

        foreach (var ch in (text ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or >= 'A' and <= 'Z')
            {
                if (pendingDash && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingDash = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = builder.ToString();
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        return slug.Length == 0 ? Fallback : slug;
    }

    /// <summary>
    /// The start every candidate id for <paramref name="text"/> shares, so a repository can load the ids that might clash
    /// with one query.
    /// </summary>
    public static string LookupPrefix(string? text)
    {
        var slug = Slugify(text);
        return slug.Length <= MaxLength - SuffixRoom ? slug : slug[..(MaxLength - SuffixRoom)];
    }

    /// <summary>The first id built from <paramref name="text"/> that is not in <paramref name="taken"/>: <c>name</c>, <c>name-2</c>, <c>name-3</c>…</summary>
    public static string Unique(string? text, IReadOnlySet<string> taken)
    {
        var slug = Slugify(text);

        if (slug != Reserved && !taken.Contains(slug))
        {
            return slug;
        }

        for (var number = 2; number < MaxAttempts; number++)
        {
            var suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
            var stem = slug.Length + suffix.Length > MaxLength ? slug[..(MaxLength - suffix.Length)].TrimEnd('-') : slug;
            var candidate = stem + suffix;

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        // Practically unreachable; a random tail keeps the call total.
        return $"{slug[..Math.Min(slug.Length, MaxLength - 9)]}-{Guid.NewGuid().ToString("N")[..8]}";
    }
}
