using AwesomeAssertions;
using Moq;
using OrderApi.Application.Common;
using OrderApi.Application.DTOs;
using OrderApi.Application.Events;
using OrderApi.Application.Services;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Domain.Interfaces;

namespace OrderApi.Test.Services;

public class OrderServiceTests
{
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly Mock<ICurrentUserAccessor> _user = new();
    private readonly Mock<IOrderEventPublisher> _publisher = new();
    private readonly OrderService _service;

    public OrderServiceTests()
    {
        _user.SetupGet(u => u.UserName).Returns("user-1");
        _service = new OrderService(_orders.Object, _customers.Object, _user.Object, _publisher.Object, TimeProvider.System);
    }

    private static ItemRequest Item(string description = "Pen", decimal price = 2.50m, int quantity = 2) =>
        new(description, price, quantity);

    private static OrderItem Line(decimal price = 10m, int quantity = 1) =>
        new() { Id = Guid.NewGuid(), Description = "x", UnitPrice = price, Quantity = quantity };

    private Order GivenOrder(OrderStatus status = OrderStatus.Open, params OrderItem[] items)
    {
        var order = new Order { Id = Guid.NewGuid(), CustomerId = Guid.NewGuid(), Status = status, Items = [.. items] };
        _orders.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        return order;
    }

