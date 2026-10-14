namespace OrderApi.Api.Middleware;

/// <summary>
/// Propagates (or generates) X-Request-Id and attaches it to every log entry of the request.
/// The id is client controlled, so only a canonical UUID is accepted; anything else is discarded
/// and replaced by a new UUID. That keeps log forging (CR/LF injection) and header injection out,
/// since the value is written to logs, to the response header and to error bodies.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        // The "D" format is the canonical 8-4-4-4-12 form: no braces, no missing hyphens, no whitespace.
        var requestId = Guid.TryParseExact(context.Request.Headers[HeaderName].FirstOrDefault(), "D", out var supplied)
            ? supplied
            : Guid.NewGuid();

        // Always rebuilt from the parsed Guid (lowercase), never from the raw header text.
        var id = requestId.ToString();
        context.TraceIdentifier = id;
        context.Response.Headers[HeaderName] = id;
        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = id }))
        {
            await next(context);
        }
    }
}
