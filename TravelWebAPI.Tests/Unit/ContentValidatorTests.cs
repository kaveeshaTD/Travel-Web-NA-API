using FluentValidation;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Content;

namespace TravelWebAPI.Tests.Unit;

/// <summary>The exact field messages the admin shows, one case per rule.</summary>
public sealed class ContentValidatorTests
{
    private static readonly string[] Images = ["img:sigiriya"];

    private static readonly PackageDraft Package = new(
        "Cultural Triangle", "Ancient kingdoms", 3, ["Climb Sigiriya"], ["culture"], 120_000m, "img:sigiriya", Images,
        [new ItineraryDayDraft("Arrival", "Meet at the airport")], ["Driver"], ["Tickets"], false, true, []);

    private static readonly DestinationDraft Destination = new(
        "Sigiriya", "Cultural Triangle", 7.95m, 80.76m, "A rock fortress in the jungle.", "Jan to Apr", "4 h", Images, true);

    private static readonly VehicleDraft Vehicle = new("car", "Prius", "A comfortable hybrid car.", "img:vehicle-car", 3, 2, true, 30m, 25_000m);

    private static readonly ServiceDraft Service = new("Airport transfer", "Pick-up at the airport.", ["Meet and greet"], "plane", "img:airport");

    private static readonly FaqDraft Faq = new("Do you provide child seats?", "Yes, on request.");

    private static readonly GalleryImageDraft Gallery = new("img:sigiriya", "The rock at sunrise", "Sunrise", 1.5m);

    private static Dictionary<string, string> Check(PackageDraft draft) => new PackageDraftValidator().Check(draft);

    private static Dictionary<string, string> Check(DestinationDraft draft) => new DestinationDraftValidator().Check(draft);

    private static Dictionary<string, string> Check(VehicleDraft draft) => new VehicleDraftValidator().Check(draft);

    private static Dictionary<string, string> Check(ServiceDraft draft) => new ServiceDraftValidator().Check(draft);

    private static Dictionary<string, string> Check(FaqDraft draft) => new FaqDraftValidator().Check(draft);

    private static Dictionary<string, string> Check(GalleryImageDraft draft) => new GalleryImageDraftValidator().Check(draft);

    [Fact]
    public void Valid_drafts_have_no_errors()
    {
        Assert.Empty(Check(Package));
        Assert.Empty(Check(Destination));
        Assert.Empty(Check(Vehicle));
        Assert.Empty(Check(Service));
        Assert.Empty(Check(Faq));
        Assert.Empty(Check(Gallery));
    }

    // ---------------------------------------------------------------- packages

