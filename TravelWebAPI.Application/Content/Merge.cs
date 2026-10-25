namespace TravelWebAPI.Application.Content;

/// <summary>
/// Partial updates: a value that was not sent (null) keeps the stored one, text is trimmed, and a list that was sent
/// replaces the stored list completely with blank lines removed.
/// </summary>
internal static class Merge
{
    public static string Text(string? sent, string stored) => sent is null ? stored : sent.Trim();

    public static IReadOnlyList<string> Lines(IReadOnlyList<string>? sent, IReadOnlyList<string> stored)
        => sent is null
            ? stored
            : [.. sent.Select(line => (line ?? string.Empty).Trim()).Where(line => line.Length > 0)];
}
