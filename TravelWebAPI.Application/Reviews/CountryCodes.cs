using System.Collections.Frozen;

namespace TravelWebAPI.Application.Reviews;

/// <summary>Turns the country a reviewer typed into its ISO two-letter code, for the flag shown next to a review.</summary>
public static class CountryCodes
{
    private static readonly FrozenDictionary<string, string> ByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["united kingdom"] = "GB", ["uk"] = "GB", ["great britain"] = "GB", ["england"] = "GB", ["scotland"] = "GB", ["wales"] = "GB",
        ["united states"] = "US", ["united states of america"] = "US", ["usa"] = "US", ["us"] = "US", ["america"] = "US",
        ["germany"] = "DE", ["france"] = "FR", ["australia"] = "AU", ["india"] = "IN", ["russia"] = "RU", ["ireland"] = "IE",
        ["canada"] = "CA", ["netherlands"] = "NL", ["the netherlands"] = "NL", ["holland"] = "NL", ["italy"] = "IT", ["spain"] = "ES",
        ["sweden"] = "SE", ["norway"] = "NO", ["denmark"] = "DK", ["finland"] = "FI", ["switzerland"] = "CH", ["austria"] = "AT",
        ["belgium"] = "BE", ["new zealand"] = "NZ", ["japan"] = "JP", ["china"] = "CN", ["singapore"] = "SG", ["malaysia"] = "MY",
        ["sri lanka"] = "LK", ["south africa"] = "ZA", ["poland"] = "PL", ["ukraine"] = "UA", ["israel"] = "IL",
        ["united arab emirates"] = "AE", ["uae"] = "AE", ["maldives"] = "MV", ["brazil"] = "BR", ["mexico"] = "MX",
        ["portugal"] = "PT", ["greece"] = "GR", ["czech republic"] = "CZ", ["czechia"] = "CZ", ["hungary"] = "HU", ["romania"] = "RO",
        ["south korea"] = "KR", ["korea"] = "KR", ["thailand"] = "TH", ["indonesia"] = "ID", ["philippines"] = "PH", ["vietnam"] = "VN",
        ["pakistan"] = "PK", ["bangladesh"] = "BD", ["nepal"] = "NP", ["turkey"] = "TR", ["egypt"] = "EG", ["saudi arabia"] = "SA",
        ["qatar"] = "QA", ["kuwait"] = "KW", ["oman"] = "OM", ["bahrain"] = "BH", ["argentina"] = "AR", ["chile"] = "CL",
        ["colombia"] = "CO", ["peru"] = "PE", ["iceland"] = "IS", ["luxembourg"] = "LU", ["estonia"] = "EE", ["latvia"] = "LV",
        ["lithuania"] = "LT", ["slovakia"] = "SK", ["slovenia"] = "SI", ["croatia"] = "HR", ["serbia"] = "RS", ["bulgaria"] = "BG",
        ["hong kong"] = "HK", ["taiwan"] = "TW", ["kenya"] = "KE", ["nigeria"] = "NG", ["morocco"] = "MA",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The two-letter code for a known country name; a value that is already two letters is upper-cased; otherwise empty.</summary>
    public static string Lookup(string? country)
    {
        var text = (country ?? string.Empty).Trim();

        // Names first, so "UK" (not an ISO code) becomes GB before it can be mistaken for a two-letter code.
        if (ByName.TryGetValue(text, out var code))
        {
            return code;
        }

        return text.Length == 2 && text.All(char.IsAsciiLetter) ? text.ToUpperInvariant() : string.Empty;
    }
}
