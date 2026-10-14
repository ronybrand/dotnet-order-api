using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OrderApi.TestSupport;

/// <summary>Builds JWTs for tests: valid ones signed with RS256 and the malicious variants the API must reject.</summary>
public static class TestTokens
{
    public const string Audience = "test-audience";
    public const string Issuer = "test-issuer";

    /// <summary>Valid RS256 token. A null subject omits the <c>sub</c> claim; a negative lifetime yields an expired token.</summary>
    public static string Create(
        RSA key,
        string? subject,
        string[]? roles = null,
        string? audience = null,
        string? issuer = null,
        TimeSpan? lifetime = null) =>
        Write(subject, roles, audience, issuer, lifetime, new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256));

    /// <summary>
    /// Algorithm confusion attack: HS256 token signed using the (public) RSA key bytes as the HMAC secret.
    /// A verifier that trusts the algorithm in the header would accept it.
    /// </summary>
    public static string CreateHmacForged(byte[] publicKeyBytes, string subject, string[]? roles = null) =>
        Write(subject, roles, null, null, null, new SigningCredentials(new SymmetricSecurityKey(publicKeyBytes), SecurityAlgorithms.HmacSha256));

    /// <summary>Token with <c>alg: none</c> and no signature.</summary>
    public static string CreateUnsigned(string subject, string[]? roles = null) =>
        Write(subject, roles, null, null, null, signingCredentials: null);

    private static string Write(
        string? subject,
        string[]? roles,
        string? audience,
        string? issuer,
        TimeSpan? lifetime,
        SigningCredentials? signingCredentials)
    {
        var claims = new List<Claim>();
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        claims.AddRange((roles ?? []).Select(role => new Claim("roles", role)));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer ?? Issuer,
            audience ?? Audience,
            claims,
            notBefore: now.AddMinutes(-5),
            expires: now.Add(lifetime ?? TimeSpan.FromMinutes(10)),
            signingCredentials: signingCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
