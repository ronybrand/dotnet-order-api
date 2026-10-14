using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OrderApi.TestSupport;

namespace OrderApi.Test.Api;

/// <summary>
/// Boots the real API pipeline (auth, rate limit, filters, serialization) without a database, so
/// the HTTP contract can be tested without Docker. Only requests rejected before reaching a
/// service (401, 403, 400) are valid here: the connection string points nowhere on purpose.
/// Tests that need real persistence live in OrderApi.IntegrationTest.
/// </summary>
public sealed class HostlessApiFactory : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly string _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jwt-public-{Guid.NewGuid():N}.pem");

    public HostlessApiFactory()
    {
        File.WriteAllText(_publicKeyPath, _rsa.ExportSubjectPublicKeyInfoPem());

        // Environment variables are visible to the configuration read at the top of Program.cs.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Host=127.0.0.1;Port=1;Database=unused;Timeout=1");
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestTokens.Audience);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestTokens.Issuer);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
    }

    public RSA SigningKey => _rsa;

    public byte[] PublicKeyBytes => _rsa.ExportSubjectPublicKeyInfo();

    public HttpClient ClientFor(string[]? roles = null, string? subject = "user-1")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(_rsa, subject, roles ?? ["user"]));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _rsa.Dispose();
            if (File.Exists(_publicKeyPath))
            {
                File.Delete(_publicKeyPath);
            }
        }
    }
}
