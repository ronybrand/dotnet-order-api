using FluentAssertions;
using FluentValidation.TestHelper;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services;
using OrderApi.Application.Validators;

namespace OrderApi.Test.Validators;

public class CustomerValidatorsTests
{
    private readonly CreateCustomerRequestValidator _create = new();
    private readonly UpdateCustomerRequestValidator _update = new();

    private static CreateCustomerRequest Valid() => new("Ana", "123.456-78", null, "ana@example.com");

    [Fact]
    public void Create_ValidRequest_IsValid() => _create.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_ValidPassport_IsValid() =>
        _create.TestValidate(Valid() with { PassportNumber = "AB123456" }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankName_Fails(string name) =>
        _create.TestValidate(Valid() with { Name = name }).ShouldHaveValidationErrorFor(r => r.Name);

    [Theory]
    [InlineData("1234")]
    [InlineData("123456789012345678901")]
    [InlineData("abc def")]
    [InlineData("")]
    public void Create_InvalidTaxId_Fails(string taxId) =>
        _create.TestValidate(Valid() with { TaxId = taxId }).ShouldHaveValidationErrorFor(r => r.TaxId);

    [Theory]
    [InlineData("ab123456")]
    [InlineData("AB123")]
    [InlineData("AB1234567890")]
    public void Create_InvalidPassport_Fails(string passport) =>
        _create.TestValidate(Valid() with { PassportNumber = passport }).ShouldHaveValidationErrorFor(r => r.PassportNumber);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Create_InvalidEmail_Fails(string email) =>
        _create.TestValidate(Valid() with { Email = email }).ShouldHaveValidationErrorFor(r => r.Email);

    [Fact]
    public void Update_InvalidTaxId_Fails() =>
        _update.TestValidate(new UpdateCustomerRequest("Ana", "1", null, "ana@example.com"))
            .ShouldHaveValidationErrorFor(r => r.TaxId);

    [Fact]
    public void Update_ValidRequest_IsValid() =>
        _update.TestValidate(new UpdateCustomerRequest("Ana", "123456", null, "ana@example.com"))
            .ShouldNotHaveAnyValidationErrors();
}

public class OrderValidatorsTests
{
    private readonly CreateOrderRequestValidator _create = new();
    private readonly ItemRequestValidator _item = new();
    private readonly UpdateItemQuantityRequestValidator _quantity = new();

    private static ItemRequest ValidItem() => new("Pen", 2.50m, 2);

    [Fact]
    public void CreateOrder_ValidRequest_IsValid() =>
        _create.TestValidate(new CreateOrderRequest(Guid.NewGuid(), [ValidItem()])).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateOrder_NoItems_IsValid() =>
        _create.TestValidate(new CreateOrderRequest(Guid.NewGuid(), null)).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void CreateOrder_EmptyCustomerId_Fails() =>
        _create.TestValidate(new CreateOrderRequest(Guid.Empty, [])).ShouldHaveValidationErrorFor(r => r.CustomerId);

    [Fact]
    public void CreateOrder_TooManyItems_Fails()
    {
        var items = Enumerable.Range(0, OrderService.MaxItemsPerOrder + 1).Select(_ => ValidItem()).ToList();

        _create.TestValidate(new CreateOrderRequest(Guid.NewGuid(), items)).ShouldHaveValidationErrorFor(r => r.Items);
    }

    [Fact]
    public void CreateOrder_InvalidNestedItem_Fails()
    {
        var result = _create.TestValidate(new CreateOrderRequest(Guid.NewGuid(), [ValidItem() with { Quantity = 0 }]));

        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Item_BlankDescription_Fails(string description) =>
        _item.TestValidate(ValidItem() with { Description = description }).ShouldHaveValidationErrorFor(i => i.Description);

    [Fact]
    public void Item_DescriptionOver255_Fails() =>
        _item.TestValidate(ValidItem() with { Description = new string('a', 256) }).ShouldHaveValidationErrorFor(i => i.Description);

    [Fact]
    public void Item_DescriptionAt255_IsValid() =>
        _item.TestValidate(ValidItem() with { Description = new string('a', 255) }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Item_NonPositivePrice_Fails(double price) =>
        _item.TestValidate(ValidItem() with { UnitPrice = (decimal)price }).ShouldHaveValidationErrorFor(i => i.UnitPrice);

    [Fact]
    public void Item_PriceWithThreeDecimals_Fails() =>
        _item.TestValidate(ValidItem() with { UnitPrice = 1.234m }).ShouldHaveValidationErrorFor(i => i.UnitPrice);

    [Fact]
    public void Item_PriceWithTwoDecimals_IsValid() =>
        _item.TestValidate(ValidItem() with { UnitPrice = 1.23m }).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Item_NonPositiveQuantity_Fails(int quantity) =>
        _item.TestValidate(ValidItem() with { Quantity = quantity }).ShouldHaveValidationErrorFor(i => i.Quantity);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateQuantity_NonPositive_Fails(int quantity) =>
        _quantity.TestValidate(new UpdateItemQuantityRequest(quantity)).ShouldHaveValidationErrorFor(r => r.Quantity);

    [Fact]
    public void UpdateQuantity_Positive_IsValid() =>
        _quantity.TestValidate(new UpdateItemQuantityRequest(1)).ShouldNotHaveAnyValidationErrors();
}
