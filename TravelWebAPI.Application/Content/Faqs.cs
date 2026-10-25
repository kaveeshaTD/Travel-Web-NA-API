using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Content;

/// <summary>The body of a create or update. A value that is not sent keeps the stored one.</summary>
public sealed record FaqRequest(string? Question = null, string? Answer = null);

/// <summary>The complete, trimmed FAQ that is validated and saved.</summary>
public sealed record FaqDraft(string Question, string Answer);

public sealed record FaqDto(string Id, string Question, string Answer) : IContentDto;

public sealed class FaqDraftValidator : AbstractValidator<FaqDraft>
{
    public const int MaxQuestion = 300;
    public const int MaxAnswer = 4000;

    public FaqDraftValidator()
    {
        RuleFor(x => x.Question).MinLength(5, "Enter the question");
        RuleFor(x => x.Question).MaxLength(MaxQuestion, "The question is too long");
        RuleFor(x => x.Answer).MinLength(5, "Enter the answer");
        RuleFor(x => x.Answer).MaxLength(MaxAnswer, "The answer is too long");
    }
}

public sealed class FaqService : IContentService<FaqRequest, FaqDto>
{
    private readonly ContentWriter<Faq> _writer;
    private readonly IContentRepository<Faq> _faqs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<FaqDraft> _validator;

    public FaqService(IContentRepository<Faq> faqs, IUnitOfWork unitOfWork, IValidator<FaqDraft> validator)
    {
        _faqs = faqs;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _writer = new ContentWriter<Faq>(faqs, unitOfWork, "FAQ");
    }

    public async Task<IReadOnlyList<FaqDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default)
        => [.. (await _faqs.ListAsync(ct)).Select(ToDto)];

    public async Task<FaqDto> GetAsync(string id, CancellationToken ct = default) => ToDto(await _writer.GetAsync(id, ct));

    public async Task<FaqDto> CreateAsync(FaqRequest request, CancellationToken ct = default)
    {
        var draft = Apply(request, new FaqDraft(string.Empty, string.Empty));
        _validator.ThrowIfInvalid(draft);

        var faq = new Faq { Question = draft.Question, Answer = draft.Answer };
        await _writer.AddAsync(faq, draft.Question, ct);
        return ToDto(faq);
    }

    public async Task<FaqDto> UpdateAsync(string id, FaqRequest request, CancellationToken ct = default)
    {
        var faq = await _writer.GetAsync(id, ct);
        var draft = Apply(request, new FaqDraft(faq.Question, faq.Answer));
        _validator.ThrowIfInvalid(draft);

        faq.Question = draft.Question;
        faq.Answer = draft.Answer;
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(faq);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _writer.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<FaqDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default)
        => [.. (await _writer.ReorderAsync(request, ct)).Select(ToDto)];

    private static FaqDraft Apply(FaqRequest request, FaqDraft stored)
        => new(Merge.Text(request.Question, stored.Question), Merge.Text(request.Answer, stored.Answer));

    private static FaqDto ToDto(Faq faq) => new(faq.Id, faq.Question, faq.Answer);
}
