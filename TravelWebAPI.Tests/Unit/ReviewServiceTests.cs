using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class CountryCodeTests
{
    [Theory]
    [InlineData("United Kingdom", "GB")]
    [InlineData("  united kingdom ", "GB")]
    [InlineData("UK", "GB")]
    [InlineData("USA", "US")]
    [InlineData("Germany", "DE")]
    [InlineData("Sri Lanka", "LK")]
    [InlineData("gb", "GB")]
    [InlineData("De", "DE")]
    public void Known_names_and_two_letter_codes_give_a_code(string country, string expected)
        => Assert.Equal(expected, CountryCodes.Lookup(country));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Narnia")]
    [InlineData("1a")]
    [InlineData("ABC")]
    public void Anything_else_gives_an_empty_code(string? country)
        => Assert.Equal(string.Empty, CountryCodes.Lookup(country));

    [Fact]
    public void A_reference_is_the_prefix_and_the_last_eight_hex_digits_in_upper_case()
    {
        var id = Guid.Parse("01234567-89ab-7cde-8f01-23456789abcd");

        Assert.Equal("REV-6789ABCD", References.For("REV", id));
        Assert.Matches("^MSG-[0-9A-F]{8}$", References.For("MSG", Guid.CreateVersion7()));
    }
}

public sealed class ReviewServiceTests
{
    private static readonly Guid CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCurrentUser _user = new();
    private readonly FakeReviews _reviews = new();
    private readonly ReviewService _service;

    public ReviewServiceTests()
        => _service = new ReviewService(_reviews, _reviews, _clock, _user, new ReviewSubmissionDraftValidator(), new ReviewDraftValidator(_clock));

    private static ReviewSubmission Submission() => new("Emma Wilson", "United Kingdom", 5, "Cultural", "Wonderful trip, our driver was excellent.");

