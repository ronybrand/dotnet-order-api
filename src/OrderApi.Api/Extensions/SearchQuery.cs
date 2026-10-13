using OrderApi.Domain.Common.Search;

namespace OrderApi.Api.Extensions;

/// <summary>Query-string form of a search (no filters: a dictionary of lists is not expressible as flat params).</summary>
public sealed record SearchQuery(int Page = 0, int Size = 20, string? Sort = null)
{
    public SearchRequest ToSearchRequest() => new(Page, Size, Sort);
}
