using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;

namespace TodayEcommerce.Core.Application.Catalog.Queries.SearchProducts;

/// <summary>
/// Full-text search with filters and paging. Served by the search index, not the database.
/// Not cached: the space of possible queries is huge and Elasticsearch is already built for this load.
/// </summary>
public sealed record SearchProductsQuery(
    string? Text,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool InStockOnly = false,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProductSearchHit>>;

internal sealed class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public const int MaxPageSize = 50;
    public const int MaxTextLength = 100;

    // Deep paging is expensive in Elasticsearch (from + size ≤ 10 000 by default).
    public const int MaxPage = 100;

    public SearchProductsQueryValidator()
    {
        RuleFor(x => x.Text).MaximumLength(MaxTextLength);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0).When(x => x.MinPrice.HasValue);
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0).When(x => x.MaxPrice.HasValue);
        RuleFor(x => x)
            .Must(x => x.MinPrice <= x.MaxPrice)
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue)
            .WithName("MaxPrice")
            .WithMessage("MaxPrice must be greater than or equal to MinPrice.");
        RuleFor(x => x.Page).InclusiveBetween(1, MaxPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

internal sealed class SearchProductsQueryHandler : IRequestHandler<SearchProductsQuery, PagedResult<ProductSearchHit>>
{
    private readonly IProductSearchService _search;

    public SearchProductsQueryHandler(IProductSearchService search)
    {
        _search = search;
    }

    public Task<PagedResult<ProductSearchHit>> Handle(SearchProductsQuery request, CancellationToken cancellationToken) =>
        _search.SearchAsync(
            new ProductSearchCriteria(
                string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim(),
                request.MinPrice,
                request.MaxPrice,
                request.InStockOnly,
                request.Page,
                request.PageSize),
            cancellationToken);
}
