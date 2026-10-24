namespace TravelWebAPI.Domain.Common;

/// <summary>
/// Overrides the string an enum member has in the API and in the database.
/// Without it the member name is used in lowerCamelCase.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class WireValueAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}
