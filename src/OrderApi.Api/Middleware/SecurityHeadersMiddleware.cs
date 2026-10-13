namespace OrderApi.Api.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'self'";
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
        return next(context);
    }
}
