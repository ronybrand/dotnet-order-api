using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AwesomeAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderApi.Api.Extensions;
using OrderApi.Api.Filters;
using OrderApi.Api.Middleware;
using OrderApi.Api.Observability;
using OrderApi.Api.Serialization;
using OrderApi.Api.Services;
using OrderApi.Application.DTOs;
using OrderApi.Domain.Common;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;

namespace OrderApi.Test.Api;

public class GlobalExceptionHandlerMiddlewareTests
{
    private static async Task<(int Status, JsonElement Body)> Run(Exception exception)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "req-1" };
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionHandlerMiddleware(_ => throw exception, NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, body.RootElement.Clone());
    }

    [Theory]
    [InlineData(typeof(NotFoundDomainException), 404)]
    [InlineData(typeof(ValidationDomainException), 400)]
    [InlineData(typeof(ConflictDomainException), 409)]
    public async Task InvokeAsync_DomainException_MapsToHttpStatusAndErrorBody(Type type, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(type, "boom", "CODE-1", null)!;

        var (status, body) = await Run(exception);

        status.Should().Be(expectedStatus);
        body.GetProperty("message").GetString().Should().Be("boom");
        body.GetProperty("code").GetString().Should().Be("CODE-1");
        body.GetProperty("request_id").GetString().Should().Be("req-1");
    }

    [Fact]
    public async Task InvokeAsync_ConcurrencyException_Returns409ConcurrentModification()
    {
        var (status, body) = await Run(new DbUpdateConcurrencyException("stale"));

        status.Should().Be(409);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.ConflictConcurrentModification);
    }

    [Fact]
    public async Task InvokeAsync_UnexpectedException_Returns500WithoutLeakingDetails()
    {
        var (status, body) = await Run(new InvalidOperationException("secret connection string"));

        status.Should().Be(500);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.Internal);
        body.ToString().Should().NotContain("secret");
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var context = new DefaultHttpContext();
        var middleware = new GlobalExceptionHandlerMiddleware(c => { c.Response.StatusCode = 204; return Task.CompletedTask; }, NullLogger<GlobalExceptionHandlerMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(204);
    }
}

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SetsBaselineSecurityHeaders()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["X-Frame-Options"].ToString().Should().Be("DENY");
        headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        headers["Content-Security-Policy"].ToString().Should().Be("default-src 'self'");
        headers["Strict-Transport-Security"].ToString().Should().Be("max-age=31536000; includeSubDomains");
        headers["Permissions-Policy"].ToString().Should().Be("geolocation=(), camera=(), microphone=()");
    }
}

public class CorrelationIdMiddlewareTests
{
    private static async Task<DefaultHttpContext> Run(string? requestId)
    {
        var context = new DefaultHttpContext();
        if (requestId is not null)
        {
            context.Request.Headers["X-Request-Id"] = requestId;
        }

        await new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance).InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task InvokeAsync_WithUuidHeader_ReusesIdAndEchoesIt()
    {
        var id = Guid.NewGuid().ToString();

        var context = await Run(id);

        context.TraceIdentifier.Should().Be(id);
        context.Response.Headers["X-Request-Id"].ToString().Should().Be(id);
    }

    [Fact]
    public async Task InvokeAsync_WithUppercaseUuid_NormalizesToLowercase()
    {
        var id = Guid.NewGuid();

        var context = await Run(id.ToString().ToUpperInvariant());

        context.TraceIdentifier.Should().Be(id.ToString());
    }

    [Fact]
    public async Task InvokeAsync_WithoutHeader_GeneratesUuid()
    {
        var context = await Run(null);

        Guid.TryParse(context.TraceIdentifier, out _).Should().BeTrue();
        context.Response.Headers["X-Request-Id"].ToString().Should().Be(context.TraceIdentifier);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("trace-123")]
    [InlineData("3f2a9c1e-0000-4000-8000-000000000001\r\nX-Injected: 1")]
    [InlineData("3f2a9c1e-0000-4000-8000-000000000001\nfake log line")]
    [InlineData("{3f2a9c1e-0000-4000-8000-000000000001}")]
    [InlineData("3f2a9c1e00004000800000000000000a")]
    [InlineData("")]
    public async Task InvokeAsync_WithNonUuidHeader_IsDiscardedAndReplacedByNewUuid(string requestId)
    {
        var context = await Run(requestId);

        context.TraceIdentifier.Should().NotBe(requestId);
        Guid.TryParse(context.TraceIdentifier, out _).Should().BeTrue();
        context.Response.Headers["X-Request-Id"].ToString().Should().Be(context.TraceIdentifier);
    }
}

