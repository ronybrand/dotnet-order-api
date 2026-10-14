using System.Collections;
using System.Text.Json;
using FluentValidation;
using OrderApi.Domain.Common.Search;

namespace OrderApi.Application.Validators;

public sealed class SearchRequestValidator : AbstractValidator<SearchRequest>
{
    public const int MaxPageSize = 100;
    public const int MaxFilterFields = 10;
    public const int MaxConditionsPerField = 10;
    public const int MaxInValues = 100;
    public const int MaxValueLength = 200;

    public SearchRequestValidator()
    {
        RuleFor(r => r.Page).GreaterThanOrEqualTo(0).Must((r, page) => IsOffsetInRange(page, r.Size))
            .WithMessage("'{PropertyName}' is too large.");
        RuleFor(r => r.Size).InclusiveBetween(1, MaxPageSize);
        RuleFor(r => r.Filters)
            .Must(HasWellFormedFilters)
            .WithMessage($"'{{PropertyName}}' is malformed or exceeds {MaxFilterFields} fields, {MaxConditionsPerField} conditions per field, {MaxInValues} values per list or {MaxValueLength} characters per value.")
            .When(r => r.Filters is not null);
    }

    /// <summary>The row offset (page * size) must fit the int taken by Skip, otherwise it overflows into a negative value.</summary>
    public static bool IsOffsetInRange(int page, int size) => (long)page * size <= int.MaxValue;

    // The body is deserialized without nullable enforcement, so null lists, conditions and operators
    // are rejected here instead of surfacing as a NullReferenceException (500) in the search engine.
    private static bool HasWellFormedFilters(IReadOnlyDictionary<string, IReadOnlyList<FilterCondition>>? filters) =>
        filters is null
        || (filters.Count <= MaxFilterFields
            && filters.Values.All(conditions =>
                conditions is { Count: <= MaxConditionsPerField }
                && conditions.All(condition => condition is { Op: not null } && ValueCount(condition.Value) <= MaxInValues
                    && ValuesFit(condition.Value))));

    private static int ValueCount(object? value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Array } array => array.GetArrayLength(),
        string => 1,
        ICollection collection => collection.Count,
        _ => 1,
    };

    private static bool ValuesFit(object? value) => value switch
    {
        string text => text.Length <= MaxValueLength,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!.Length <= MaxValueLength,
        JsonElement { ValueKind: JsonValueKind.Array } array => array.EnumerateArray().All(item => ValuesFit(item)),
        IEnumerable items => items.Cast<object?>().All(ValuesFit),
        _ => true,
    };
}
