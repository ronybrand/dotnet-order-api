using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using OrderApi.Api.Controllers;
using OrderApi.Api.Extensions;
using OrderApi.Api.Security;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services.Interfaces;
using OrderApi.Domain.Common.Search;

namespace OrderApi.Test.Controllers;

public class CustomersControllerTests
{
    private readonly Mock<ICustomerService> _service = new();
    private readonly CustomersController _controller;

    public CustomersControllerTests() => _controller = new CustomersController(_service.Object);

    [Fact]
    public async Task Create_ReturnsCreatedAtGetWithBody()
    {
        var dto = new CustomerDto(Guid.NewGuid(), "Ana");
        var request = new CreateCustomerRequest("Ana", "12345", null, "ana@example.com");
        _service.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await _controller.Create(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(CustomersController.Get));
        created.RouteValues!["id"].Should().Be(dto.Id);
        created.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Get_ReturnsOkWithDto()
    {
        var dto = new CustomerDto(Guid.NewGuid(), "Ana");
        _service.Setup(s => s.GetAsync(dto.Id, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await _controller.Get(dto.Id, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Update_ReturnsOkWithDto()
    {
        var dto = new CustomerDto(Guid.NewGuid(), "New");
        var request = new UpdateCustomerRequest("New", "12345", null, "n@example.com");
        _service.Setup(s => s.UpdateAsync(dto.Id, request, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await _controller.Update(dto.Id, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        var id = Guid.NewGuid();

        var result = await _controller.Delete(id, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchByQuery_ConvertsQueryToSearchRequest()
    {
        SearchRequest? captured = null;
        _service.Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SearchRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(new PagedResult<CustomerDto>([], 0, 2, 5));

        await _controller.SearchByQuery(new SearchQuery(2, 5, "-name"), CancellationToken.None);

        captured.Should().Be(new SearchRequest(2, 5, "-name"));
    }

    [Fact]
    public async Task SearchByBody_ForwardsRequestAndReturnsPage()
    {
        var request = new SearchRequest(1, 10);
        var page = new PagedResult<CustomerDto>([new CustomerDto(Guid.NewGuid(), "Ana")], 1, 1, 10);
        _service.Setup(s => s.SearchAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(page);

        var result = await _controller.SearchByBody(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(page);
    }
}

public class OrdersControllerTests
{
    private readonly Mock<IOrderService> _service = new();
    private readonly OrdersController _controller;
    private static readonly OrderDto Order = new(Guid.NewGuid(), Guid.NewGuid(), "OPEN", 1, [], 0m);

    public OrdersControllerTests() => _controller = new OrdersController(_service.Object);

    [Fact]
    public async Task Create_ReturnsCreatedAtGetWithBody()
    {
        var request = new CreateOrderRequest(Order.CustomerId, null);
        _service.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        var result = await _controller.Create(request, CancellationToken.None);

        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.ActionName.Should().Be(nameof(OrdersController.Get));
        created.Value.Should().Be(Order);
    }

    [Fact]
    public async Task Get_ReturnsOk()
    {
        _service.Setup(s => s.GetAsync(Order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        (await _controller.Get(Order.Id, CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Order);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent()
    {
        (await _controller.Delete(Order.Id, CancellationToken.None)).Should().BeOfType<NoContentResult>();
        _service.Verify(s => s.DeleteAsync(Order.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddItem_ReturnsOkWithUpdatedOrder()
    {
        var item = new ItemRequest("Pen", 1m, 1);
        _service.Setup(s => s.AddItemAsync(Order.Id, item, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        (await _controller.AddItem(Order.Id, item, CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Order);
    }

    [Fact]
    public async Task UpdateItemQuantity_PassesQuantityFromBody()
    {
        var itemId = Guid.NewGuid();
        _service.Setup(s => s.UpdateItemQuantityAsync(Order.Id, itemId, 7, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        var result = await _controller.UpdateItemQuantity(Order.Id, itemId, new UpdateItemQuantityRequest(7), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Order);
    }

    [Fact]
    public async Task RemoveItem_ReturnsOkWithUpdatedOrder()
    {
        var itemId = Guid.NewGuid();
        _service.Setup(s => s.RemoveItemAsync(Order.Id, itemId, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        (await _controller.RemoveItem(Order.Id, itemId, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Confirm_ReturnsOk()
    {
        _service.Setup(s => s.ConfirmAsync(Order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        (await _controller.Confirm(Order.Id, CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Order);
    }

    [Fact]
    public async Task Cancel_ReturnsOk()
    {
        _service.Setup(s => s.CancelAsync(Order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Order);

        (await _controller.Cancel(Order.Id, CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(Order);
    }

    [Fact]
    public async Task SearchByQuery_And_SearchByBody_DelegateToService()
    {
        var page = new PagedResult<OrderDto>([Order], 1, 0, 20);
        _service.Setup(s => s.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(page);

        (await _controller.SearchByQuery(new SearchQuery(), CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(page);
        (await _controller.SearchByBody(new SearchRequest(), CancellationToken.None)).Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(page);
    }
}

/// <summary>Guards "no public route by omission": authorization is verified by reflection on every controller.</summary>
public class ControllerAuthorizationTests
{
    private static readonly Type[] Controllers = [typeof(CustomersController), typeof(OrdersController)];

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    [Fact]
    public void EveryController_RequiresAuthenticationAtClassLevel()
    {
        foreach (var controller in Controllers)
        {
            controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull($"{controller.Name} must declare [Authorize]");
        }
    }

    [Fact]
    public void NoAction_AllowsAnonymousAccess()
    {
        Controllers.SelectMany(Actions)
            .Should().NotContain(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
    }

    [Fact]
    public void CustomerMutations_RequireAdminRole()
    {
        var mutations = new[] { nameof(CustomersController.Create), nameof(CustomersController.Update), nameof(CustomersController.Delete) };

        foreach (var name in mutations)
        {
            var method = typeof(CustomersController).GetMethod(name)!;
            method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.Should().Be(Roles.Admin, $"{name} must be admin only");
        }
    }

    [Fact]
    public void CustomerReads_DoNotRequireAdminRole()
    {
        var reads = new[] { nameof(CustomersController.Get), nameof(CustomersController.SearchByQuery), nameof(CustomersController.SearchByBody) };

        foreach (var name in reads)
        {
            typeof(CustomersController).GetMethod(name)!.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
        }
    }

    [Fact]
    public void EveryAction_AcceptsACancellationToken()
    {
        Controllers.SelectMany(Actions)
            .Should().OnlyContain(m => m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)));
    }
}
