using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OrderApi.IntegrationTest;

public static class TestTokens
{
    public const string Audience = "test-audience";
    public const string Issuer = "test-issuer";

    public static string Create(RSA key, string subject, string[]? roles = null, string? audience = null, string? issuer = null, TimeSpan? lifetime = null)
    {
        var claims = new List<Claim> { new("sub", subject) };
        claims.AddRange((roles ?? []).Select(role => new Claim("roles", role)));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer ?? Issuer,
            audience ?? Audience,
            claims,
            notBefore: now.AddMinutes(-5),
            expires: now.Add(lifetime ?? TimeSpan.FromMinutes(10)),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
