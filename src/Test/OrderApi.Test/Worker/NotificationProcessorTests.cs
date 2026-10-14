using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrderApi.Application.Events;
using OrderApi.Worker;

namespace OrderApi.Test.Worker;

public class NotificationProcessorTests
{
    private readonly Mock<IEmailSender> _sender = new();
    private readonly NotificationProcessor _processor;

    public NotificationProcessorTests() =>
        _processor = new NotificationProcessor(_sender.Object, NullLogger<NotificationProcessor>.Instance);

    private static byte[] ValidPayload() => OrderEventMessages.Serialize(new OrderStatusChangedEvent(
        Guid.NewGuid(), "ana@example.com", "Ana", "OPEN", "CONFIRMED", 20m, DateTimeOffset.UtcNow));

    [Fact]
    public async Task ProcessAsync_ValidMessage_SendsEmailAndAcks()
    {
        var outcome = await _processor.ProcessAsync(ValidPayload(), retryCount: 0);

        outcome.Should().Be(ProcessingOutcome.Ack);
        _sender.Verify(
            s => s.SendAsync("ana@example.com", It.Is<string>(subject => subject.Contains("CONFIRMED")), It.Is<string>(html => html.Contains("Ana")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task ProcessAsync_MalformedMessage_GoesToDeadLetterWithoutSending(string payload)
    {
        var outcome = await _processor.ProcessAsync(Encoding.UTF8.GetBytes(payload), retryCount: 0);

        outcome.Should().Be(ProcessingOutcome.DeadLetter);
        _sender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ProcessAsync_TransientSendFailureBelowLimit_Retries(int retryCount)
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var outcome = await _processor.ProcessAsync(ValidPayload(), retryCount);

        outcome.Should().Be(ProcessingOutcome.Retry);
    }

    [Fact]
    public async Task ProcessAsync_SendFailureAfterMaxRetries_GoesToDeadLetter()
    {
        _sender.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var outcome = await _processor.ProcessAsync(ValidPayload(), NotificationProcessor.MaxRetries);

        outcome.Should().Be(ProcessingOutcome.DeadLetter);
    }
}

public class EmailRendererTests
{
    private static OrderStatusChangedEvent Event(string name = "Ana") => new(
        Guid.Parse("3f2a9c1e-0000-4000-8000-000000000001"), "ana@example.com", name, "OPEN", "CONFIRMED", 20.5m,
        new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Render_IncludesOrderDetails()
    {
        var html = OrderStatusEmailRenderer.Render(Event());

        html.Should().Contain("Hello, Ana!").And.Contain("3F2A9C1E").And.Contain("OPEN").And.Contain("CONFIRMED").And.Contain("20.50");
    }

    [Fact]
    public void Render_HtmlEncodesCustomerName()
    {
        var html = OrderStatusEmailRenderer.Render(Event("<script>alert(1)</script>"));

        html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void Render_CanceledOrder_UsesCancellationMessage()
    {
        var html = OrderStatusEmailRenderer.Render(Event() with { NewStatus = "CANCELED" });

        html.Should().Contain("canceled");
    }

    [Fact]
    public void Subject_UsesShortOrderIdAndNewStatus()
    {
        OrderStatusEmailRenderer.Subject(Event()).Should().Be("Order #3F2A9C1E - CONFIRMED");
    }
}
