namespace OrderApi.Domain.Common.Search;

/// <summary>One condition of a field filter, e.g. <c>{ "op": "gte", "value": 10 }</c>.</summary>
public sealed record FilterCondition(string Op = "eq", object? Value = null);

/// <summary>Filters (field to conditions), sort (prefix "-" for descending) and 0-based pagination.</summary>
public sealed record SearchRequest(
    int Page = 0,
    int Size = 20,
    string? Sort = null,
    IReadOnlyDictionary<string, IReadOnlyList<FilterCondition>>? Filters = null);


public sealed record PagedResult<T>(IReadOnlyList<T> Items, long Total, int Page, int Size)
{
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), Total, Page, Size);
}
