using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Reviews;

/// <summary>What a visitor sends with <c>POST /api/reviews</c>. The rating is a decimal so "4.5" gets the contract's message.</summary>
public sealed record ReviewSubmission(string? Name = null, string? Country = null, decimal? Rating = null, string? TripType = null, string? Comment = null);

public sealed record ReviewSubmissionDraft(string Name, string Country, decimal Rating, string TripType, string Comment);

/// <summary>An admin save (add or edit). A value that is not sent keeps the stored one; <c>Date</c> is <c>YYYY-MM-DD</c>.</summary>
public sealed record ReviewRequest(
    string? Name = null,
    string? Country = null,
    string? CountryCode = null,
    decimal? Rating = null,
    string? Photo = null,
    string? TripType = null,
    string? Comment = null,
    string? Date = null,
    string? Status = null);

public sealed record ReviewDraft(
    string Name,
    string Country,
    string CountryCode,
    decimal Rating,
    string Photo,
    string TripType,
    string Comment,
    string Date,
    string Status);

/// <summary>A review as visitors see it. There is no author id.</summary>
public sealed record ReviewDto(
    Guid Id,
    string Name,
    string Country,
    string CountryCode,
    int Rating,
    string Photo,
    string TripType,
    string Comment,
    string Date,
    string Status);

/// <summary>A review as the admin sees it: the same fields plus the signed-in author, if there was one.</summary>
public sealed record AdminReviewDto(
    Guid Id,
    string Name,
    string Country,
    string CountryCode,
    int Rating,
    string Photo,
    string TripType,
    string Comment,
    string Date,
    string Status,
    Guid? UserId);

/// <summary>The receipt for a submitted review or message, for example <c>REV-1A2B3C4D</c>.</summary>
public sealed record SubmissionReceipt(string Reference);

public interface IReviewRepository : IRepository<Review>
{
    /// <summary>Reviews newest first. With <paramref name="publishedOnly"/> only the published ones.</summary>
    Task<IReadOnlyList<Review>> ListAsync(bool publishedOnly, CancellationToken cancellationToken = default);
}

public static class References
{
    /// <summary>
    /// Prefix plus the last 8 hex digits of the id in upper case. The first digits of a version 7 id are the time and would
    /// repeat for records created close together. It is only a receipt; nothing looks a record up by it.
    /// </summary>
    public static string For(string prefix, Guid id) => $"{prefix}-{id.ToString("N")[^8..].ToUpperInvariant()}";
}

public sealed class ReviewSubmissionDraftValidator : AbstractValidator<ReviewSubmissionDraft>
{
    public ReviewSubmissionDraftValidator()
    {
        RuleFor(x => x.Name).MinLength(2, "Enter your name");
        RuleFor(x => x.Name).MaxLength(120, "Your name is too long");
        RuleFor(x => x.Country).MinLength(2, "Enter your country");
        RuleFor(x => x.Country).MaxLength(80, "The country is too long");
        RuleFor(x => x.Rating).Must(ReviewRules.IsRating).WithMessage("Choose a rating from 1 to 5");
        RuleFor(x => x.TripType).MinLength(1, "Choose your trip type");
        RuleFor(x => x.TripType).MaxLength(80, "The trip type is too long");
        RuleFor(x => x.Comment).MinLength(10, "Please write at least 10 characters");
        RuleFor(x => x.Comment).MaxLength(ReviewRules.MaxComment, "The comment is too long");
    }
}

public sealed partial class ReviewDraftValidator : AbstractValidator<ReviewDraft>
{
    public const string DateMessage = "Enter a valid date that is not in the future";

    public static readonly string StatusMessage = $"Status must be one of: {string.Join(", ", Enum.GetValues<ReviewStatus>().Select(EnumWire.ToWire))}";

    public ReviewDraftValidator(IClock clock)
    {
        RuleFor(x => x.Name).MinLength(2, "Enter the reviewer name");
        RuleFor(x => x.Name).MaxLength(120, "The reviewer name is too long");
        RuleFor(x => x.Country).MaxLength(80, "The country is too long");
        RuleFor(x => x.CountryCode).Must(code => code.Length == 0 || CountryCodePattern().IsMatch(code)).WithMessage("Country code must be two letters");
        RuleFor(x => x.Rating).Must(ReviewRules.IsRating).WithMessage("Rating must be 1 to 5");
        RuleFor(x => x.Photo).Must(photo => photo.Length == 0 || ImageReference.IsValid(photo))
            .WithMessage("Photo must be a link, an uploaded file or a website photo");
        RuleFor(x => x.TripType).MaxLength(80, "The trip type is too long");
        RuleFor(x => x.Comment).MinLength(10, "Comment is too short");
        RuleFor(x => x.Comment).MaxLength(ReviewRules.MaxComment, "The comment is too long");
        RuleFor(x => x.Date).Must(date => ReviewRules.TryParseDate(date, out var parsed) && parsed <= clock.TodayInSriLanka).WithMessage(DateMessage);
        RuleFor(x => x.Status).Must(status => EnumWire.TryFromWire<ReviewStatus>(status, out _)).WithMessage(StatusMessage);
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryCodePattern();
}

public static class ReviewRules
{
    public const int MaxComment = 2000;

    public static bool IsRating(decimal rating) => rating is >= 1 and <= 5 && rating == decimal.Truncate(rating);

