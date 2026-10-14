using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Infrastructure.Data;
using OrderApi.Infrastructure.Search;

namespace OrderApi.Test.Search;

public class SearchExecutorTests : IDisposable
{
    private static readonly string[] InFilterValues = ["Alice", "Diego"];
    private static readonly string[] TextBetweenBounds = ["Bruno", "Carla"];
    private static readonly string[] EnumBetweenBounds = ["OPEN", "CONFIRMED"];
    private static readonly string[] BetweenBounds = ["2025-01-02T00:00:00+00:00", "2025-01-03T00:00:00+00:00"];

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

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

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

    [Theory]
    [InlineData("email", "eq", "alice@example.com", "Alice")]
    [InlineData("tax_id", "eq", "TAX-0002", "Bruno")]
    [InlineData("TaxId", "gte", "TAX-0004", "Diego")]
    public async Task SearchAsync_SensitiveFieldFilter_NarrowsResults(string field, string op, string value, string expectedName)
    {
        var result = await Search(Request(field, op, value));

        result.Items.Should().ContainSingle().Which.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("email", "Alice")]
    [InlineData("-tax_id", "Diego")]
    public async Task SearchAsync_SortBySensitiveField_Works(string sort, string expectedFirst)
    {
        var result = await Search(Request(sort: sort));

        result.Items[0].Name.Should().Be(expectedFirst);
    }

    [Theory]
    [InlineData("gt", "Bruno", "Carla,Diego")]
    [InlineData("gte", "Carla", "Carla,Diego")]
    [InlineData("lt", "Bruno", "Alice")]
    [InlineData("lte", "Bruno", "Alice,Bruno")]
    public async Task SearchAsync_OrderingOperatorsOnText_CompareStrings(string op, string value, string expected)
    {
        var result = await Search(Request("name", op, value, sort: "name"));

        result.Items.Select(c => c.Name).Should().Equal(expected.Split(','));
    }

    [Fact]
    public async Task SearchAsync_BetweenOnText_IsInclusive()
    {
        var result = await Search(Request("name", "between", TextBetweenBounds, sort: "name"));

        result.Items.Select(c => c.Name).Should().Equal("Bruno", "Carla");
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
        var result = await Search(Request("created_at", value: "2025-01-03T00:00:00+00:00"));

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
        var result = await Search(Request("name", "in", InFilterValues));

        result.Items.Select(c => c.Name).Should().BeEquivalentTo("Alice", "Diego");
    }

    [Fact]
    public async Task SearchAsync_BetweenFilter_IsInclusive()
    {
        var result = await Search(Request("created_at", "between", BetweenBounds));

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

    private Task<PagedResult<Order>> SearchOrders(SearchRequest request) =>
        _db.Orders.SearchAsync(request, _db.Model.FindEntityType(typeof(Order))!);

    private Task<PagedResult<OrderItem>> SearchItems(SearchRequest request) =>
        _db.OrderItems.SearchAsync(request, _db.Model.FindEntityType(typeof(OrderItem))!);

    private static async Task AssertInvalidFilter(Func<Task> act) =>
        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationInvalidFilter);

    [Theory]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    public async Task SearchAsync_OrderingOperatorOnEnum_IsRejected(string op)
    {
        // The status is stored as its text code: "greater than" would compare alphabetically, which means nothing.
        await AssertInvalidFilter(() => SearchOrders(Request("status", op, "OPEN")));
    }

    [Fact]
    public async Task SearchAsync_BetweenOnEnum_IsRejected() =>
        await AssertInvalidFilter(() => SearchOrders(Request("status", "between", EnumBetweenBounds)));

    [Theory]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("-1")]
    public async Task SearchAsync_NumericTextForEnum_IsRejected(string value) =>
        await AssertInvalidFilter(() => SearchOrders(Request("status", "eq", value)));

    [Fact]
    public async Task SearchAsync_EnumByName_IsCaseInsensitive()
    {
        _db.Orders.Add(new Order { CustomerId = Guid.NewGuid(), Status = OrderStatus.Confirmed, CreatedBy = "t", UpdatedBy = "t" });
        await _db.SaveChangesAsync();

        var result = await SearchOrders(Request("status", "eq", "confirmed"));

        result.Total.Should().Be(1);
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000.50")]
    [InlineData("1e3")]
    public async Task SearchAsync_DecimalWithThousandsSeparatorOrExponent_IsRejected(string value) =>
        await AssertInvalidFilter(() => SearchItems(Request("unit_price", "eq", value)));

    [Fact]
    public async Task SearchAsync_PlainDecimal_IsAccepted()
    {
        var result = await SearchItems(Request("unit_price", "eq", "1.50"));

        result.Total.Should().Be(0);
    }

    [Theory]
    [InlineData("01/02/2025")]
    [InlineData("Jan 2 2025")]
    [InlineData("yesterday")]
    public async Task SearchAsync_DateThatIsNotIso8601_IsRejected(string value) =>
        await AssertInvalidFilter(() => Search(Request("created_at", "gte", value)));

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
