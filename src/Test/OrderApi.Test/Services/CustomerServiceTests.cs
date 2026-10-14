using AwesomeAssertions;
using Moq;
using OrderApi.Application.Common;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Domain.Interfaces;

namespace OrderApi.Test.Services;

public class CustomerServiceTests
{
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<ICurrentUserAccessor> _user = new();
    private readonly CustomerService _service;

    public CustomerServiceTests()
    {
        _user.SetupGet(u => u.UserName).Returns("admin-1");
        _service = new CustomerService(_customers.Object, _orders.Object, _user.Object, TimeProvider.System);
    }

    private static CreateCustomerRequest NewRequest(string taxId = "12345678900", string? passport = null) =>
        new("Ana", taxId, passport, "ana@example.com");

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsWithAuditAndReturnsDto()
    {
        Customer? saved = null;
        _customers.Setup(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()))
            .Callback<Customer, CancellationToken>((c, _) => saved = c);

        var result = await _service.CreateAsync(NewRequest());

        saved.Should().NotBeNull();
        saved!.TaxId.Should().Be("12345678900");
        saved.CreatedBy.Should().Be("admin-1");
        saved.UpdatedBy.Should().Be("admin-1");
        result.Name.Should().Be("Ana");
    }

    [Fact]
    public async Task CreateAsync_DuplicateTaxId_ThrowsConflict()
    {
        _customers.Setup(r => r.ExistsByTaxIdAsync("12345678900", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.CreateAsync(NewRequest());

        (await act.Should().ThrowAsync<ConflictDomainException>()).Which.Code.Should().Be(ErrorCodes.ConflictTaxId);
        _customers.Verify(r => r.AddAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DuplicatePassport_ThrowsConflict()
    {
        _customers.Setup(r => r.ExistsByPassportNumberAsync("AB123456", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.CreateAsync(NewRequest(passport: "AB123456"));

        (await act.Should().ThrowAsync<ConflictDomainException>()).Which.Code.Should().Be(ErrorCodes.ConflictPassportNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task CreateAsync_BlankPassport_SkipsPassportUniquenessCheck(string? passport)
    {
        await _service.CreateAsync(NewRequest(passport: passport));

        _customers.Verify(
            r => r.ExistsByPassportNumberAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsDto()
    {
        var id = Guid.NewGuid();
        _customers.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new Customer { Id = id, Name = "Ana" });

        var result = await _service.GetAsync(id);

        result.Should().Be(new CustomerDto(id, "Ana"));
    }

    [Fact]
    public async Task GetAsync_NonExistingId_ThrowsNotFound()
    {
        var act = () => _service.GetAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<NotFoundDomainException>()).Which.Code.Should().Be(ErrorCodes.CustomerNotFound);
    }

    [Fact]
    public async Task UpdateAsync_ExistingId_UpdatesFieldsAndAudit()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id, Name = "Old", TaxId = "00000", Email = "o@x.com" };
        _customers.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);

        var result = await _service.UpdateAsync(id, new UpdateCustomerRequest("New", "98765", null, "n@x.com"));

        customer.Name.Should().Be("New");
        customer.TaxId.Should().Be("98765");
        customer.Email.Should().Be("n@x.com");
        customer.UpdatedBy.Should().Be("admin-1");
        result.Name.Should().Be("New");
        _customers.Verify(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingId_ThrowsNotFound()
    {
        var act = () => _service.UpdateAsync(Guid.NewGuid(), new UpdateCustomerRequest("N", "98765", null, "n@x.com"));

        await act.Should().ThrowAsync<NotFoundDomainException>();
    }

    [Fact]
    public async Task UpdateAsync_TaxIdOfAnotherCustomer_ThrowsConflictExcludingSelf()
    {
        var id = Guid.NewGuid();
        _customers.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new Customer { Id = id });
        _customers.Setup(r => r.ExistsByTaxIdAsync("98765", id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.UpdateAsync(id, new UpdateCustomerRequest("N", "98765", null, "n@x.com"));

        (await act.Should().ThrowAsync<ConflictDomainException>()).Which.Code.Should().Be(ErrorCodes.ConflictTaxId);
    }

    [Fact]
    public async Task DeleteAsync_WithoutOrders_SoftDeletes()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id };
        _customers.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);

        await _service.DeleteAsync(id);

        customer.DeletedAt.Should().NotBeNull();
        customer.DeletedBy.Should().Be("admin-1");
        _customers.Verify(r => r.UpdateAsync(customer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_WithActiveOrders_ThrowsConflict()
    {
        var id = Guid.NewGuid();
        var customer = new Customer { Id = id };
        _customers.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(customer);
        _orders.Setup(r => r.ExistsActiveByCustomerAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => _service.DeleteAsync(id);

        (await act.Should().ThrowAsync<ConflictDomainException>()).Which.Code.Should().Be(ErrorCodes.ConflictCustomerHasOrders);
        customer.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingId_ThrowsNotFound()
    {
        var act = () => _service.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundDomainException>();
    }

    [Fact]
    public async Task SearchAsync_MapsPagedEntitiesToDtos()
    {
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Ana" };
        var request = new SearchRequest(Page: 1, Size: 5);
        _customers.Setup(r => r.SearchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Customer>([customer], 11, 1, 5));

        var result = await _service.SearchAsync(request);

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Ana");
        result.Total.Should().Be(11);
        result.Page.Should().Be(1);
    }
}