    public static bool TryParseDate(string? text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>Reading, submitting, moderating and deleting reviews.</summary>
public sealed class ReviewService(
    IReviewRepository reviews,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IValidator<ReviewSubmissionDraft> submissionValidator,
    IValidator<ReviewDraft> draftValidator)
{
    public const string NotFoundMessage = "Review not found";

    public async Task<IReadOnlyList<ReviewDto>> ListPublishedAsync(CancellationToken cancellationToken = default)
        => [.. (await reviews.ListAsync(publishedOnly: true, cancellationToken)).Select(ToPublic)];

    public async Task<IReadOnlyList<AdminReviewDto>> ListAllAsync(CancellationToken cancellationToken = default)
        => [.. (await reviews.ListAsync(publishedOnly: false, cancellationToken)).Select(ToAdmin)];

    /// <summary>A visitor's review: saved as pending, today's date, linked to the signed-in customer when there is one.</summary>
    public async Task<SubmissionReceipt> SubmitAsync(ReviewSubmission submission, CancellationToken cancellationToken = default)
    {
        var draft = new ReviewSubmissionDraft(
            Merge.Text(submission.Name, string.Empty),
            Merge.Text(submission.Country, string.Empty),
            submission.Rating ?? 0,
            Merge.Text(submission.TripType, string.Empty),
            Merge.Text(submission.Comment, string.Empty));
        submissionValidator.ThrowIfInvalid(draft);

        var review = new Review
        {
            Id = Guid.CreateVersion7(),
            UserId = currentUser.UserId,
            Name = draft.Name,
            Country = draft.Country,
            CountryCode = CountryCodes.Lookup(draft.Country),
            Rating = (short)draft.Rating,
            TripType = draft.TripType,
            Comment = draft.Comment,
            ReviewDate = clock.TodayInSriLanka,
            Status = ReviewStatus.Pending,
        };

        await reviews.AddAsync(review, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SubmissionReceipt(References.For("REV", review.Id));
    }

    /// <summary>An admin adds a review (published unless another status is sent).</summary>
    public async Task<AdminReviewDto> AddAsync(ReviewRequest request, CancellationToken cancellationToken = default)
    {
        var draft = Apply(request, new ReviewDraft(
            string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty,
            clock.TodayInSriLanka.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), EnumWire.ToWire(ReviewStatus.Published)));
        draftValidator.ThrowIfInvalid(draft);

        var review = new Review { Id = Guid.CreateVersion7() };
        Copy(draft, review);
        await reviews.AddAsync(review, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAdmin(review);
    }

    /// <summary>Edits or moderates a review: what is not sent keeps its value, then the whole review is validated.</summary>
    public async Task<AdminReviewDto> UpdateAsync(Guid id, ReviewRequest request, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        var draft = Apply(request, ToDraft(review));
        draftValidator.ThrowIfInvalid(draft);

        Copy(draft, review);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAdmin(review);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await reviews.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(NotFoundMessage);
        reviews.Remove(review);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static ReviewDraft Apply(ReviewRequest request, ReviewDraft stored)
    {
        var country = Merge.Text(request.Country, stored.Country);

        // A new country without a new code gets the code of the new country; a code the admin typed is kept.
        var code = request.CountryCode is not null
            ? request.CountryCode.Trim().ToUpperInvariant()
            : request.Country is not null || stored.CountryCode.Length == 0 ? CountryCodes.Lookup(country) : stored.CountryCode;

        return new ReviewDraft(
            Merge.Text(request.Name, stored.Name),
            country,
            code,
            request.Rating ?? stored.Rating,
            Merge.Text(request.Photo, stored.Photo),
            Merge.Text(request.TripType, stored.TripType),
            Merge.Text(request.Comment, stored.Comment),
            Merge.Text(request.Date, stored.Date),
            Merge.Text(request.Status, stored.Status).ToLowerInvariant());
    }

    private static ReviewDraft ToDraft(Review review) => new(
        review.Name,
        review.Country,
        review.CountryCode.Trim(),
        review.Rating,
        review.Photo ?? string.Empty,
        review.TripType,
        review.Comment,
        review.ReviewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EnumWire.ToWire(review.Status));

    private static void Copy(ReviewDraft draft, Review review)
    {
        review.Name = draft.Name;
        review.Country = draft.Country;
        review.CountryCode = draft.CountryCode;
        review.Rating = (short)draft.Rating;
        review.Photo = draft.Photo.Length == 0 ? null : draft.Photo;
        review.TripType = draft.TripType;
        review.Comment = draft.Comment;
        review.ReviewDate = DateOnly.ParseExact(draft.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        review.Status = EnumWire.FromWire<ReviewStatus>(draft.Status);
    }

    private static ReviewDto ToPublic(Review r) => new(
        r.Id,
        r.Name,
        r.Country,
        r.CountryCode.Trim(),
        r.Rating,
        r.Photo ?? string.Empty,
        r.TripType,
        r.Comment,
        r.ReviewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EnumWire.ToWire(r.Status));

    private static AdminReviewDto ToAdmin(Review r) => new(
        r.Id,
        r.Name,
        r.Country,
        r.CountryCode.Trim(),
        r.Rating,
        r.Photo ?? string.Empty,
        r.TripType,
        r.Comment,
        r.ReviewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        EnumWire.ToWire(r.Status),
        r.UserId);
}