    private void GivenCustomer(Guid id, string email = "ana@example.com") =>
        _customers.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer { Id = id, Name = "Ana", Email = email });

    private void VerifyNothingPublished() =>
        _publisher.Verify(
            p => p.PublishStatusChangedAsync(It.IsAny<OrderStatusChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // --- create ---

    [Fact]
    public async Task CreateAsync_ExistingCustomer_MarksCustomerReferencedBeforeSavingTheOrder()
    {
        var customerId = Guid.NewGuid();
        GivenCustomer(customerId);
        var calls = new List<string>();
        _customers.Setup(r => r.MarkReferenced(It.IsAny<Customer>())).Callback(() => calls.Add("mark"));
        _orders.Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).Callback(() => calls.Add("add"));

        await _service.CreateAsync(new CreateOrderRequest(customerId, [Item()]));

        // Touching the customer row in the same save is what makes a racing soft delete fail with a 409.
        calls.Should().Equal("mark", "add");
    }

    [Fact]
    public async Task CreateAsync_ExistingCustomer_CreatesOpenOrderWithItemsAndTotal()
    {
        var customerId = Guid.NewGuid();
        GivenCustomer(customerId);
        Order? saved = null;
        _orders.Setup(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((o, _) => saved = o);

        var result = await _service.CreateAsync(new CreateOrderRequest(customerId, [Item(), Item("Book", 10m, 1)]));

        saved.Should().NotBeNull();
        saved!.Status.Should().Be(OrderStatus.Open);
        saved.CreatedBy.Should().Be("user-1");
        result.Total.Should().Be(15m);
        result.Items.Should().HaveCount(2);
        result.Status.Should().Be("OPEN");
    }

    [Fact]
    public async Task CreateAsync_NullItems_CreatesEmptyOrder()
    {
        var customerId = Guid.NewGuid();
        GivenCustomer(customerId);

        var result = await _service.CreateAsync(new CreateOrderRequest(customerId, null));

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0m);
    }

    [Fact]
    public async Task CreateAsync_NonExistingCustomer_ThrowsValidation()
    {
        var act = () => _service.CreateAsync(new CreateOrderRequest(Guid.NewGuid(), [Item()]));

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationCustomerNotFound);
        _orders.Verify(r => r.AddAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_MoreThanMaxItems_ThrowsValidation()
    {
        var customerId = Guid.NewGuid();
        GivenCustomer(customerId);
        var items = Enumerable.Range(0, OrderService.MaxItemsPerOrder + 1).Select(_ => Item()).ToList();

        var act = () => _service.CreateAsync(new CreateOrderRequest(customerId, items));

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationRequest);
    }

    // --- get / delete ---

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsDto()
    {
        var order = GivenOrder(OrderStatus.Open, Line(5m, 3));

        var result = await _service.GetAsync(order.Id);

        result.Id.Should().Be(order.Id);
        result.Total.Should().Be(15m);
        result.Items.Single().Subtotal.Should().Be(15m);
    }

    [Fact]
    public async Task GetAsync_NonExistingId_ThrowsNotFound()
    {
        var act = () => _service.GetAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<NotFoundDomainException>()).Which.Code.Should().Be(ErrorCodes.OrderNotFound);
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_SoftDeletes()
    {
        var order = GivenOrder();

        await _service.DeleteAsync(order.Id);

        order.DeletedAt.Should().NotBeNull();
        order.DeletedBy.Should().Be("user-1");
        _orders.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingId_ThrowsNotFound()
    {
        var act = () => _service.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundDomainException>();
    }

    // --- items ---

    [Fact]
    public async Task AddItemAsync_OpenOrder_AddsItemAndRecalculatesTotal()
    {
        var order = GivenOrder(OrderStatus.Open, Line(10m, 1));

        var result = await _service.AddItemAsync(order.Id, Item(price: 2.50m, quantity: 4));

        order.Items.Should().HaveCount(2);
        result.Total.Should().Be(20m);
        order.UpdatedBy.Should().Be("user-1");
        _orders.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItemAsync_OrderAlreadyAtMaxItems_ThrowsValidation()
    {
        var order = GivenOrder(OrderStatus.Open, [.. Enumerable.Range(0, OrderService.MaxItemsPerOrder).Select(_ => Line())]);

        var act = () => _service.AddItemAsync(order.Id, Item());

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationRequest);
        _orders.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Canceled)]
    public async Task AddItemAsync_NotEditableOrder_ThrowsValidation(OrderStatus status)
    {
        var order = GivenOrder(status);

        var act = () => _service.AddItemAsync(order.Id, Item());

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationOrderNotEditable);
        _orders.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddItemAsync_NonExistingOrder_ThrowsNotFound()
    {
        var act = () => _service.AddItemAsync(Guid.NewGuid(), Item());

        await act.Should().ThrowAsync<NotFoundDomainException>();
    }

    [Fact]
    public async Task UpdateItemQuantityAsync_ExistingItem_UpdatesQuantityAndTotal()
    {
        var line = Line(10m, 1);
        var order = GivenOrder(OrderStatus.Open, line);

        var result = await _service.UpdateItemQuantityAsync(order.Id, line.Id, 3);

        line.Quantity.Should().Be(3);
        result.Total.Should().Be(30m);
        _orders.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateItemQuantityAsync_NonExistingItem_ThrowsNotFound()
    {
        var order = GivenOrder(OrderStatus.Open, Line());

        var act = () => _service.UpdateItemQuantityAsync(order.Id, Guid.NewGuid(), 2);

        (await act.Should().ThrowAsync<NotFoundDomainException>()).Which.Code.Should().Be(ErrorCodes.ItemNotFound);
    }

    [Fact]
    public async Task UpdateItemQuantityAsync_NotEditableOrder_ThrowsValidation()
    {
        var line = Line();
        var order = GivenOrder(OrderStatus.Confirmed, line);

        var act = () => _service.UpdateItemQuantityAsync(order.Id, line.Id, 2);

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationOrderNotEditable);
    }

    [Fact]
    public async Task RemoveItemAsync_ExistingItem_RemovesAndRecalculatesTotal()
    {
        var keep = Line(4m, 1);
        var remove = Line(10m, 1);
        var order = GivenOrder(OrderStatus.Open, keep, remove);

        var result = await _service.RemoveItemAsync(order.Id, remove.Id);

        order.Items.Should().ContainSingle().Which.Should().Be(keep);
        result.Total.Should().Be(4m);
    }

    [Fact]
    public async Task RemoveItemAsync_NonExistingItem_ThrowsNotFound()
    {
        var order = GivenOrder(OrderStatus.Open, Line());

        var act = () => _service.RemoveItemAsync(order.Id, Guid.NewGuid());

        (await act.Should().ThrowAsync<NotFoundDomainException>()).Which.Code.Should().Be(ErrorCodes.ItemNotFound);
    }

    [Fact]
    public async Task RemoveItemAsync_NotEditableOrder_ThrowsValidation()
    {
        var line = Line();
        var order = GivenOrder(OrderStatus.Canceled, line);

        var act = () => _service.RemoveItemAsync(order.Id, line.Id);

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationOrderNotEditable);
    }

    // --- confirm ---

    [Fact]
    public async Task ConfirmAsync_OpenOrderWithItems_ConfirmsAndPublishesEvent()
    {
        var order = GivenOrder(OrderStatus.Open, Line(10m, 2));
        GivenCustomer(order.CustomerId);
        OrderStatusChangedEvent? published = null;
        _publisher.Setup(p => p.PublishStatusChangedAsync(It.IsAny<OrderStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrderStatusChangedEvent, CancellationToken>((e, _) => published = e);

        var result = await _service.ConfirmAsync(order.Id);

        result.Status.Should().Be("CONFIRMED");
        order.UpdatedBy.Should().Be("user-1");
        published.Should().NotBeNull();
        published!.OldStatus.Should().Be("OPEN");
        published.NewStatus.Should().Be("CONFIRMED");
        published.TotalAmount.Should().Be(20m);
        published.CustomerName.Should().Be("Ana");
        _orders.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Canceled)]
    public async Task ConfirmAsync_NotOpenOrder_ThrowsInvalidTransition(OrderStatus status)
    {
        var order = GivenOrder(status, Line());

        var act = () => _service.ConfirmAsync(order.Id);

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationInvalidTransition);
        VerifyNothingPublished();
    }

    [Fact]
    public async Task ConfirmAsync_OrderWithoutItems_ThrowsEmptyOrder()
    {
        var order = GivenOrder();

        var act = () => _service.ConfirmAsync(order.Id);

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationOrderEmpty);
    }

    [Fact]
    public async Task ConfirmAsync_NonExistingOrder_ThrowsNotFound()
    {
        var act = () => _service.ConfirmAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundDomainException>();
    }

    [Fact]
    public async Task ConfirmAsync_CustomerWithoutEmail_DoesNotPublishEvent()
    {
        var order = GivenOrder(OrderStatus.Open, Line());
        GivenCustomer(order.CustomerId, email: " ");

        await _service.ConfirmAsync(order.Id);

        VerifyNothingPublished();
    }

    // --- cancel ---

    [Theory]
    [InlineData(OrderStatus.Open, "OPEN")]
    [InlineData(OrderStatus.Confirmed, "CONFIRMED")]
    public async Task CancelAsync_CancelableOrder_CancelsAndPublishesEvent(OrderStatus from, string expectedOld)
    {
        var order = GivenOrder(from, Line());
        GivenCustomer(order.CustomerId);
        OrderStatusChangedEvent? published = null;
        _publisher.Setup(p => p.PublishStatusChangedAsync(It.IsAny<OrderStatusChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OrderStatusChangedEvent, CancellationToken>((e, _) => published = e);

        var result = await _service.CancelAsync(order.Id);

        result.Status.Should().Be("CANCELED");
        published!.OldStatus.Should().Be(expectedOld);
        published.NewStatus.Should().Be("CANCELED");
    }

    [Fact]
    public async Task CancelAsync_AlreadyCanceled_ThrowsValidation()
    {
        var order = GivenOrder(OrderStatus.Canceled);

        var act = () => _service.CancelAsync(order.Id);

        (await act.Should().ThrowAsync<ValidationDomainException>()).Which.Code.Should().Be(ErrorCodes.ValidationAlreadyCanceled);
    }

    [Fact]
    public async Task CancelAsync_NonExistingOrder_ThrowsNotFound()
    {
        var act = () => _service.CancelAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundDomainException>();
    }

    // --- search ---

    [Fact]
    public async Task SearchAsync_MapsPagedEntitiesToDtos()
    {
        var order = new Order { Id = Guid.NewGuid(), Items = [Line(3m, 2)] };
        var request = new SearchRequest();
        _orders.Setup(r => r.SearchAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Order>([order], 1, 0, 20));

        var result = await _service.SearchAsync(request);

        result.Items.Single().Total.Should().Be(6m);
        result.Total.Should().Be(1);
    }
}
