using OrderApi.TestSupport;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace OrderApi.IntegrationTest;

public abstract class ApiTestBase(ApiWebApplicationFactory factory) : IAsyncLifetime
{
    private static int _counter;

    protected static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    protected ApiWebApplicationFactory Factory { get; } = factory;

    public Task InitializeAsync() => Factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Client authenticated with a real signed token. Each call uses a unique subject unless given.</summary>
    protected HttpClient Client(string[]? roles = null, string? subject = null, bool authenticated = true)
    {
        var client = Factory.CreateClient();
        if (authenticated)
        {
            var token = TestTokens.Create(Factory.SigningKey, subject ?? $"user-{Guid.NewGuid():N}", roles ?? ["user"]);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    protected HttpClient Admin() => Client(["admin"]);

    protected static string NewTaxId() => $"TAX-{Interlocked.Increment(ref _counter):D6}";

    protected static Task<HttpResponseMessage> Post(HttpClient client, string url, object? body) =>
        client.PostAsJsonAsync(url, body, Json);

    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement.Clone();

    protected async Task<Guid> CreateCustomerAsync(string? taxId = null, string? passport = null, string name = "Ana", string email = "ana@example.com")
    {
        var response = await Post(Admin(), "/customers", new { name, tax_id = taxId ?? NewTaxId(), passport_number = passport, email });
        response.EnsureSuccessStatusCode();
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    protected async Task<JsonElement> CreateOrderAsync(Guid customerId, params (string Description, decimal Price, int Quantity)[] items)
    {
        var response = await Post(Client(), "/orders", new
        {
            customer_id = customerId,
            items = items.Select(i => new { description = i.Description, unit_price = i.Price, quantity = i.Quantity }),
        });
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }
}
