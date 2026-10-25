using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Content;

/// <summary>The body of <c>POST /api/{resource}/reorder</c>.</summary>
public sealed record ReorderRequest(IReadOnlyList<string>? Ids = null);

public static class Ordering
{
    public const string IdsMessage = "ids must be a list of record ids";

    /// <summary>
    /// Puts the listed ids first, in the given order, and keeps every record that is not listed after them in its current order.
    /// A missing list, a repeated id or an id that does not exist is a validation error and changes nothing.
    /// </summary>
    public static List<T> Reorder<T>(IReadOnlyList<T> current, ReorderRequest request, Func<T, string> idOf)
    {
        if (request.Ids is null)
        {
            throw new ValidationException("ids", IdsMessage);
        }

        var byId = current.ToDictionary(idOf, StringComparer.Ordinal);

        var repeated = request.Ids.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repeated.Count > 0)
        {
            throw new ValidationException("ids", $"Repeated ids: {string.Join(", ", repeated)}");
        }

        var unknown = request.Ids.Where(id => !byId.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationException("ids", $"Unknown ids: {string.Join(", ", unknown)}");
        }

        var listed = request.Ids.Select(id => byId[id]).ToList();
        var listedIds = request.Ids.ToHashSet(StringComparer.Ordinal);
        listed.AddRange(current.Where(item => !listedIds.Contains(idOf(item))));
        return listed;
    }
}
