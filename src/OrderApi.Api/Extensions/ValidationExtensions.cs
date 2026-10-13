using FluentValidation.Results;
using OrderApi.Api.Serialization;
using OrderApi.Application.DTOs;
using OrderApi.Domain.Exceptions;

namespace OrderApi.Api.Extensions;

public static class ValidationExtensions
{
    public static ErrorResponse ToValidationErrorResponse(this ValidationResult result, string requestId)
    {
        var first = result.Errors[0];
        return ValidationError(first.ErrorMessage, first.PropertyName, requestId);
    }

    public static ErrorResponse ValidationError(string message, string propertyPath, string requestId)
    {
        var last = propertyPath.Split('.', '[').Last().TrimEnd(']');
        return new ErrorResponse(
            message,
            ErrorCodes.ValidationRequest,
            new Dictionary<string, object?> { ["field"] = ApiJson.ToSnakeCase(last) },
            requestId);
    }
}
