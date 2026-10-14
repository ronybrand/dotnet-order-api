using OrderApi.Application.Common;
using OrderApi.Application.DTOs;
using OrderApi.Application.Events;
using OrderApi.Application.Services.Interfaces;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Domain.Interfaces;

namespace OrderApi.Application.Services;

public sealed class OrderService(
    IOrderRepository orders,
    ICustomerRepository customers,
    ICurrentUserAccessor currentUser,
    IOrderEventPublisher eventPublisher,
    TimeProvider timeProvider) : IOrderService
{
    public const int MaxItemsPerOrder = 200;

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, cancellationToken)
            ?? throw new ValidationDomainException(
                $"Customer {request.CustomerId} not found",
                ErrorCodes.ValidationCustomerNotFound,
                new Dictionary<string, object?> { ["customer_id"] = request.CustomerId });

        var items = request.Items ?? [];
        AssertItemCountWithinLimit(items.Count);

        var now = timeProvider.GetUtcNow();
        var order = new Order
        {
            CustomerId = customer.Id,
            Status = OrderStatus.Open,
            Items = [.. items.Select(MapToItem)],
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = currentUser.UserName,
            UpdatedBy = currentUser.UserName,
        };
        customers.MarkReferenced(customer);
        await orders.AddAsync(order, cancellationToken);
        return MapToDto(order);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        MapToDto(await GetActiveAsync(id, cancellationToken));

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetActiveAsync(id, cancellationToken);
        order.DeletedAt = timeProvider.GetUtcNow();
        order.DeletedBy = currentUser.UserName;
        await orders.UpdateAsync(order, cancellationToken);
    }

    public async Task<OrderDto> AddItemAsync(Guid orderId, ItemRequest request, CancellationToken cancellationToken = default)
    {
        var order = await GetEditableAsync(orderId, cancellationToken);
        AssertItemCountWithinLimit(order.Items.Count + 1);
        order.Items.Add(MapToItem(request));
        return await SaveItemChangeAsync(order, cancellationToken);
    }

    public async Task<OrderDto> UpdateItemQuantityAsync(Guid orderId, Guid itemId, int quantity, CancellationToken cancellationToken = default)
    {
        var order = await GetEditableAsync(orderId, cancellationToken);
        FindItem(order, itemId).Quantity = quantity;
        return await SaveItemChangeAsync(order, cancellationToken);
    }

    public async Task<OrderDto> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken cancellationToken = default)
    {
        var order = await GetEditableAsync(orderId, cancellationToken);
        order.Items.Remove(FindItem(order, itemId));
        return await SaveItemChangeAsync(order, cancellationToken);
    }

    public async Task<OrderDto> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetActiveAsync(id, cancellationToken);
        if (order.Status != OrderStatus.Open)
        {
            throw new ValidationDomainException(
                $"Cannot confirm order {id} from status {order.Status.ToCode()}",
                ErrorCodes.ValidationInvalidTransition,
                new Dictionary<string, object?> { ["id"] = id, ["status"] = order.Status.ToCode() });
        }

        if (order.Items is not { Count: > 0 })
        {
            throw new ValidationDomainException(
                $"Order {id} has no items and cannot be confirmed",
                ErrorCodes.ValidationOrderEmpty,
                new Dictionary<string, object?> { ["id"] = id });
        }

        return await TransitionAsync(order, OrderStatus.Confirmed, cancellationToken);
    }

    public async Task<OrderDto> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await GetActiveAsync(id, cancellationToken);
        if (order.IsCanceled)
        {
            throw new ValidationDomainException(
                $"Order {id} is already canceled",
                ErrorCodes.ValidationAlreadyCanceled,
                new Dictionary<string, object?> { ["id"] = id, ["status"] = order.Status.ToCode() });
        }

        return await TransitionAsync(order, OrderStatus.Canceled, cancellationToken);
    }

    public async Task<PagedResult<OrderDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        (await orders.SearchAsync(request, cancellationToken)).Map(MapToDto);

    private async Task<Order> GetActiveAsync(Guid id, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundDomainException(
            $"Order {id} not found",
            ErrorCodes.OrderNotFound,
            new Dictionary<string, object?> { ["id"] = id });

    private async Task<Order> GetEditableAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetActiveAsync(id, cancellationToken);
        if (order.IsEditable)
        {
            return order;
        }

        throw new ValidationDomainException(
            $"Order {id} is not editable in status {order.Status.ToCode()}",
            ErrorCodes.ValidationOrderNotEditable,
            new Dictionary<string, object?> { ["id"] = id, ["status"] = order.Status.ToCode() });
    }

    private static void AssertItemCountWithinLimit(int count)
    {
        if (count > MaxItemsPerOrder)
        {
            throw new ValidationDomainException(
                $"An order can have at most {MaxItemsPerOrder} items",
                ErrorCodes.ValidationRequest,
                new Dictionary<string, object?> { ["field"] = "items", ["max"] = MaxItemsPerOrder });
        }
    }

    private static OrderItem FindItem(Order order, Guid itemId) =>
        order.Items.FirstOrDefault(item => item.Id == itemId)
        ?? throw new NotFoundDomainException(
            $"Item {itemId} not found in order {order.Id}",
            ErrorCodes.ItemNotFound,
            new Dictionary<string, object?> { ["order_id"] = order.Id, ["item_id"] = itemId });

    private async Task<OrderDto> SaveItemChangeAsync(Order order, CancellationToken cancellationToken)
    {
        // Touching the audit fields forces an UPDATE of the order row even when only a child
        // changed, so the optimistic concurrency token also covers item mutations.
        Touch(order);
        await orders.UpdateAsync(order, cancellationToken);
        return MapToDto(order);
    }

    private async Task<OrderDto> TransitionAsync(Order order, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        var oldStatus = order.Status;
        order.Status = newStatus;
        Touch(order);
        await orders.UpdateAsync(order, cancellationToken);

        await PublishStatusChangedAsync(order, oldStatus, cancellationToken);
        return MapToDto(order);
    }

    private async Task PublishStatusChangedAsync(Order order, OrderStatus oldStatus, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(order.CustomerId, cancellationToken);
        if (customer is null || string.IsNullOrWhiteSpace(customer.Email))
        {
            return;
        }

        await eventPublisher.PublishStatusChangedAsync(
            new OrderStatusChangedEvent(
                order.Id,
                customer.Email,
                customer.Name,
                oldStatus.ToCode(),
                order.Status.ToCode(),
                order.Total,
                timeProvider.GetUtcNow()),
            cancellationToken);
    }

    private void Touch(Order order)
    {
        order.UpdatedAt = timeProvider.GetUtcNow();
        order.UpdatedBy = currentUser.UserName;
    }

    private static OrderItem MapToItem(ItemRequest request) => new()
    {
        Description = request.Description,
        UnitPrice = request.UnitPrice,
        Quantity = request.Quantity,
    };

    private static OrderDto MapToDto(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status.ToCode(),
        order.Version,
        [.. order.Items.Select(item => new OrderItemDto(
            item.Id, item.OrderId, item.Description, item.UnitPrice, item.Quantity, item.Subtotal))],
        order.Total);
}
