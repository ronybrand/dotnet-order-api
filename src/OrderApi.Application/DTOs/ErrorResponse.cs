namespace OrderApi.Application.DTOs;

public sealed record ErrorResponse(
    string Message,
    string Code,
    IDictionary<string, object?>? Params = null,
    string? RequestId = null);
