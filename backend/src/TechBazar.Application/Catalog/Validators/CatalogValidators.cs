using FluentValidation;

namespace TechBazar.Application.Catalog.Validators;

public abstract class ProductListQueryValidatorBase<T> : AbstractValidator<T> where T : ProductListQuery
{
    protected ProductListQueryValidatorBase()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ProductListQuery.MaxPageSize);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).When(x => x.MaxPrice.HasValue);
        RuleFor(x => x).Must(x => x.MinPrice is null || x.MaxPrice is null || x.MinPrice <= x.MaxPrice)
            .WithName("MinPrice").WithMessage("minPrice must be less than or equal to maxPrice.");
        RuleFor(x => x.Sort)
            .Must(s => ProductListQuery.AllowedSorts.Contains(s!.Trim().ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort))
            .WithMessage($"sort must be one of: {string.Join(", ", ProductListQuery.AllowedSorts)}.");
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.Category).MaximumLength(200);
        RuleFor(x => x.Spec)
            .Must(list => list!.All(s => SpecFilterParser.TryParseOne(s, out _, out _)))
            .When(x => x.Spec is { Count: > 0 })
            .WithMessage("spec filters must look like 'Key:Value', e.g. spec=Socket:AM5.");
        RuleFor(x => x.Spec).Must(l => l!.Count <= 20).When(x => x.Spec is not null).WithMessage("At most 20 spec filters.");
        RuleFor(x => x.Brand).Must(l => l!.Count <= 30).When(x => x.Brand is not null).WithMessage("At most 30 brand filters.");
    }
}

public sealed class ProductListQueryValidator : ProductListQueryValidatorBase<ProductListQuery>;

/// <summary>Same contract as <see cref="ProductListQuery"/> but <c>q</c> is mandatory (GET /search).</summary>
public sealed class SearchQuery : ProductListQuery;

public sealed class SearchQueryValidator : ProductListQueryValidatorBase<SearchQuery>
{
    public SearchQueryValidator()
    {
        RuleFor(x => x.Q).NotEmpty().MinimumLength(2).WithMessage("q must be at least 2 characters.");
    }
}

public sealed record AutocompleteQuery(string? Q, int Limit = 8);

public sealed class AutocompleteQueryValidator : AbstractValidator<AutocompleteQuery>
{
    public AutocompleteQueryValidator()
    {
        RuleFor(x => x.Q).NotEmpty().MinimumLength(2).MaximumLength(100);
        RuleFor(x => x.Limit).InclusiveBetween(1, 20);
    }
}
