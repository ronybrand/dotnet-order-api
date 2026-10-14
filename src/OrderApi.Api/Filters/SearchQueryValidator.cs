using FluentValidation;
using OrderApi.Api.Extensions;
using OrderApi.Application.Validators;

namespace OrderApi.Api.Extensions;

public sealed class SearchQueryValidator : AbstractValidator<SearchQuery>
{
    public SearchQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(0)
            .Must((q, page) => SearchRequestValidator.IsOffsetInRange(page, q.Size))
            .WithMessage("'{PropertyName}' is too large.");
        RuleFor(q => q.Size).InclusiveBetween(1, SearchRequestValidator.MaxPageSize);
    }
}
