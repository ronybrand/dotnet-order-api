using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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
