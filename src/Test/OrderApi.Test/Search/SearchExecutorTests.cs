using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Infrastructure.Data;
using OrderApi.Infrastructure.Search;

namespace OrderApi.Test.Search;

public class SearchExecutorTests : IDisposable
{
    private readonly AppDbContext _db;

    public SearchExecutorTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var baseDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _db.Customers.AddRange(
            NewCustomer("Alice", "TAX-0001", baseDate),
            NewCustomer("Bruno", "TAX-0002", baseDate.AddDays(1)),
            NewCustomer("Carla", "TAX-0003", baseDate.AddDays(2)),
            NewCustomer("Diego", "TAX-0004", baseDate.AddDays(3)),
            NewCustomer("Erased", "TAX-0005", baseDate, deleted: true));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static Customer NewCustomer(string name, string taxId, DateTimeOffset createdAt, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        TaxId = taxId,
        Email = $"{name.ToLowerInvariant()}@example.com",
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
        CreatedBy = "test",
        UpdatedBy = "test",
        DeletedAt = deleted ? createdAt : null,
    };

    private static SearchRequest Request(
        string? field = null, string op = "eq", object? value = null, int page = 0, int size = 20, string? sort = null) =>
        new(page, size, sort, field is null
            ? null
            : new Dictionary<string, IReadOnlyList<FilterCondition>> { [field] = [new FilterCondition(op, value)] });

    private Task<PagedResult<Customer>> Search(SearchRequest request) =>
        _db.Customers.SearchAsync(request, _db.Model.FindEntityType(typeof(Customer))!);

    [Fact]
    public async Task SearchAsync_NoFilters_ReturnsActiveRecordsOnly()
    {
        var result = await Search(Request());

        result.Total.Should().Be(4);
        result.Items.Should().NotContain(c => c.Name == "Erased");
    }

    [Fact]
    public async Task SearchAsync_EqFilter_MatchesProperty()
    {
        var result = await Search(Request("name", value: "Bruno"));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Bruno");
    }

    [Fact]
    public async Task SearchAsync_SnakeCaseField_IsResolvedFromEntityMetadata()
    {
        var result = await Search(Request("tax_id", value: "TAX-0003"));

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Carla");
    }

    [Fact]
    public async Task SearchAsync_NeqFilter_ExcludesMatch()
    {
        var result = await Search(Request("name", "neq", "Alice"));

        result.Total.Should().Be(3);
    }

    [Fact]
    public async Task SearchAsync_GtFilterOnDate_ComparesTypedValue()
    {
        var result = await Search(Request("created_at", "gt", "2025-01-02T00:00:00+00:00"));

        result.Items.Select(c => c.Name).Should().BeEquivalentTo("Carla", "Diego");
    }

    [Fact]
    public async Task SearchAsync_LteFilterOnDate_IncludesBoundary()
    {
        var result = await Search(Request("created_at", "lte", "2025-01-02T00:00:00+00:00"));

        result.Items.Select(c => c.Name).Should().BeEquivalentTo("Alice", "Bruno");
    }

    [Fact]
    public async Task SearchAsync_InFilter_MatchesAnyValue()
    {
        var result = await Search(Request("name", "in", new[] { "Alice", "Diego" }));

        result.Items.Select(c => c.Name).Should().BeEquivalentTo("Alice", "Diego");
    }

    [Fact]
    public async Task SearchAsync_BetweenFilter_IsInclusive()
    {
        var result = await Search(Request("created_at", "between", new[] { "2025-01-02T00:00:00+00:00", "2025-01-03T00:00:00+00:00" }));

        result.Items.Select(c => c.Name).Should().BeEquivalentTo("Bruno", "Carla");
    }

    [Fact]
    public async Task SearchAsync_UnknownFilterField_IsIgnored()
    {
        var result = await Search(Request("does_not_exist", value: "x"));

        result.Total.Should().Be(4);
    }

    [Fact]
    public async Task SearchAsync_ValueNotConvertibleToPropertyType_ThrowsValidation()
    {
        var act = () => Search(Request("created_at", "gt", "not-a-date"));

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationInvalidFilter);
    }

    [Fact]
    public async Task SearchAsync_UnknownOperator_ThrowsValidation()
    {
        var act = () => Search(Request("name", "nope", "x"));

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationInvalidFilter);
    }

    [Fact]
    public async Task SearchAsync_OrderingOperatorOnString_ThrowsValidation()
    {
        var act = () => Search(Request("name", "gt", "A"));

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationInvalidFilter);
    }

    [Fact]
    public async Task SearchAsync_SortAscending_OrdersByField()
    {
        var result = await Search(Request(sort: "name"));

        result.Items.Select(c => c.Name).Should().Equal("Alice", "Bruno", "Carla", "Diego");
    }

    [Fact]
    public async Task SearchAsync_SortDescending_PrefixDashReversesOrder()
    {
        var result = await Search(Request(sort: "-name"));

        result.Items.Select(c => c.Name).Should().Equal("Diego", "Carla", "Bruno", "Alice");
    }

    [Fact]
    public async Task SearchAsync_UnknownSortField_ThrowsValidation()
    {
        var act = () => Search(Request(sort: "bogus"));

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationUnknownSort);
    }

    [Fact]
    public async Task SearchAsync_Pagination_IsZeroBasedAndReportsTotal()
    {
        var result = await Search(Request(page: 1, size: 3, sort: "name"));

        result.Items.Select(c => c.Name).Should().Equal("Diego");
        result.Total.Should().Be(4);
        result.Page.Should().Be(1);
        result.Size.Should().Be(3);
    }
}
