using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace OrderApi.IntegrationTest;

[Collection(ApiCollection.Name)]
public class OrderControllerIntegrationTests(ApiWebApplicationFactory factory) : ApiTestBase(factory)
{
    private static Guid IdOf(JsonElement order) => order.GetProperty("id").GetGuid();

    [Fact]
    public async Task Create_WithItems_Returns201WithTotal()
    {
        var customerId = await CreateCustomerAsync();

        var order = await CreateOrderAsync(customerId, ("Pen", 2.50m, 2), ("Book", 10m, 1));

        order.GetProperty("status").GetString().Should().Be("OPEN");
        order.GetProperty("total").GetDecimal().Should().Be(15m);
        order.GetProperty("items").GetArrayLength().Should().Be(2);
        order.GetProperty("items")[0].GetProperty("subtotal").GetDecimal().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns400()
    {
        var response = await Post(Client(), "/orders", new { customer_id = Guid.NewGuid(), items = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-07");
    }

    [Fact]
    public async Task Create_InvalidItem_Returns400()
    {
        var customerId = await CreateCustomerAsync();

        var response = await Post(Client(), "/orders", new { customer_id = customerId, items = new[] { new { description = "x", unit_price = 1.234m, quantity = 1 } } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-01");
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var response = await Post(Client(authenticated: false), "/orders", new { customer_id = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ExistingOrder_Returns200()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 1m, 1));

        var response = await Client().GetAsync($"/orders/{IdOf(order)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_UnknownOrder_Returns404()
    {
        var response = await Client().GetAsync($"/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("RESOURCE-NOT-FOUND-02");
    }

    [Fact]
    public async Task Delete_ExistingOrder_SoftDeletes()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync());

        (await Client().DeleteAsync($"/orders/{IdOf(order)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client().GetAsync($"/orders/{IdOf(order)}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ItemFlow_AddUpdateRemove_RecalculatesTotal()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 2m, 1));
        var id = IdOf(order);
        var client = Client();

        var added = await ReadAsync(await Post(client, $"/orders/{id}/items", new { description = "Book", unit_price = 10m, quantity = 2 }));
        added.GetProperty("total").GetDecimal().Should().Be(22m);

        var bookId = added.GetProperty("items").EnumerateArray().First(i => i.GetProperty("description").GetString() == "Book").GetProperty("id").GetGuid();
        var updated = await ReadAsync(await client.PatchAsJsonAsync($"/orders/{id}/items/{bookId}", new { quantity = 3 }, Json));
        updated.GetProperty("total").GetDecimal().Should().Be(32m);

        var removed = await ReadAsync(await client.DeleteAsync($"/orders/{id}/items/{bookId}"));
        removed.GetProperty("total").GetDecimal().Should().Be(2m);
        removed.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task UpdateItem_UnknownItem_Returns404()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 2m, 1));

        var response = await Client().PatchAsJsonAsync($"/orders/{IdOf(order)}/items/{Guid.NewGuid()}", new { quantity = 3 }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("RESOURCE-NOT-FOUND-03");
    }

    [Fact]
    public async Task UpdateItem_InvalidQuantity_Returns400()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 2m, 1));
        var itemId = order.GetProperty("items")[0].GetProperty("id").GetGuid();

        var response = await Client().PatchAsJsonAsync($"/orders/{IdOf(order)}/items/{itemId}", new { quantity = 0 }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_OpenOrderWithItems_ConfirmsAndPublishesEvent()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(email: "buyer@example.com"), ("Pen", 2m, 3));

        var response = await Post(Client(), $"/orders/{IdOf(order)}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("status").GetString().Should().Be("CONFIRMED");
        var published = Factory.PublishedEvents.Should().ContainSingle().Subject;
        published.OldStatus.Should().Be("OPEN");
        published.NewStatus.Should().Be("CONFIRMED");
        published.TotalAmount.Should().Be(6m);
        published.CustomerEmail.Should().Be("buyer@example.com");
    }

    [Fact]
    public async Task Confirm_EmptyOrder_Returns400()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync());

        var response = await Post(Client(), $"/orders/{IdOf(order)}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-03");
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_Returns400InvalidTransition()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 2m, 1));
        await Post(Client(), $"/orders/{IdOf(order)}/confirm", null);

