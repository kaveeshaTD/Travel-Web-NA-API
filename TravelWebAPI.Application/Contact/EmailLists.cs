using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Contact;

/// <summary>The email log request: the common paging, sorting and search plus the status, kind and booking filters.</summary>
public sealed class EmailListRequest : ListQueryRequest
{
    /// <summary>Only emails with this delivery status: queued, sent or failed.</summary>
    public string? Status { get; set; }

    /// <summary>Only emails of this kind, for example <c>booking-received</c> or <c>reply</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>Only the emails linked to this booking (its id).</summary>
    public string? Booking { get; set; }
}

/// <summary>The filters of the email log after they have been checked. A missing value means "do not filter".</summary>
public sealed record EmailListFilter(EmailDeliveryStatus? Status, EmailKind? Kind, Guid? BookingId);

public static class EmailLists
{
    public const string BookingMessage = "Booking must be a valid id";

    /// <summary>Sort by: createdAt (default, newest first), to, subject, kind, status. Facets: status, kind.</summary>
    public static readonly ListDefinition Definition = new(
        ["createdAt", "to", "subject", "kind", "status"],
        "-createdAt",
        ["status", "kind"]);

    public static (ListQuery Query, EmailListFilter Filter) Parse(EmailListRequest request)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, Definition, errors);
        var filter = new EmailListFilter(
            ListFilters.Wire<EmailDeliveryStatus>(request.Status, "status", ListFilters.OneOf<EmailDeliveryStatus>("Status"), errors),
            ListFilters.Wire<EmailKind>(request.Kind, "kind", ListFilters.OneOf<EmailKind>("Kind"), errors),
            ListFilters.Id(request.Booking, "booking", BookingMessage, errors));

        errors.ThrowIfAny();
        return (query, filter);
    }
}
