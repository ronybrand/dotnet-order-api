using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OrderApi.Api.Middleware;
using OrderApi.Application.DTOs;
using OrderApi.Domain.Exceptions;

namespace OrderApi.Api.Extensions;

public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// This service only verifies tokens (RS256) and never issues them: Jwt:PublicKeyPath must
    /// point at the public half of the issuer's key pair, so leaking it grants no ability to mint tokens.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var keyPath = configuration["Jwt:PublicKeyPath"]
            ?? throw new InvalidOperationException("Jwt:PublicKeyPath must be set (RS256 public key in PEM format)");
        var audience = configuration["Jwt:Audience"] ?? "dotnet-order-api";
        var issuer = configuration["Jwt:Issuer"] ?? "dotnet-order-api";

        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keyPath));
        var signingKey = new RsaSecurityKey(rsa);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                // A token without a subject cannot be attributed to anyone (audit columns, rate limit
                // partition), so it is rejected instead of silently falling back to a shared identity.
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (string.IsNullOrWhiteSpace(context.Principal?.FindFirst("sub")?.Value))
                        {
                            context.Fail("Token has no subject");
                        }

                        return Task.CompletedTask;
                    },
                    // 401 and 403 carry the same error contract as every other failure instead of an empty body.
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = context.AuthenticateFailure is null
                            ? "Bearer"
                            : "Bearer error=\"invalid_token\"";
                        return GlobalExceptionHandlerMiddleware.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            new ErrorResponse("Authentication required", ErrorCodes.AuthUnauthenticated, null, context.HttpContext.TraceIdentifier));
                    },
                    OnForbidden = context => GlobalExceptionHandlerMiddleware.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        new ErrorResponse("Access denied", ErrorCodes.AuthForbidden, null, context.HttpContext.TraceIdentifier)),
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    IssuerSigningKey = signingKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                    RoleClaimType = "roles",
                };
            });
        services.AddAuthorization();
        return services;
    }
}
