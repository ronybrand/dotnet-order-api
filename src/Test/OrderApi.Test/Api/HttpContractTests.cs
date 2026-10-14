using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using OrderApi.Application.DTOs;
using OrderApi.TestSupport;

namespace OrderApi.Test.Api;

[CollectionDefinition(Name)]
public sealed class HostlessApiCollection : ICollectionFixture<HostlessApiFactory>
{
    public const string Name = "hostless-api";
}

/// <summary>
/// HTTP contract of the real pipeline without a database: every request here is rejected before it
/// reaches a service, so these tests need neither Docker nor Postgres.
/// </summary>
[Collection(HostlessApiCollection.Name)]
public class AuthenticationContractTests(HostlessApiFactory factory)
{
    private static readonly Guid SomeId = Guid.NewGuid();

    private HttpClient WithToken(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Subject() => $"user-{Guid.NewGuid():N}";

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NoToken_ReturnsErrorContractAndBearerChallenge()
    {
        var response = await factory.CreateClient().GetAsync($"/orders/{SomeId}");

        response.Headers.WwwAuthenticate.Should().ContainSingle().Which.Scheme.Should().Be("Bearer");
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("code").GetString().Should().Be("AUTH-01");
        error.GetProperty("request_id").GetString().Should().Be(response.Headers.GetValues("X-Request-Id").Single());
    }

    [Fact]
    public async Task AdminOnlyRoute_WithUserRole_ReturnsErrorContract()
    {
        var token = TestTokens.Create(factory.SigningKey, Subject(), ["user"]);

        var response = await WithToken(token).DeleteAsync($"/customers/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        error.GetProperty("code").GetString().Should().Be("AUTH-02");
    }

    [Fact]
    public async Task UnauthorizedResponse_StillCarriesSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync($"/orders/{SomeId}");

        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        response.Headers.GetValues("X-Request-Id").Should().ContainSingle();
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var token = TestTokens.Create(factory.SigningKey, Subject(), ["user"], lifetime: TimeSpan.FromMinutes(-1));

        var response = await WithToken(token).GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TokenWithoutSubject_Returns401()
    {
        var token = TestTokens.Create(factory.SigningKey, subject: null, ["user"]);

        var response = await WithToken(token).GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HmacTokenSignedWithThePublicKey_Returns401()
    {
        var token = TestTokens.CreateHmacForged(factory.PublicKeyBytes, Subject(), ["admin"]);

        var response = await WithToken(token).GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnsignedToken_Returns401()
    {
        var response = await WithToken(TestTokens.CreateUnsigned(Subject(), ["admin"])).GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedBearer_Returns401()
    {
        var response = await WithToken("not.a.jwt").GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var token = TestTokens.Create(factory.SigningKey, Subject(), ["user"], issuer: "someone-else");

        var response = await WithToken(token).GetAsync($"/orders/{SomeId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("user")]
    [InlineData(null)]
    public async Task CustomerWrite_WithoutAdminRole_Returns403(string? role)
    {
        var token = TestTokens.Create(factory.SigningKey, Subject(), role is null ? [] : [role]);
        var body = new { name = "Ana", tax_id = "TAX-12345", email = "ana@example.com" };

        var response = await WithToken(token).PostAsJsonAsync("/customers", body, ContractJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

[Collection(HostlessApiCollection.Name)]
public class RequestValidationContractTests(HostlessApiFactory factory)
{
    private HttpClient Admin() => factory.ClientFor(["admin"], $"admin-{Guid.NewGuid():N}");

    private HttpClient User() => factory.ClientFor(["user"], $"user-{Guid.NewGuid():N}");

    private static async Task AssertValidationError(HttpResponseMessage response, string? field = null)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement;
        body.GetProperty("code").GetString().Should().Be("VALIDATION-01");
        body.GetProperty("request_id").GetString().Should().NotBeNullOrWhiteSpace();
        if (field is not null)
        {
            body.GetProperty("params").GetProperty("field").GetString().Should().Be(field);
        }
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Theory]
    [InlineData("GET", "/customers/not-a-guid")]
    [InlineData("GET", "/orders/not-a-guid")]
    [InlineData("DELETE", "/orders/not-a-guid")]
    [InlineData("POST", "/orders/not-a-guid/confirm")]
    [InlineData("POST", "/orders/not-a-guid/cancel")]
    public async Task InvalidGuidInRoute_Returns400WithErrorBody(string method, string url)
    {
        var response = await User().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        await AssertValidationError(response, "id");
    }

    [Fact]
    public async Task InvalidItemGuidInRoute_Returns400()
    {
        var response = await User().DeleteAsync($"/orders/{Guid.NewGuid()}/items/not-a-guid");

        await AssertValidationError(response, "item_id");
    }

    [Fact]
    public async Task MalformedJsonBody_Returns400()
    {
        var response = await User().PostAsync("/orders", Json("{ this is not json"));

        await AssertValidationError(response);
    }

    [Fact]
    public async Task EmptyBody_Returns400()
    {
        var response = await Admin().PostAsync("/customers", Json(string.Empty));

        await AssertValidationError(response);
    }

    [Theory]
    [InlineData("POST", "/orders", "{\"customer_id\":\"3f2a9c1e-0000-4000-8000-000000000001\",\"items\":[],\"status\":\"CONFIRMED\"}")]
    [InlineData("POST", "/orders", "{\"customer_id\":\"3f2a9c1e-0000-4000-8000-000000000001\",\"items\":[{\"description\":\"x\",\"unit_price\":1,\"quantity\":1,\"id\":\"x\"}]}")]
    [InlineData("POST", "/orders/3f2a9c1e-0000-4000-8000-000000000001/items", "{\"description\":\"x\",\"unit_price\":1,\"quantity\":1,\"total\":9}")]
    [InlineData("PATCH", "/orders/3f2a9c1e-0000-4000-8000-000000000001/items/3f2a9c1e-0000-4000-8000-000000000002", "{\"quantity\":1,\"unit_price\":0.01}")]
    [InlineData("POST", "/orders/search", "{\"page\":0,\"is_admin\":true}")]
    [InlineData("POST", "/customers/search", "{\"size\":5,\"extra\":1}")]
    [InlineData("POST", "/customers", "{\"name\":\"Ana\",\"tax_id\":\"TAX-12345\",\"email\":\"a@b.com\",\"role\":\"admin\"}")]
    [InlineData("PUT", "/customers/3f2a9c1e-0000-4000-8000-000000000001", "{\"name\":\"Ana\",\"tax_id\":\"TAX-12345\",\"email\":\"a@b.com\",\"id\":\"x\"}")]
    public async Task UnknownFieldInBody_Returns400(string method, string url, string json)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = Json(json) };

        var response = await Admin().SendAsync(request);

        await AssertValidationError(response);
    }

    [Theory]
    [InlineData("{\"name\":\"Ana\",\"tax_id\":\"TAX-12345\",\"email\":\"not-an-email\"}", "email")]
    [InlineData("{\"name\":\"Ana\",\"tax_id\":\"TAX-12345\",\"passport_number\":\"ab123456\",\"email\":\"a@b.com\"}", "passport_number")]
    [InlineData("{\"name\":\"  \",\"tax_id\":\"TAX-12345\",\"email\":\"a@b.com\"}", "name")]
    [InlineData("{\"name\":\"Ana\",\"tax_id\":\"1\",\"email\":\"a@b.com\"}", "tax_id")]
    public async Task CustomerBody_InvalidField_Returns400NamingTheField(string json, string field)
    {
        var response = await Admin().PostAsync("/customers", Json(json));

        await AssertValidationError(response, field);
    }

    [Theory]
    [InlineData("\"description\":\"x\",\"unit_price\":1.234,\"quantity\":1", "unit_price")]
    [InlineData("\"description\":\"x\",\"unit_price\":0,\"quantity\":1", "unit_price")]
    [InlineData("\"description\":\"x\",\"unit_price\":1,\"quantity\":0", "quantity")]
    [InlineData("\"description\":\"   \",\"unit_price\":1,\"quantity\":1", "description")]
    public async Task AddItemBody_InvalidField_Returns400NamingTheField(string fields, string field)
    {
        var response = await User().PostAsync($"/orders/{Guid.NewGuid()}/items", Json("{" + fields + "}"));

        await AssertValidationError(response, field);
    }

    [Fact]
    public async Task AddItemBody_DescriptionOver255Characters_Returns400()
    {
        var json = "{\"description\":\"" + new string('a', 256) + "\",\"unit_price\":1,\"quantity\":1}";

        var response = await User().PostAsync($"/orders/{Guid.NewGuid()}/items", Json(json));

        await AssertValidationError(response, "description");
    }

    [Fact]
    public async Task UpdateQuantity_NonPositive_Returns400()
    {
        var url = $"/orders/{Guid.NewGuid()}/items/{Guid.NewGuid()}";

        var response = await User().PatchAsync(url, Json("{\"quantity\":0}"));

        await AssertValidationError(response, "quantity");
    }

    [Fact]
    public async Task CreateOrder_MoreThan200Items_Returns400()
    {
        var item = "{\"description\":\"x\",\"unit_price\":1,\"quantity\":1}";
        var items = string.Join(",", Enumerable.Repeat(item, 201));
        var json = "{\"customer_id\":\"" + Guid.NewGuid() + "\",\"items\":[" + items + "]}";

        var response = await User().PostAsync("/orders", Json(json));

        await AssertValidationError(response, "items");
    }

    [Fact]
    public async Task CreateOrder_EmptyCustomerId_Returns400()
    {
        var response = await User().PostAsync("/orders", Json("{\"customer_id\":\"00000000-0000-0000-0000-000000000000\"}"));

        await AssertValidationError(response, "customer_id");
    }

    [Theory]
    [InlineData("/customers/search?size=0")]
    [InlineData("/customers/search?size=101")]
    [InlineData("/customers/search?page=-1")]
    [InlineData("/orders/search?size=1000")]
    public async Task SearchQuery_OutOfRange_Returns400(string url)
    {
        var response = await User().GetAsync(url);

        await AssertValidationError(response);
    }

    [Theory]
    [InlineData("{\"size\":0}")]
    [InlineData("{\"size\":101}")]
    [InlineData("{\"page\":-1}")]
    public async Task SearchBody_OutOfRange_Returns400(string json)
    {
        var response = await User().PostAsync("/orders/search", Json(json));

        await AssertValidationError(response);
    }

    [Fact]
    public async Task ValidationError_EchoesTheRequestIdHeaderInTheBody()
    {
        var requestId = Guid.NewGuid().ToString();
        var client = User();
        client.DefaultRequestHeaders.Add("X-Request-Id", requestId);

        var response = await client.GetAsync("/orders/not-a-guid");

        (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync())).RootElement
            .GetProperty("request_id").GetString().Should().Be(requestId);
    }
}

public class ResponseShapeTests
{
    [Fact]
    public void CustomerDto_DoesNotExposePii()
    {
        var properties = typeof(CustomerDto).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);

        properties.Should().BeEquivalentTo(["Id", "Name"]);
    }
}

[Collection(HostlessApiCollection.Name)]
public class RateLimitContractTests(HostlessApiFactory factory)
{
    [Fact]
    public async Task Health_IsNotRateLimited()
    {
        // The database is unreachable here, so an admitted probe hangs on the health check while a
        // rejected one answers 429 immediately: fire more probes than the global limit at once and
        // inspect only the ones that answered within the timeout.
        var client = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var probes = Enumerable.Range(0, 105).Select(async _ =>
        {
            try
            {
                return (HttpStatusCode?)(await client.GetAsync("/health", timeout.Token)).StatusCode;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        });

        (await Task.WhenAll(probes)).Should().NotContain(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task OrderWrites_ExceedingPerRouteLimit_Return429()
    {
        var client = factory.ClientFor(subject: $"order-writer-{Guid.NewGuid():N}");
        HttpResponseMessage? last = null;
        for (var i = 0; i <= 30; i++)
        {
            last = await client.PostAsJsonAsync("/orders", new { });
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}

internal static class ContractJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}
