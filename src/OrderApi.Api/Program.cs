using OrderApi.Api.Extensions;
using OrderApi.Api.Middleware;
using OrderApi.Application.Extensions;
using OrderApi.Infrastructure.Data;
using OrderApi.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// CORS fails the startup when no origin is configured outside Development: never silently open.
var allowedOrigins = CorsOriginsValidator.GetAllowedOrigins(builder.Configuration, builder.Environment.IsDevelopment());

// Generous but finite body limit: the body is read into memory before any validation runs.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApiServices();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddTrustedForwardedHeaders(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length == 0)
    {
        policy.AllowAnyOrigin();
    }
    else
    {
        policy.WithOrigins(allowedOrigins);
    }

    policy.AllowAnyHeader().AllowAnyMethod();
}));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddApiDocumentation();
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// No-op unless ForwardedHeaders:KnownProxies is configured (see ForwardedHeadersExtensions).
app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Swagger is only exposed in development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors();

// Authentication runs before the rate limiter so authenticated callers are partitioned by subject.
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

await app.RunAsync();

public partial class Program;