        var response = await Post(Client(), $"/orders/{IdOf(order)}/confirm", null);

        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-04");
    }

    [Fact]
    public async Task AddItem_ToConfirmedOrder_Returns400NotEditable()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 2m, 1));
        await Post(Client(), $"/orders/{IdOf(order)}/confirm", null);

        var response = await Post(Client(), $"/orders/{IdOf(order)}/items", new { description = "Late", unit_price = 1m, quantity = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-02");
    }

    [Fact]
    public async Task Cancel_ConfirmedOrder_CancelsAndSecondCancelFails()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Pen", 2m, 1));
        await Post(Client(), $"/orders/{IdOf(order)}/confirm", null);

        var first = await Post(Client(), $"/orders/{IdOf(order)}/cancel", null);
        var second = await Post(Client(), $"/orders/{IdOf(order)}/cancel", null);

        (await ReadAsync(first)).GetProperty("status").GetString().Should().Be("CANCELED");
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(second)).GetProperty("code").GetString().Should().Be("VALIDATION-08");
        Factory.PublishedEvents.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentAddItem_NeverLosesAcceptedWrites()
    {
        var order = await CreateOrderAsync(await CreateCustomerAsync(), ("Seed", 1m, 1));
        var id = IdOf(order);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Post(Client(), $"/orders/{id}/items", new { description = $"Item {i}", unit_price = 1m, quantity = 1 })));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        var accepted = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        accepted.Should().BeGreaterThan(0);
        var final = await ReadAsync(await Client().GetAsync($"/orders/{id}"));
        final.GetProperty("items").GetArrayLength().Should().Be(1 + accepted);
    }

    [Fact]
    public async Task Search_FilterByStatus_ReturnsMatchingOrdersWithItems()
    {
        var customerId = await CreateCustomerAsync();
        await CreateOrderAsync(customerId, ("A", 1m, 1));
        var confirmed = await CreateOrderAsync(customerId, ("B", 2m, 1));
        await Post(Client(), $"/orders/{IdOf(confirmed)}/confirm", null);

        var response = await Post(Client(), "/orders/search", new { filters = new Dictionary<string, object[]> { ["status"] = [new { op = "eq", value = "CONFIRMED" }] } });

        var body = await ReadAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(1);
        body.GetProperty("items")[0].GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Search_FilterByCustomerId_ReturnsOnlyThatCustomersOrders()
    {
        var first = await CreateCustomerAsync();
        var second = await CreateCustomerAsync();
        await CreateOrderAsync(first);
        await CreateOrderAsync(second);

        var response = await Post(Client(), "/orders/search", new { filters = new Dictionary<string, object[]> { ["customer_id"] = [new { op = "eq", value = first }] } });

        (await ReadAsync(response)).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Search_InvalidFilterValue_Returns400()
    {
        var response = await Post(Client(), "/orders/search", new { filters = new Dictionary<string, object[]> { ["customer_id"] = [new { op = "eq", value = "not-a-guid" }] } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-05");
    }
}

[Collection(ApiCollection.Name)]
public class PlatformIntegrationTests(ApiWebApplicationFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Responses_CarrySecurityHeadersAndRequestId()
    {
        var requestId = Guid.NewGuid().ToString();
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Request-Id", requestId);

        var response = await client.GetAsync($"/customers/{Guid.NewGuid()}");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        response.Headers.GetValues("X-Request-Id").Should().Contain(requestId);
        (await ReadAsync(response)).GetProperty("request_id").GetString().Should().Be(requestId);
    }

    [Fact]
    public async Task Responses_ReplaceNonUuidRequestIdWithGeneratedUuid()
    {
        var client = Client();
        client.DefaultRequestHeaders.Add("X-Request-Id", "not-a-uuid");

        var response = await client.GetAsync($"/customers/{Guid.NewGuid()}");

        var echoed = response.Headers.GetValues("X-Request-Id").Single();
        Guid.TryParse(echoed, out _).Should().BeTrue();
        (await ReadAsync(response)).GetProperty("request_id").GetString().Should().Be(echoed);
    }

    [Fact]
    public async Task Health_Returns200WithoutAuthentication()
    {
        var response = await Client(authenticated: false).GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_IsNotExposedOutsideDevelopment()
    {
        var response = await Client(authenticated: false).GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OversizedBody_IsRejected()
    {
        var payload = new string('a', 11 * 1024 * 1024);

        var response = await Client(["admin"]).PostAsync("/customers", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.RequestEntityTooLarge, HttpStatusCode.BadRequest);
    }
}
