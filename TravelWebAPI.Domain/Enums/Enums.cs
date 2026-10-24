using TravelWebAPI.Domain.Common;

namespace TravelWebAPI.Domain.Enums;

public enum UserRole
{
    Customer,
    Admin,
}

public enum UserStatus
{
    Active,
    Inactive,
}

public enum VehicleType
{
    Car,
    Van,
    Minicoach,
}

public enum InterestType
{
    Culture,
    Beach,
    Wildlife,
    Nature,
}

public enum PackageItemKind
{
    Highlight,
    Included,
    Excluded,
}

public enum ReviewStatus
{
    Pending,
    Published,
    Rejected,
}

public enum BookingStatus
{
    Pending,
    Confirmed,
    Completed,
    Cancelled,
}

public enum CurrencyCode
{
    [WireValue("USD")] Usd,
    [WireValue("EUR")] Eur,
    [WireValue("GBP")] Gbp,
    [WireValue("LKR")] Lkr,
}

public enum PaymentMethod
{
    Cash,
    [WireValue("bank_transfer")] BankTransfer,
    Card,
    Other,
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Refunded,
}

public enum MessageStatus
{
    New,
    Read,
    Replied,
}

public enum EmailKind
{
    Welcome,
    [WireValue("password-reset")] PasswordReset,
    [WireValue("booking-received")] BookingReceived,
    [WireValue("booking-notice")] BookingNotice,
    [WireValue("booking-confirmed")] BookingConfirmed,
    [WireValue("booking-cancelled")] BookingCancelled,
    [WireValue("booking-completed")] BookingCompleted,
    [WireValue("contact-ack")] ContactAck,
    Reply,
    Custom,
}

public enum EmailDeliveryStatus
{
    Queued,
    Sent,
    Failed,
}
