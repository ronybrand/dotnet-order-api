using System.Text.Json;
using System.Threading.RateLimiting;
using OrderApi.Api.Serialization;
using OrderApi.Application.DTOs;
using OrderApi.Domain.Exceptions;

namespace OrderApi.Api.Extensions;

public static class RateLimitingExtensions
{
    public const string WritePolicy = "customer-write";
    public const string OrderWritePolicy = "order-write";

    private const int GlobalPermitLimit = 100;

    // Stricter than the global default: customer writes touch PII and are costlier than reads.
    private const int WritePermitLimit = 10;

    // Every order transition opens a broker connection to publish the notification, so order
    // writes get their own ceiling, looser than customer writes but well under the global one.
    private const int OrderWritePermitLimit = 30;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // The health probe must never be starved by traffic sharing its partition (anonymous callers
            // behind a proxy share one address), otherwise the orchestrator would restart a healthy pod.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                context => context.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : Window(context, "global", GlobalPermitLimit));
            options.AddPolicy(WritePolicy, context => Window(context, WritePolicy, WritePermitLimit));
            options.AddPolicy(OrderWritePolicy, context => Window(context, OrderWritePolicy, OrderWritePermitLimit));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                var error = new ErrorResponse(
                    "Rate limit exceeded",
                    ErrorCodes.RateLimit,
                    new Dictionary<string, object?>(),
                    context.HttpContext.TraceIdentifier);
                await JsonSerializer.SerializeAsync(context.HttpContext.Response.Body, error, ApiJson.ErrorOptions, cancellationToken);
            };
        });

    // Authenticated callers are partitioned by subject, anonymous ones by remote address.
    private static RateLimitPartition<string> Window(HttpContext context, string scope, int permitLimit)
    {
        var caller = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst("sub")?.Value
            : null;
        caller ??= context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{scope}:{caller}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    }
}
