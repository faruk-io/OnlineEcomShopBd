using FluentValidation;
using TechBazar.Domain.Enums;

namespace TechBazar.Application.PcBuilder;

public sealed record BuildItemRequest(BuildSlot Slot, int ProductId, int Quantity = 1);
public sealed record BuildRequest(IReadOnlyList<BuildItemRequest> Items);
public sealed record SaveBuildRequest(string? Name, IReadOnlyList<BuildItemRequest> Items);

/// <summary>A chosen part priced from the database right now (the browser never supplies prices).</summary>
public sealed record BuildLineDto(
    BuildSlot Slot, int ProductId, string Name, string Slug, string Sku, string? ImageUrl, string BrandName,
    decimal ListPrice, decimal UnitPrice, int Quantity, decimal LineTotal, StockStatus StockStatus, bool Purchasable);

public sealed record BuildReportDto(
    IReadOnlyList<BuildLineDto> Lines, decimal Total, decimal Savings, bool AllPurchasable, CompatibilityReport Compatibility);

public sealed record SavedBuildDto(string Code, string? Name, DateTime CreatedAt, BuildReportDto Report);

public sealed record BuilderSlotDto(BuildSlot Slot, string Label, string CategorySlug, bool Required, bool AllowMultiple);

public interface IPcBuilderService
{
    IReadOnlyList<BuilderSlotDto> Slots();
    Task<BuildReportDto> EvaluateAsync(BuildRequest request, CancellationToken ct = default);
    Task<SavedBuildDto> SaveAsync(Guid? userId, SaveBuildRequest request, CancellationToken ct = default);
    Task<SavedBuildDto> GetAsync(string code, CancellationToken ct = default);
}

public sealed class BuildItemRequestValidator : AbstractValidator<BuildItemRequest>
{
    public BuildItemRequestValidator()
    {
        RuleFor(x => x.Slot).IsInEnum();
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity).InclusiveBetween(1, 8);
    }
}

public sealed class BuildRequestValidator : AbstractValidator<BuildRequest>
{
    public BuildRequestValidator()
    {
        RuleFor(x => x.Items).NotNull().Must(i => i.Count <= 24).WithMessage("A build can have at most 24 parts.");
        RuleForEach(x => x.Items).SetValidator(new BuildItemRequestValidator());
    }
}

public sealed class SaveBuildRequestValidator : AbstractValidator<SaveBuildRequest>
{
    public SaveBuildRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(100);
        RuleFor(x => x.Items).NotNull().Must(i => i.Count is >= 1 and <= 24).WithMessage("Add at least one part before saving.");
        RuleForEach(x => x.Items).SetValidator(new BuildItemRequestValidator());
    }
}