    private static Review Stored(ReviewStatus status = ReviewStatus.Published, string name = "Old Name") => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Country = "Germany",
        CountryCode = "DE",
        Rating = 4,
        TripType = "Beach",
        Comment = "A really nice time on the south coast.",
        ReviewDate = new DateOnly(2026, 9, 1),
        Status = status,
    };

    private static ValidationException Invalid(Func<Task> action)
        => Assert.Throws<ValidationException>(() => action().GetAwaiter().GetResult());

    // ---------------------------------------------------------------- submitting

    [Fact]
    public async Task A_guest_review_is_saved_pending_with_todays_date_and_no_author()
    {
        var receipt = await _service.SubmitAsync(Submission());

        var saved = Assert.Single(_reviews.Items);
        Assert.Matches("^REV-[0-9A-F]{8}$", receipt.Reference);
        Assert.EndsWith(saved.Id.ToString("N")[^8..].ToUpperInvariant(), receipt.Reference, StringComparison.Ordinal);
        Assert.Equal(ReviewStatus.Pending, saved.Status);
        Assert.Equal(new DateOnly(2026, 10, 25), saved.ReviewDate);
        Assert.Null(saved.UserId);
        Assert.Equal("GB", saved.CountryCode);
        Assert.Equal(5, saved.Rating);
        Assert.Equal(1, _reviews.Saves);
    }

    [Fact]
    public async Task A_signed_in_customer_is_linked_as_the_author()
    {
        _user.UserId = CustomerId;

        await _service.SubmitAsync(Submission());

        Assert.Equal(CustomerId, _reviews.Items.Single().UserId);
    }

    [Fact]
    public async Task Submitted_text_is_trimmed_and_an_unknown_country_has_an_empty_code()
    {
        await _service.SubmitAsync(Submission() with { Name = "  Emma  ", Country = " Narnia ", Comment = "   A long enough comment here.  " });

        var saved = _reviews.Items.Single();
        Assert.Equal("Emma", saved.Name);
        Assert.Equal("Narnia", saved.Country);
        Assert.Equal(string.Empty, saved.CountryCode);
        Assert.Equal("A long enough comment here.", saved.Comment);
    }

    [Fact]
    public async Task Every_submission_message_is_the_one_in_the_contract()
    {
        var error = Invalid(() => _service.SubmitAsync(new ReviewSubmission("E", "U", 9, " ", "short")));

        Assert.Equal("Enter your name", error.Fields["name"]);
        Assert.Equal("Enter your country", error.Fields["country"]);
        Assert.Equal("Choose a rating from 1 to 5", error.Fields["rating"]);
        Assert.Equal("Choose your trip type", error.Fields["tripType"]);
        Assert.Equal("Please write at least 10 characters", error.Fields["comment"]);
        Assert.Empty(_reviews.Items);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(6.0)]
    [InlineData(4.5)]
    [InlineData(-1.0)]
    [InlineData(1.0001)]
    public void A_rating_must_be_a_whole_number_from_one_to_five(double rating)
    {
        var error = Invalid(() => _service.SubmitAsync(Submission() with { Rating = (decimal)rating }));

        Assert.Equal("Choose a rating from 1 to 5", error.Fields["rating"]);
    }

    [Fact]
    public void A_missing_rating_is_rejected_with_the_same_message()
    {
        var error = Invalid(() => _service.SubmitAsync(Submission() with { Rating = null }));

        Assert.Equal("Choose a rating from 1 to 5", error.Fields["rating"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Ratings_one_to_five_are_accepted(int rating)
    {
        await _service.SubmitAsync(Submission() with { Rating = rating });

        Assert.Equal(rating, _reviews.Items.Single().Rating);
    }

    [Fact]
    public async Task Over_long_text_is_a_field_message_not_a_server_error()
    {
        var error = Invalid(() => _service.SubmitAsync(Submission() with { Name = new string('n', 121), Comment = new string('c', 2001) }));

        Assert.Equal("Your name is too long", error.Fields["name"]);
        Assert.Equal("The comment is too long", error.Fields["comment"]);
    }

    // ---------------------------------------------------------------- lists

    [Fact]
    public async Task The_public_list_has_no_author_and_the_admin_list_has_one()
    {
        _reviews.Items.Add(Stored());
        _reviews.Items[0].UserId = CustomerId;

        var publicList = (await _service.ListPublishedAsync(new ReviewListRequest())).Items;
        var adminList = (await _service.ListAllAsync(new ReviewListRequest())).Items;

        Assert.Equal(CustomerId, adminList.Single().UserId);
        Assert.DoesNotContain("UserId", typeof(ReviewDto).GetProperties().Select(p => p.Name));
        Assert.Equal("2026-09-01", publicList.Single().Date);
        Assert.Equal("DE", publicList.Single().CountryCode);
    }

    [Fact]
    public async Task The_public_list_only_asks_for_published_reviews()
    {
        await _service.ListPublishedAsync(new ReviewListRequest { Status = "pending" });
        Assert.True(_reviews.LastPublishedOnly);
        Assert.Null(_reviews.LastFilter!.Status); // the public cannot ask for other statuses

        await _service.ListAllAsync(new ReviewListRequest { Status = "pending" });
        Assert.False(_reviews.LastPublishedOnly);
        Assert.Equal(ReviewStatus.Pending, _reviews.LastFilter!.Status);
    }

    // ---------------------------------------------------------------- admin add

    [Fact]
    public async Task An_admin_added_review_is_published_with_todays_date_by_default()
    {
        var added = await _service.AddAsync(new ReviewRequest(Name: "Hans Muller", Country: "Germany", Rating: 5, TripType: "Wildlife", Comment: "Saw elephants and leopards!"));

        Assert.Equal("published", added.Status);
        Assert.Equal("2026-10-25", added.Date);
        Assert.Equal("DE", added.CountryCode);
        Assert.Null(added.UserId);
        Assert.Equal(string.Empty, added.Photo);
        Assert.Single(_reviews.Items);
    }

    [Fact]
    public async Task An_admin_can_set_status_date_photo_and_code()
    {
        var added = await _service.AddAsync(new ReviewRequest(
            Name: "Hans", Country: "Germany", CountryCode: "at", Rating: 4, Photo: "/uploads/abc.jpg", TripType: "x",
            Comment: "Saw elephants and leopards!", Date: "2026-01-02", Status: "Pending"));

        Assert.Equal("pending", added.Status);
        Assert.Equal("2026-01-02", added.Date);
        Assert.Equal("AT", added.CountryCode);
        Assert.Equal("/uploads/abc.jpg", added.Photo);
    }

    [Fact]
    public async Task Admin_messages_are_the_ones_in_the_contract()
    {
        var error = Invalid(() => _service.AddAsync(new ReviewRequest(
            Name: "H", Rating: 9, Comment: "short", Status: "archived", Date: "not a date", Photo: "javascript:alert(1)", CountryCode: "GBR")));

        Assert.Equal("Enter the reviewer name", error.Fields["name"]);
        Assert.Equal("Rating must be 1 to 5", error.Fields["rating"]);
        Assert.Equal("Comment is too short", error.Fields["comment"]);
        Assert.Equal("Status must be one of: pending, published, rejected", error.Fields["status"]);
        Assert.Equal("Enter a valid date that is not in the future", error.Fields["date"]);
        Assert.Contains("Photo", error.Fields["photo"], StringComparison.Ordinal);
        Assert.Equal("Country code must be two letters", error.Fields["countryCode"]);
    }

    [Theory]
    [InlineData("2026-10-26")]
    [InlineData("2099-01-01")]
    [InlineData("2026-13-01")]
    [InlineData("25/10/2026")]
    [InlineData("")]
    public async Task A_date_in_the_future_or_not_a_date_is_rejected(string date)
    {
        var error = Invalid(() => _service.AddAsync(new ReviewRequest(Name: "Hans", Rating: 5, Comment: "A long enough comment.", Date: date)));

        Assert.Equal(ReviewDraftValidator.DateMessage, error.Fields["date"]);
    }

    [Fact]
    public async Task Todays_date_is_allowed()
    {
        var added = await _service.AddAsync(new ReviewRequest(Name: "Hans", Rating: 5, Comment: "A long enough comment.", Date: "2026-10-25"));

        Assert.Equal("2026-10-25", added.Date);
    }

    // ---------------------------------------------------------------- admin edit

    [Fact]
    public async Task Publishing_changes_only_the_status()
    {
        var review = Stored(ReviewStatus.Pending);
        _reviews.Items.Add(review);

        var updated = await _service.UpdateAsync(review.Id, new ReviewRequest(Status: "published"));

        Assert.Equal("published", updated.Status);
        Assert.Equal("Old Name", updated.Name);
        Assert.Equal("A really nice time on the south coast.", updated.Comment);
        Assert.Equal("2026-09-01", updated.Date);
        Assert.Equal("DE", updated.CountryCode);
        Assert.Equal(4, updated.Rating);
    }

    [Fact]
    public async Task A_new_country_gets_its_own_code_unless_a_code_is_sent()
    {
        var review = Stored();
        _reviews.Items.Add(review);

        var byName = await _service.UpdateAsync(review.Id, new ReviewRequest(Country: "France"));
        var byCode = await _service.UpdateAsync(review.Id, new ReviewRequest(Country: "Spain", CountryCode: "ca"));

        Assert.Equal("FR", byName.CountryCode);
        Assert.Equal("CA", byCode.CountryCode);
    }

    [Fact]
    public async Task An_invalid_merged_review_changes_nothing()
    {
        var review = Stored();
        _reviews.Items.Add(review);

        var error = Invalid(() => _service.UpdateAsync(review.Id, new ReviewRequest(Comment: "no", Rating: 7)));

        Assert.True(error.Fields.ContainsKey("comment") && error.Fields.ContainsKey("rating"));
        Assert.Equal("A really nice time on the south coast.", review.Comment);
        Assert.Equal(4, review.Rating);
        Assert.Equal(0, _reviews.Saves);
    }

    [Fact]
    public async Task A_stored_review_with_a_blank_padded_code_is_returned_trimmed()
    {
        var review = Stored();
        review.CountryCode = "GB";
        review.Photo = null;
        _reviews.Items.Add(review);

        var updated = await _service.UpdateAsync(review.Id, new ReviewRequest(TripType: "Hill country"));

        Assert.Equal("GB", updated.CountryCode);
        Assert.Equal(string.Empty, updated.Photo);
        Assert.Null(review.Photo);
    }

    [Fact]
    public async Task Editing_or_deleting_an_unknown_review_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(Guid.NewGuid(), new ReviewRequest()));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_removes_the_review()
    {
        var review = Stored();
        _reviews.Items.Add(review);

        await _service.DeleteAsync(review.Id);

        Assert.Empty(_reviews.Items);
        Assert.Equal(1, _reviews.Saves);
    }

    // ---------------------------------------------------------------- fake

    private sealed class FakeReviews : IReviewRepository, IUnitOfWork
    {
        public List<Review> Items { get; } = [];

        public int Saves { get; private set; }

        public bool LastPublishedOnly { get; private set; }

        public ReviewListFilter? LastFilter { get; private set; }

        /// <summary>A plain in-memory page; the real filtering, sorting and searching are tested against PostgreSQL.</summary>
        public Task<PagedResult<Review>> PageAsync(ReviewListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            LastPublishedOnly = !filter.IncludeEveryStatus;
            var rows = Items.Where(r => filter.IncludeEveryStatus || r.Status == ReviewStatus.Published).ToList();
            return Task.FromResult(Paged.Of<Review>([.. rows.Skip(query.Skip).Take(query.PageSize)], query, rows.Count));
        }

        public Task<ReviewSummary> SummarizeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ReviewSummary(0, 0, new Dictionary<string, int>()));

        public Task<Review?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(r => r.Id == (Guid)id));

        public Task AddAsync(Review entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Remove(Review entity) => Items.Remove(entity);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }
}
