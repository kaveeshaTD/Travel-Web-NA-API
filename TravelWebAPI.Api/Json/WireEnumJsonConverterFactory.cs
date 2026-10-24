using System.Text.Json;
using System.Text.Json.Serialization;
using TravelWebAPI.Domain.Common;

namespace TravelWebAPI.Api.Json;

/// <summary>Reads and writes every enum as its API string (for example <c>password-reset</c>).</summary>
public sealed class WireEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(WireEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class WireEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (EnumWire.TryFromWire<TEnum>(text, out var value))
            {
                return value;
            }

            throw new JsonException($"'{text}' is not a valid {typeof(TEnum).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
            => writer.WriteStringValue(EnumWire.ToWire(value));
    }
}
