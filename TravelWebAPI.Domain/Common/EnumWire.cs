using System.Collections.Frozen;
using System.Reflection;

namespace TravelWebAPI.Domain.Common;

/// <summary>
/// Converts enum members to and from the strings used in the API contract and the database
/// (for example <c>EmailKind.PasswordReset</c> to <c>password-reset</c>).
/// </summary>
public static class EnumWire
{
    public static string ToWire<TEnum>(TEnum value) where TEnum : struct, Enum
        => Map<TEnum>.ToWire[value];

    public static bool TryFromWire<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        if (text is not null && Map<TEnum>.FromWire.TryGetValue(text, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static TEnum FromWire<TEnum>(string text) where TEnum : struct, Enum
        => TryFromWire(text, out TEnum value)
            ? value
            : throw new ArgumentException($"'{text}' is not a valid {typeof(TEnum).Name}.", nameof(text));

    public static IReadOnlyCollection<string> Values<TEnum>() where TEnum : struct, Enum
        => Map<TEnum>.FromWire.Keys;

    private static class Map<TEnum> where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> ToWire;
        public static readonly FrozenDictionary<string, TEnum> FromWire;

        static Map()
        {
            var toWire = new Dictionary<TEnum, string>();
            var fromWire = new Dictionary<string, TEnum>(StringComparer.Ordinal);

            foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var member = (TEnum)field.GetValue(null)!;
                var text = field.GetCustomAttribute<WireValueAttribute>()?.Value ?? CamelCase(field.Name);
                toWire[member] = text;
                fromWire[text] = member;
            }

            ToWire = toWire.ToFrozenDictionary();
            FromWire = fromWire.ToFrozenDictionary(StringComparer.Ordinal);
        }

        private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
    }
}
