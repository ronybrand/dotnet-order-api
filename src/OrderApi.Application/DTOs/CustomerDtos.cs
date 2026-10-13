using OrderApi.Domain.Common;

namespace OrderApi.Application.DTOs;

// TaxId, PassportNumber and Email are PII and deliberately absent from the response DTO.
public sealed record CustomerDto(Guid Id, string Name);

public sealed record CreateCustomerRequest(
    string Name,
    [property: Sensitive] string TaxId,
    [property: Sensitive] string? PassportNumber,
    [property: Sensitive] string Email);

public sealed record UpdateCustomerRequest(
    string Name,
    [property: Sensitive] string TaxId,
    [property: Sensitive] string? PassportNumber,
    [property: Sensitive] string Email);
