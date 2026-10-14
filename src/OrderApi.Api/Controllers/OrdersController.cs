using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrderApi.Api.Extensions;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services.Interfaces;
using OrderApi.Domain.Common.Search;

namespace OrderApi.Api.Controllers;

[ApiController]
[Authorize]
[Route("orders")]
[Produces("application/json")]
public class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Creates an order for an existing customer with an initial list of items.</summary>
    /// <response code="201">Order created.</response>
    /// <response code="400">VALIDATION-01 invalid body / VALIDATION-07 customer not found.</response>
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Searches orders using page/size/sort query parameters.</summary>
    /// <response code="200">Page of orders.</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchByQuery([FromQuery] SearchQuery query, CancellationToken cancellationToken) =>
        Ok(await orders.SearchAsync(query.ToSearchRequest(), cancellationToken));

    /// <summary>Searches orders accepting the full filters payload in the request body.</summary>
    /// <response code="200">Page of orders.</response>
    [HttpPost("search")]
    [ProducesResponseType(typeof(PagedResult<OrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchByBody([FromBody] SearchRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.SearchAsync(request, cancellationToken));

    /// <summary>Retrieves an order by id, including items and calculated total.</summary>
    /// <response code="200">The order.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.GetAsync(id, cancellationToken));

    /// <summary>Soft-deletes an order.</summary>
    /// <response code="204">Order deleted.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02.</response>
    [HttpDelete("{id}")]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await orders.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Adds a line item. Requires the order to be OPEN.</summary>
    /// <response code="200">Updated order.</response>
    /// <response code="400">VALIDATION-01 / VALIDATION-02 order is not editable.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02.</response>
    /// <response code="409">CONFLICT-00 concurrent modification.</response>
    [HttpPost("{id}/items")]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] ItemRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.AddItemAsync(id, request, cancellationToken));

    /// <summary>Updates the quantity of a line item. Requires the order to be OPEN.</summary>
    /// <response code="200">Updated order.</response>
    /// <response code="400">VALIDATION-01 / VALIDATION-02 order is not editable.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02 order / RESOURCE-NOT-FOUND-03 item.</response>
    /// <response code="409">CONFLICT-00 concurrent modification.</response>
    [HttpPatch("{orderId}/items/{itemId}")]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateItemQuantity(
        Guid orderId, Guid itemId, [FromBody] UpdateItemQuantityRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.UpdateItemQuantityAsync(orderId, itemId, request.Quantity, cancellationToken));

    /// <summary>Removes a line item. Requires the order to be OPEN.</summary>
    /// <response code="200">Updated order.</response>
    /// <response code="400">VALIDATION-02 order is not editable.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02 order / RESOURCE-NOT-FOUND-03 item.</response>
    /// <response code="409">CONFLICT-00 concurrent modification.</response>
    [HttpDelete("{orderId}/items/{itemId}")]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(Guid orderId, Guid itemId, CancellationToken cancellationToken) =>
        Ok(await orders.RemoveItemAsync(orderId, itemId, cancellationToken));

    /// <summary>Transitions an order from OPEN to CONFIRMED. Fails if empty or not OPEN.</summary>
    /// <response code="200">Confirmed order.</response>
    /// <response code="400">VALIDATION-03 empty order / VALIDATION-04 invalid transition.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02.</response>
    /// <response code="409">CONFLICT-00 concurrent modification.</response>
    [HttpPost("{id}/confirm")]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.ConfirmAsync(id, cancellationToken));

    /// <summary>Transitions an order from OPEN or CONFIRMED to CANCELED.</summary>
    /// <response code="200">Canceled order.</response>
    /// <response code="400">VALIDATION-08 already canceled.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-02.</response>
    /// <response code="409">CONFLICT-00 concurrent modification.</response>
    [HttpPost("{id}/cancel")]
    [EnableRateLimiting(RateLimitingExtensions.OrderWritePolicy)]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.CancelAsync(id, cancellationToken));
}
