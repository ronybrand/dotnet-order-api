using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrderApi.Api.Extensions;
using OrderApi.Api.Security;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services.Interfaces;
using OrderApi.Domain.Common.Search;

namespace OrderApi.Api.Controllers;

[ApiController]
[Authorize]
[Route("customers")]
[Produces("application/json")]
public class CustomersController(ICustomerService customers) : ControllerBase
{
    /// <summary>Creates a customer, validating tax_id/passport_number uniqueness.</summary>
    /// <response code="201">Customer created.</response>
    /// <response code="400">VALIDATION-01 invalid request body.</response>
    /// <response code="409">CONFLICT-01 tax_id exists / CONFLICT-02 passport_number exists.</response>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await customers.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    /// <summary>Searches customers using page/size/sort query parameters.</summary>
    /// <response code="200">Page of customers.</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchByQuery([FromQuery] SearchQuery query, CancellationToken cancellationToken) =>
        Ok(await customers.SearchAsync(query.ToSearchRequest(), cancellationToken));

    /// <summary>Searches customers accepting the full filters payload in the request body.</summary>
    /// <response code="200">Page of customers.</response>
    [HttpPost("search")]
    [ProducesResponseType(typeof(PagedResult<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchByBody([FromBody] SearchRequest request, CancellationToken cancellationToken) =>
        Ok(await customers.SearchAsync(request, cancellationToken));

    /// <summary>Retrieves a customer by id.</summary>
    /// <response code="200">The customer.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-01.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await customers.GetAsync(id, cancellationToken));

    /// <summary>Updates a customer, validating uniqueness excluding the record itself.</summary>
    /// <response code="200">Customer updated.</response>
    /// <response code="400">VALIDATION-01 invalid request body.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-01.</response>
    /// <response code="409">CONFLICT-01 / CONFLICT-02.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        Ok(await customers.UpdateAsync(id, request, cancellationToken));

    /// <summary>Soft-deletes a customer. Blocked while it still has non-deleted orders.</summary>
    /// <response code="204">Customer deleted.</response>
    /// <response code="404">RESOURCE-NOT-FOUND-01.</response>
    /// <response code="409">CONFLICT-03 customer has orders.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await customers.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
