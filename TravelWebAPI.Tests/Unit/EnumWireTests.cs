using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Unit;

public class EnumWireTests
{
    [Theory]
    [InlineData(EmailKind.Welcome, "welcome")]
    [InlineData(EmailKind.PasswordReset, "password-reset")]
    [InlineData(EmailKind.BookingReceived, "booking-received")]
    [InlineData(EmailKind.BookingNotice, "booking-notice")]
    [InlineData(EmailKind.BookingConfirmed, "booking-confirmed")]
    [InlineData(EmailKind.BookingCancelled, "booking-cancelled")]
    [InlineData(EmailKind.BookingCompleted, "booking-completed")]
    [InlineData(EmailKind.ContactAck, "contact-ack")]
    [InlineData(EmailKind.Reply, "reply")]
    [InlineData(EmailKind.Custom, "custom")]
    public void Email_kinds_use_the_contract_strings(EmailKind kind, string expected)
    {
        Assert.Equal(expected, EnumWire.ToWire(kind));
        Assert.Equal(kind, EnumWire.FromWire<EmailKind>(expected));
    }

    [Fact]
    public void Payment_method_and_currency_keep_their_contract_spelling()
    {
        Assert.Equal("bank_transfer", EnumWire.ToWire(PaymentMethod.BankTransfer));
        Assert.Equal("LKR", EnumWire.ToWire(CurrencyCode.Lkr));
        Assert.Equal("USD", EnumWire.ToWire(CurrencyCode.Usd));
    }

    [Fact]
    public void Every_member_of_every_enum_round_trips()
    {
        AssertRoundTrip<UserRole>();
        AssertRoundTrip<UserStatus>();
        AssertRoundTrip<VehicleType>();
        AssertRoundTrip<InterestType>();
        AssertRoundTrip<PackageItemKind>();
        AssertRoundTrip<ReviewStatus>();
        AssertRoundTrip<BookingStatus>();
        AssertRoundTrip<CurrencyCode>();
        AssertRoundTrip<PaymentMethod>();
        AssertRoundTrip<PaymentStatus>();
        AssertRoundTrip<MessageStatus>();
        AssertRoundTrip<EmailKind>();
        AssertRoundTrip<EmailDeliveryStatus>();
    }

    [Fact]
    public void Unknown_text_is_rejected()
    {
        Assert.False(EnumWire.TryFromWire<BookingStatus>("nope", out _));
        Assert.False(EnumWire.TryFromWire<BookingStatus>(null, out _));
        Assert.Throws<ArgumentException>(() => EnumWire.FromWire<BookingStatus>("nope"));
    }

    private static void AssertRoundTrip<TEnum>() where TEnum : struct, Enum
    {
        foreach (var member in Enum.GetValues<TEnum>())
        {
            var text = EnumWire.ToWire(member);
            Assert.Equal(member, EnumWire.FromWire<TEnum>(text));
        }

        Assert.Equal(Enum.GetValues<TEnum>().Length, EnumWire.Values<TEnum>().Count);
    }
}
