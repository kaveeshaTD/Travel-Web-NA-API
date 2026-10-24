using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TravelWebAPI.Domain.Common;

namespace TravelWebAPI.Infrastructure.Persistence;

internal static class ModelBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>App-generated version 7 GUID, with a database default as a safety net.</summary>
    public static PropertyBuilder<Guid> HasUuidKey(this PropertyBuilder<Guid> property)
        => property
            .HasValueGenerator<GuidV7ValueGenerator>()
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("gen_random_uuid()");

    /// <summary>Stores the enum as the API string (for example <c>password-reset</c>).</summary>
    public static PropertyBuilder<TEnum> HasWireConversion<TEnum>(this PropertyBuilder<TEnum> property, int maxLength = 30)
        where TEnum : struct, Enum
        => property
            .HasConversion(new ValueConverter<TEnum, string>(v => EnumWire.ToWire(v), s => EnumWire.FromWire<TEnum>(s)))
            .HasMaxLength(maxLength);

    /// <summary>Adds a CHECK constraint that only allows the wire strings of the enum (or the given subset).</summary>
    public static TableBuilder<TEntity> HasEnumCheck<TEntity, TEnum>(
        this TableBuilder<TEntity> table,
        string tableName,
        string column,
        params TEnum[] allowed)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var values = allowed.Length > 0 ? allowed.Select(v => EnumWire.ToWire(v)) : EnumWire.Values<TEnum>();
        var list = string.Join(", ", values.Order(StringComparer.Ordinal).Select(v => $"'{v}'"));

        table.HasCheckConstraint($"ck_{tableName}_{column}", $"{column} IN ({list})");
        return table;
    }

    /// <summary>Stores a list or object as <c>jsonb</c>.</summary>
    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> property)
        where T : class
    {
        var converter = new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            s => JsonSerializer.Deserialize<T>(s, JsonOptions)!);

        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);

        return property.HasConversion(converter, comparer).HasColumnType("jsonb");
    }
}
