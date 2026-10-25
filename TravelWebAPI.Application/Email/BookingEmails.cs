using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Email;

/// <summary>Texts of the emails sent for bookings. All are plain text and linked to the booking in the email log.</summary>
public static class BookingEmails
{
    /// <summary>To the customer when the booking is made: we have it, nothing has been charged.</summary>
    public static EmailMessage Received(Booking booking, string serviceTitle, string driverName) => new(
        EmailKind.BookingReceived,
        booking.Email,
        $"We received your booking request {booking.Reference}",
        $"Hi {booking.Name},\n\nThanks for your request for {serviceTitle} on {Date(booking)} at {Time(booking)}.\n"
        + $"{driverName} will confirm the price and details by WhatsApp or email soon. No payment has been taken.\n\nReference: {booking.Reference}",
        booking.Id);

    /// <summary>To the owner when a booking arrives.</summary>
    public static EmailMessage Notice(string ownerEmail, Booking booking, string serviceTitle) => new(
        EmailKind.BookingNotice,
        ownerEmail,
        $"New booking request {booking.Reference}",
        $"{booking.Name} ({booking.Email}, {booking.Whatsapp}) requested {serviceTitle} on {Date(booking)} at {Time(booking)} "
        + $"for {booking.Adults} adults, {booking.Children} children.",
        booking.Id);

    public static EmailMessage Confirmed(Booking booking, string serviceTitle, string driverName) => new(
        EmailKind.BookingConfirmed,
        booking.Email,
        $"Your booking {booking.Reference} is confirmed",
        $"Hi {booking.Name},\n\nGood news: your {serviceTitle} on {Date(booking)} at {Time(booking)} is confirmed."
        + (booking.PriceLkr is { } price ? $"\nAgreed price: {BookingMoney.Text(price)}." : string.Empty)
        + $"\n\nSee you soon,\n{driverName}",
        booking.Id);

    public static EmailMessage Cancelled(Booking booking, string driverName) => new(
        EmailKind.BookingCancelled,
        booking.Email,
        $"Your booking {booking.Reference} was cancelled",
        $"Hi {booking.Name},\n\nYour booking {booking.Reference} has been cancelled. If this was unexpected, just reply to this email.\n\n{driverName}",
        booking.Id);

    public static EmailMessage Completed(Booking booking, string driverName) => new(
        EmailKind.BookingCompleted,
        booking.Email,
        "Thank you for travelling with us",
        $"Hi {booking.Name},\n\nThank you for travelling with us! If you enjoyed the trip, a short review on our website would mean a lot.\n\n{driverName}",
        booking.Id);

    /// <summary>The admin's own message to the customer.</summary>
    public static EmailMessage Custom(Booking booking, string subject, string body)
        => new(EmailKind.Custom, booking.Email, subject, body, booking.Id);

    private static string Date(Booking booking) => BookingFormats.Date(booking.TravelDate);

    private static string Time(Booking booking) => BookingFormats.Time(booking.TravelTime);
}
