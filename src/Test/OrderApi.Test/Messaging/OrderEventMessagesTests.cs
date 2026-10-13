using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderApi.Application.Events;
using OrderApi.Infrastructure.Messaging;

namespace OrderApi.Test.Messaging;

public class OrderEventMessagesTests
{
    private static OrderStatusChangedEvent SampleEvent() => new(
        Guid.Parse("3f2a9c1e-0000-4000-8000-000000000001"),
        "ana@example.com",
        "Ana",
        "OPEN",
        "CONFIRMED",
        20.5m,
        new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Serialize_UsesSnakeCasePropertyNames()
    {
        var json = JsonDocument.Parse(OrderEventMessages.Serialize(SampleEvent())).RootElement;

        json.GetProperty("order_id").GetGuid().Should().Be(SampleEvent().OrderId);
        json.GetProperty("customer_email").GetString().Should().Be("ana@example.com");
        json.GetProperty("customer_name").GetString().Should().Be("Ana");
        json.GetProperty("old_status").GetString().Should().Be("OPEN");
        json.GetProperty("new_status").GetString().Should().Be("CONFIRMED");
        json.GetProperty("total_amount").GetDecimal().Should().Be(20.5m);
        json.TryGetProperty("changed_at", out _).Should().BeTrue();
    }

    [Fact]
    public void Deserialize_RoundTripsSerializedEvent()
    {
        var result = OrderEventMessages.Deserialize(OrderEventMessages.Serialize(SampleEvent()));

        result.Should().Be(SampleEvent());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"order_id\":\"3f2a9c1e-0000-4000-8000-000000000001\"}")]
    [InlineData("null")]
    public void Deserialize_MalformedOrIncompletePayload_ReturnsNull(string payload)
    {
        OrderEventMessages.Deserialize(System.Text.Encoding.UTF8.GetBytes(payload)).Should().BeNull();
    }

    [Fact]
    public void QueueArguments_RouteRejectedMessagesToDeadLetterQueue()
    {
        OrderEventMessages.QueueArguments["x-dead-letter-exchange"].Should().Be(string.Empty);
        OrderEventMessages.QueueArguments["x-dead-letter-routing-key"].Should().Be(OrderEventMessages.DeadLetterQueue);
    }

    [Fact]
    public async Task PublishStatusChangedAsync_BrokerUnavailable_DoesNotThrow()
    {
        var options = Options.Create(new RabbitMqOptions { Host = "127.0.0.1", Port = 1 });
        var publisher = new RabbitMqOrderEventPublisher(options, NullLogger<RabbitMqOrderEventPublisher>.Instance);

        var act = () => publisher.PublishStatusChangedAsync(SampleEvent());

        await act.Should().NotThrowAsync();
    }
}
