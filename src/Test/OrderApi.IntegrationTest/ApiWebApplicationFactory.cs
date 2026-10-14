using OrderApi.TestSupport;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OrderApi.Application.Events;
using Testcontainers.PostgreSql;

namespace OrderApi.IntegrationTest;

/// <summary>
/// Boots the real API against a real Postgres (Testcontainers) with real RS256 JWT validation.
/// The only host difference is the event publisher, replaced by an in-memory recorder
/// (no broker in tests); every production guard (rate limit, CORS, headers...) stays active.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private readonly RSA _rsa = RSA.Create(2048);
    private string? _publicKeyPath;

    public ConcurrentQueue<OrderStatusChangedEvent> PublishedEvents { get; } = new();

    public RSA SigningKey => _rsa;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _publicKeyPath = Path.Combine(Path.GetTempPath(), $"jwt-public-{Guid.NewGuid():N}.pem");
        await File.WriteAllTextAsync(_publicKeyPath, _rsa.ExportSubjectPublicKeyInfoPem());

        // Environment variables are visible to the configuration read at the top of Program.cs.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__PublicKeyPath", _publicKeyPath);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestTokens.Audience);
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestTokens.Issuer);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "true");

        // Creating a client boots the host, which applies the real migrations on startup;
        // the schema must exist before ResetAsync truncates any table.
        using var bootClient = CreateClient();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        _rsa.Dispose();
        if (_publicKeyPath is not null && File.Exists(_publicKeyPath))
        {
            File.Delete(_publicKeyPath);
        }
    }

    public async Task ResetAsync()
    {
        PublishedEvents.Clear();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("TRUNCATE order_items, orders, customers RESTART IDENTITY CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOrderEventPublisher>();
            services.AddSingleton<IOrderEventPublisher>(new RecordingPublisher(PublishedEvents));
        });
    }

    private sealed class RecordingPublisher(ConcurrentQueue<OrderStatusChangedEvent> events) : IOrderEventPublisher
    {
        public Task PublishStatusChangedAsync(OrderStatusChangedEvent @event, CancellationToken cancellationToken = default)
        {
            events.Enqueue(@event);
            return Task.CompletedTask;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiWebApplicationFactory>
{
    public const string Name = "api";
}
