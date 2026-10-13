using FluentAssertions;
using OrderApi.Domain.Entities;

namespace OrderApi.Test.Entities;

public class OrderTests
{
    [Fact]
    public void Total_WithoutItems_IsZero()
    {
        var order = new Order();

        order.Total.Should().Be(0m);
    }

    [Fact]
    public void Total_WithItems_SumsUnitPriceTimesQuantity()
    {
        var order = new Order
        {
            Items =
            [
                new OrderItem { Description = "a", UnitPrice = 10.50m, Quantity = 2 },
                new OrderItem { Description = "b", UnitPrice = 3m, Quantity = 1 },
            ],
        };

        order.Total.Should().Be(24m);
    }

    [Theory]
    [InlineData(OrderStatus.Open, true)]
    [InlineData(OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Canceled, false)]
    public void IsEditable_ByStatus_OnlyOpenIsEditable(OrderStatus status, bool expected)
    {
        var order = new Order { Status = status };

        order.IsEditable.Should().Be(expected);
    }

    [Fact]
    public void Subtotal_IsUnitPriceTimesQuantity()
    {
        var item = new OrderItem { UnitPrice = 2.25m, Quantity = 4 };

        item.Subtotal.Should().Be(9m);
    }

    [Fact]
    public void NewOrder_DefaultsToOpen()
    {
        new Order().Status.Should().Be(OrderStatus.Open);
    }
}