    [Fact]
    public void Package_messages()
    {
        var fields = Check(Package with
        {
            Name = "A",
            DurationDays = 61,
            PriceLkr = 0,
            Image = string.Empty,
            Interests = ["culture", "space"],
            Itinerary = [new ItineraryDayDraft(string.Empty, "x")],
        });

        Assert.Equal("Enter a package name", fields["name"]);
        Assert.Equal("Duration must be 1 to 60 days", fields["durationDays"]);
        Assert.Equal("Enter a price greater than 0", fields["priceLkr"]);
        Assert.Equal("Choose a cover image", fields["image"]);
        Assert.Equal("Interests must be: culture, beach, wildlife, nature", fields["interests"]);
        Assert.Equal("Every itinerary day needs a title", fields["itinerary"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100_000_000)]
    [InlineData(10.123)]
    public void Package_price_must_be_a_storable_positive_amount(double price)
        => Assert.Equal("Enter a price greater than 0", Check(Package with { PriceLkr = (decimal)price })["priceLkr"]);

    [Fact]
    public void Package_price_with_two_decimals_is_fine()
        => Assert.Empty(Check(Package with { PriceLkr = 99_999_999.99m }));

    [Fact]
    public void Package_gallery_and_lists_are_checked()
    {
        var fields = Check(Package with
        {
            Gallery = ["javascript:alert(1)"],
            Highlights = [new string('x', 301)],
            DestinationIds = ["galle", "galle"],
        });

        Assert.Contains("gallery image", fields["gallery"]);
        Assert.Contains("highlights", fields["highlights"]);
        Assert.Equal("A destination can appear only once in the route", fields["destinationIds"]);
    }

    [Fact]
    public void Package_text_limits_give_messages_not_database_errors()
    {
        var fields = Check(Package with { Name = new string('n', 151), Tagline = new string('t', 251) });

        Assert.Equal("The package name is too long", fields["name"]);
        Assert.Equal("The tagline is too long", fields["tagline"]);
    }

    // ---------------------------------------------------------------- destinations

    [Fact]
    public void Destination_messages()
    {
        var fields = Check(Destination with
        {
            Name = "S",
            Region = string.Empty,
            Lat = 12,
            Lng = 70,
            Description = "short",
            Images = [],
        });

        Assert.Equal("Enter a destination name", fields["name"]);
        Assert.Equal("Enter a region", fields["region"]);
        Assert.Equal("Latitude must be between 5 and 10 (Sri Lanka)", fields["lat"]);
        Assert.Equal("Longitude must be between 79 and 82.5 (Sri Lanka)", fields["lng"]);
        Assert.Equal("Add a short description", fields["description"]);
        Assert.Equal("Add at least one image", fields["images"]);
    }

    [Theory]
    [InlineData(5, 79)]
    [InlineData(10, 82.5)]
    public void Destination_coordinate_limits_are_inclusive(double lat, double lng)
        => Assert.Empty(Check(Destination with { Lat = (decimal)lat, Lng = (decimal)lng }));

    [Fact]
    public void Destination_with_an_unsafe_image_is_rejected()
        => Assert.Contains("image", Check(Destination with { Images = ["data:image/png;base64,AA"] })["images"]);

    // ---------------------------------------------------------------- vehicles

    [Fact]
    public void Vehicle_messages()
    {
        var fields = Check(Vehicle with
        {
            Type = "bus",
            Name = "P",
            Image = string.Empty,
            Seats = 0,
            Luggage = -1,
            PricePerKmLkr = -1,
            PricePerDayLkr = 0,
        });

        Assert.Equal("Type must be one of: car, van, minicoach", fields["type"]);
        Assert.Equal("Enter a vehicle name", fields["name"]);
        Assert.Equal("Choose a photo", fields["image"]);
        Assert.Equal("Seats must be a whole number of 1 or more", fields["seats"]);
        Assert.Equal("Luggage must be a whole number of 0 or more", fields["luggage"]);
        Assert.Equal("Enter the price per km", fields["pricePerKmLkr"]);
        Assert.Equal("Enter the price per day", fields["pricePerDayLkr"]);
    }

    [Fact]
    public void Vehicle_price_per_km_may_be_zero_but_not_per_day()
    {
        var fields = Check(Vehicle with { PricePerKmLkr = 0, PricePerDayLkr = 0 });

        Assert.False(fields.ContainsKey("pricePerKmLkr"));
        Assert.True(fields.ContainsKey("pricePerDayLkr"));
    }

    // ---------------------------------------------------------------- services

    [Fact]
    public void Service_messages()
    {
        var fields = Check(Service with { Title = "A", Description = "short", Icon = "rocket", Image = string.Empty });

        Assert.Equal("Enter a service title", fields["title"]);
        Assert.Equal("Add a short description", fields["description"]);
        Assert.Equal("Icon must be one of: plane, sun, route, compass, car, users, pin, calendar, star, check", fields["icon"]);
        Assert.Equal("Choose an image", fields["image"]);
    }

    [Fact]
    public void Service_details_have_a_size_limit()
        => Assert.Contains("details", Check(Service with { Details = [.. Enumerable.Repeat("x", 51)] })["details"]);

    // ---------------------------------------------------------------- faqs and gallery

    [Fact]
    public void Faq_messages()
    {
        var fields = Check(new FaqDraft("Why", "No"));

        Assert.Equal("Enter the question", fields["question"]);
        Assert.Equal("Enter the answer", fields["answer"]);
    }

    [Fact]
    public void Gallery_messages()
    {
        var fields = Check(new GalleryImageDraft(string.Empty, "ab", string.Empty, 4m));

        Assert.Equal("Choose an image", fields["src"]);
        Assert.Equal("Describe the image for accessibility", fields["alt"]);
        Assert.Equal("Enter a caption", fields["caption"]);
        Assert.Equal("Aspect ratio must be between 0.3 and 3.5", fields["aspect"]);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(3.5)]
    public void Gallery_aspect_limits_are_inclusive(double aspect)
        => Assert.Empty(Check(Gallery with { Aspect = (decimal)aspect }));

    [Fact]
    public void Field_names_are_camel_case_paths()
    {
        var validator = new InlineValidator<Nested>();
        validator.RuleFor(x => x.Trust.Rating).Must(_ => false).WithMessage("bad");
        validator.RuleForEach(x => x.Regions).ChildRules(r => r.RuleFor(x => x.Months).Must(_ => false).WithMessage("worse"));

        var fields = validator.Check(new Nested(new Inner(1), [new Region(2)]));

        Assert.Equal("bad", fields["trust.rating"]);
        Assert.Equal("worse", fields["regions[0].months"]);
    }

    private sealed record Nested(Inner Trust, List<Region> Regions);

    private sealed record Inner(int Rating);

    private sealed record Region(int Months);
}
