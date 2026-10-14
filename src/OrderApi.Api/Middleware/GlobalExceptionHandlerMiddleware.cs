using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderApi.Api.Serialization;
using OrderApi.Application.DTOs;
using OrderApi.Domain.Exceptions;

namespace OrderApi.Api.Middleware;

public sealed partial class GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            await WriteAsync(context, MapStatus(ex), new ErrorResponse(ex.Message, ex.Code, ex.Parameters, context.TraceIdentifier));
        }
        catch (DbUpdateConcurrencyException)
        {
            await WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                new ErrorResponse("Record was modified by another request", ErrorCodes.ConflictConcurrentModification, null, context.TraceIdentifier));
        }
        catch (BadHttpRequestException ex)
        {
            // Kestrel signals a client error (oversized body, malformed request) with its own status:
            // answer it as such instead of logging an error and replying 500.
            await WriteAsync(
                context,
                ex.StatusCode,
                new ErrorResponse(
                    ex.StatusCode == StatusCodes.Status413PayloadTooLarge ? "Request body too large" : "Invalid request",
                    ErrorCodes.ValidationRequest,
                    null,
                    context.TraceIdentifier));
        }
        catch (Exception ex)
        {
            // Full detail only in the log; the client always receives a generic message.
            LogUnhandled(logger, ex, context.Request.Path, context.Request.Method);
            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                new ErrorResponse("Internal server error", ErrorCodes.Internal, null, context.TraceIdentifier));
        }
    }

    internal static Task WriteAsync(HttpContext context, int status, ErrorResponse error)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            error with { Params = error.Params ?? new Dictionary<string, object?>() },
            ApiJson.ErrorOptions,
            context.RequestAborted);
    }

    private static int MapStatus(DomainException exception) => exception switch
    {
        NotFoundDomainException => StatusCodes.Status404NotFound,
        ConflictDomainException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "unhandled_exception: path={Path} method={Method}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, PathString path, string method);
}
