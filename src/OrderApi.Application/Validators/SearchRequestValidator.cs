using FluentValidation;
using OrderApi.Domain.Common.Search;

namespace OrderApi.Application.Validators;

public sealed class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    public const int MaxPageSize = 100;

    public SearchRequestValidator()
    {
        RuleFor(r => r.Page).GreaterThanOrEqualTo(0);
        RuleFor(r => r.Size).InclusiveBetween(1, MaxPageSize);
    }
}