public class CorsOriginsValidatorTests
{
    private static IConfiguration Config(string? origins) =>
        new ConfigurationBuilder().AddInMemoryCollection(origins is null ? [] : [new("Cors:AllowedOrigins", origins)]).Build();

    [Fact]
    public void GetAllowedOrigins_Production_EmptyList_FailsFast()
    {
        var act = () => CorsOriginsValidator.GetAllowedOrigins(Config(null), isProduction: true);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetAllowedOrigins_Production_WithOrigins_ReturnsThem()
    {
        CorsOriginsValidator.GetAllowedOrigins(Config("https://a.com,https://b.com"), isProduction: true)
            .Should().Equal("https://a.com", "https://b.com");
    }

    [Fact]
    public void GetAllowedOrigins_Development_EmptyList_IsAllowed()
    {
        CorsOriginsValidator.GetAllowedOrigins(Config(null), isProduction: false).Should().BeEmpty();
    }
}

public class SensitiveDataTests
{
    private sealed class Sample
    {
        public string Name { get; set; } = "public";
        [Sensitive] public string Secret { get; set; } = "s3cr3t";
        [Sensitive] public string? Empty { get; set; }
    }

    [Fact]
    public void Mask_RedactsSensitiveProperties_KeepsTheRest()
    {
        var masked = SensitiveMasker.Mask(new Sample());

        masked["Name"].Should().Be("public");
        masked["Secret"].Should().Be(SensitiveMasker.Redacted);
        masked["Empty"].Should().BeNull();
    }

    [Fact]
    public void Mask_Customer_RedactsPii()
    {
        var masked = SensitiveMasker.Mask(new Customer { TaxId = "123", PassportNumber = "AB123456", Email = "a@b.com", Name = "Ana" });

        masked["TaxId"].Should().Be(SensitiveMasker.Redacted);
        masked["PassportNumber"].Should().Be(SensitiveMasker.Redacted);
        masked["Email"].Should().Be(SensitiveMasker.Redacted);
        masked["Name"].Should().Be("Ana");
    }

    [Fact]
    public void JsonModifier_SensitiveProperty_IsNeverSerialized()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SensitiveJsonModifier.Modify } },
        };

        var json = JsonSerializer.Serialize(new Sample(), options);

        json.Should().Contain("public").And.NotContain("s3cr3t").And.NotContain("Secret");
    }
}

public class ValidationFilterTests
{
    private sealed record Payload(string Name);

    private sealed class PayloadValidator : AbstractValidator<Payload>
    {
        public PayloadValidator() => RuleFor(p => p.Name).NotEmpty();
    }

    private static ActionExecutingContext NewContext(object? argument, bool withValidator)
    {
        var services = new ServiceCollection();
        if (withValidator)
        {
            services.AddSingleton<IValidator<Payload>, PayloadValidator>();
        }

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(), TraceIdentifier = "req-9" };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?> { ["payload"] = argument }, new object());
    }

    [Fact]
    public async Task OnActionExecutionAsync_InvalidArgument_ShortCircuitsWith400AndNeverCallsAction()
    {
        var context = NewContext(new Payload(""), withValidator: true);
        var called = false;

        await new ValidationFilter().OnActionExecutionAsync(context, () => { called = true; return Task.FromResult<ActionExecutedContext>(null!); });

        called.Should().BeFalse();
        var result = context.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var error = result.Value.Should().BeOfType<ErrorResponse>().Subject;
        error.Code.Should().Be(ErrorCodes.ValidationRequest);
        error.RequestId.Should().Be("req-9");
    }

    [Fact]
    public async Task OnActionExecutionAsync_ValidArgument_CallsAction()
    {
        var context = NewContext(new Payload("ok"), withValidator: true);
        var called = false;

        await new ValidationFilter().OnActionExecutionAsync(context, () => { called = true; return Task.FromResult<ActionExecutedContext>(null!); });

        called.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_NoValidatorRegistered_CallsAction()
    {
        var context = NewContext(new Payload(""), withValidator: false);
        var called = false;

        await new ValidationFilter().OnActionExecutionAsync(context, () => { called = true; return Task.FromResult<ActionExecutedContext>(null!); });

        called.Should().BeTrue();
    }
}

public class CurrentUserAccessorTests
{
    private static CurrentUserAccessor For(HttpContext? context) =>
        new(new HttpContextAccessor { HttpContext = context });

    [Fact]
    public void UserName_AuthenticatedUser_ReturnsSubjectClaim()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-42")], "test")),
        };

        For(context).UserName.Should().Be("user-42");
    }

    [Fact]
    public void UserName_NoUser_FallsBackToSystem()
    {
        For(null).UserName.Should().Be("system");
        For(new DefaultHttpContext()).UserName.Should().Be("system");
    }
}
