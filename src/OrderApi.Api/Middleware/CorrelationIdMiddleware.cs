namespace OrderApi.Api.Middleware;

/// <summary>Propagates (or generates) X-Request-Id and attaches it to every log entry of the request.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = Guid.NewGuid().ToString();
        }

        context.TraceIdentifier = requestId;
        context.Response.Headers[HeaderName] = requestId;
        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        {
            await next(context);
        }
    }
}
