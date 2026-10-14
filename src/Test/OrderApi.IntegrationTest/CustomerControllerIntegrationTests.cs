using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace OrderApi.IntegrationTest;

[Collection(ApiCollection.Name)]
public class CustomerControllerIntegrationTests(ApiWebApplicationFactory factory) : ApiTestBase(factory)
{
    private static object NewCustomer(string? taxId = null, string? passport = null) =>
        new { name = "Ana", tax_id = taxId ?? NewTaxId(), passport_number = passport, email = "ana@example.com" };

    [Fact]
    public async Task Create_AsAdmin_Returns201WithoutPii()
    {
        var response = await Post(Admin(), "/customers", NewCustomer());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(response);
        body.GetProperty("name").GetString().Should().Be("Ana");
        body.TryGetProperty("tax_id", out _).Should().BeFalse();
        body.TryGetProperty("email", out _).Should().BeFalse();
        response.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_AsRegularUser_Returns403()
    {
        var response = await Post(Client(["user"]), "/customers", NewCustomer());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var response = await Post(Client(authenticated: false), "/customers", NewCustomer());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_TokenWithWrongAudience_Returns401()
    {
        var client = Factory.CreateClient();
        var token = TestTokens.Create(Factory.SigningKey, "u1", ["admin"], audience: "someone-else");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await Post(client, "/customers", NewCustomer());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_TokenSignedWithAnotherKey_Returns401()
    {
        using var attackerKey = System.Security.Cryptography.RSA.Create(2048);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create(attackerKey, "u1", ["admin"]));

        var response = await Post(client, "/customers", NewCustomer());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_InvalidBody_Returns400WithField()
    {
        var response = await Post(Admin(), "/customers", new { name = "Ana", tax_id = "1", email = "ana@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        body.GetProperty("code").GetString().Should().Be("VALIDATION-01");
        body.GetProperty("params").GetProperty("field").GetString().Should().Be("tax_id");
    }

    [Fact]
    public async Task Create_UnknownField_Returns400()
    {
        var response = await Post(Admin(), "/customers", new { name = "Ana", tax_id = NewTaxId(), email = "a@b.com", is_admin = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-01");
    }

    [Fact]
    public async Task Create_DuplicateTaxId_Returns409()
    {
        var taxId = NewTaxId();
        await CreateCustomerAsync(taxId);

        var response = await Post(Admin(), "/customers", NewCustomer(taxId));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("CONFLICT-01");
    }

    [Fact]
    public async Task Create_DuplicatePassport_Returns409()
    {
        await CreateCustomerAsync(passport: "AB123456");

        var response = await Post(Admin(), "/customers", NewCustomer(passport: "AB123456"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("CONFLICT-02");
    }

    [Fact]
    public async Task Create_ConcurrentSameTaxId_ExactlyOneSucceeds()
    {
        var taxId = NewTaxId();
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(Admin(), "/customers", NewCustomer(taxId))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Get_ExistingCustomer_Returns200()
    {
        var id = await CreateCustomerAsync();

        var response = await Client().GetAsync($"/customers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Get_UnknownCustomer_Returns404()
    {
        var response = await Client().GetAsync($"/customers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("RESOURCE-NOT-FOUND-01");
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var response = await Client(authenticated: false).GetAsync($"/customers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_AsAdmin_Returns200AndPersists()
    {
        var id = await CreateCustomerAsync();

        var response = await Admin().PutAsJsonAsync($"/customers/{id}", new { name = "Renamed", tax_id = NewTaxId(), email = "r@example.com" }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(await Client().GetAsync($"/customers/{id}"))).GetProperty("name").GetString().Should().Be("Renamed");
    }

    [Fact]
    public async Task Update_KeepingOwnTaxId_IsNotAConflict()
    {
        var taxId = NewTaxId();
        var id = await CreateCustomerAsync(taxId);

        var response = await Admin().PutAsJsonAsync($"/customers/{id}", new { name = "Same", tax_id = taxId, email = "s@example.com" }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_AsRegularUser_Returns403()
    {
        var id = await CreateCustomerAsync();

        var response = await Client(["user"]).PutAsJsonAsync($"/customers/{id}", new { name = "X", tax_id = NewTaxId(), email = "x@example.com" }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_UnknownCustomer_Returns404()
    {
        var response = await Admin().PutAsJsonAsync($"/customers/{Guid.NewGuid()}", new { name = "X", tax_id = NewTaxId(), email = "x@example.com" }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_AsAdmin_SoftDeletesAndReleasesTaxId()
    {
        var taxId = NewTaxId();
        var id = await CreateCustomerAsync(taxId);

        var response = await Admin().DeleteAsync($"/customers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client().GetAsync($"/customers/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Post(Admin(), "/customers", NewCustomer(taxId))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Delete_AsRegularUser_Returns403()
    {
        var id = await CreateCustomerAsync();

        var response = await Client(["user"]).DeleteAsync($"/customers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_CustomerWithOrders_Returns409()
    {
        var id = await CreateCustomerAsync();
        await CreateOrderAsync(id);

        var response = await Admin().DeleteAsync($"/customers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("CONFLICT-03");
    }

    [Fact]
    public async Task Delete_CustomerWhoseOrdersWereDeleted_Succeeds()
    {
        var id = await CreateCustomerAsync();
        var order = await CreateOrderAsync(id);
        await Client().DeleteAsync($"/orders/{order.GetProperty("id").GetGuid()}");

        var response = await Admin().DeleteAsync($"/customers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SearchGet_PaginatesAndSorts()
    {
        foreach (var name in new[] { "Carla", "Alice", "Bruno" })
        {
            await CreateCustomerAsync(name: name);
        }

        var response = await Client().GetAsync("/customers/search?page=0&size=2&sort=name");

        var body = await ReadAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(3);
        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).Should().Equal("Alice", "Bruno");
    }

    [Fact]
    public async Task SearchPost_FilterEq_ReturnsOnlyMatches()
    {
        await CreateCustomerAsync(name: "Alice");
        await CreateCustomerAsync(name: "Bruno");

        var response = await Post(Client(), "/customers/search", new { filters = new Dictionary<string, object[]> { ["name"] = [new { op = "eq", value = "Bruno" }] } });

        var body = await ReadAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(1);
        body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Bruno");
    }

    [Fact]
    public async Task SearchPost_FilterLike_IsCaseInsensitive()
    {
        await CreateCustomerAsync(name: "Alice");
        await CreateCustomerAsync(name: "Bruno");

        var response = await Post(Client(), "/customers/search", new { filters = new Dictionary<string, object[]> { ["name"] = [new { op = "lk", value = "ALI" }] } });

        (await ReadAsync(response)).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Search_UnknownSortField_Returns400()
    {
        var response = await Client().GetAsync("/customers/search?sort=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().Should().Be("VALIDATION-06");
    }

    [Fact]
    public async Task Search_SizeAboveMax_Returns400()
    {
        var response = await Client().GetAsync("/customers/search?size=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Search_WithoutToken_Returns401()
    {
        (await Client(authenticated: false).GetAsync("/customers/search")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Writes_ExceedingPerRouteLimit_Return429()
    {
        var client = Client(["admin"], subject: "rate-limit-probe");
        HttpResponseMessage? last = null;
        for (var i = 0; i < 11; i++)
        {
            last = await Post(client, "/customers", NewCustomer());
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ReadAsync(last)).GetProperty("code").GetString().Should().Be("RATE-00");
    }
}
