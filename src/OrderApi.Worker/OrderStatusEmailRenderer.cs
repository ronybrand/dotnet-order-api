using System.Globalization;
using System.Text.Encodings.Web;
using OrderApi.Application.Events;

namespace OrderApi.Worker;

public static class OrderStatusEmailRenderer
{
    private const string Template = """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="UTF-8"><title>Order Update</title></head>
        <body style="font-family:Arial,sans-serif;background:#f0f2f5;color:#1a1a2e;padding:32px 16px">
        <div style="max-width:600px;margin:0 auto">
          <div style="background:#16213e;border-radius:16px 16px 0 0;padding:32px 40px;text-align:center">
            <div style="font-size:12px;letter-spacing:3px;color:#e94560;text-transform:uppercase">Order API</div>
            <h1 style="color:#fff;font-size:24px">Your order status<br>has been updated</h1>
            <span style="display:inline-block;padding:6px 20px;border-radius:50px;background:{{badge}};color:#fff;font-weight:700">{{newStatus}}</span>
          </div>
          <div style="background:#fff;padding:32px 40px">
            <p style="font-weight:600">Hello, {{name}}!</p>
            <p style="color:#4a5568;line-height:1.6">{{message}}</p>
            <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:12px;padding:20px">
              <p>Order ID: <strong>{{shortId}}...</strong></p>
              <p>Status: <strong>{{oldStatus}} &rarr; {{newStatus}}</strong></p>
              <p>Updated at: <strong>{{changedAt}}</strong></p>
              <p>Total: <strong>$ {{total}}</strong></p>
            </div>
            <p style="font-size:12px;color:#94a3b8">This is an automated notification. Please do not reply.</p>
          </div>
        </div>
        </body>
        </html>
        """;

    public static string Subject(OrderStatusChangedEvent statusChanged) => $"Order #{ShortId(statusChanged)} - {statusChanged.NewStatus}";

    public static string Render(OrderStatusChangedEvent statusChanged)
    {
        var (message, badge) = statusChanged.NewStatus switch
        {
            "CONFIRMED" => ("Great news! Your order has been <strong>confirmed</strong> and is now being processed.", "#10b981"),
            "CANCELED" => ("We are sorry to inform you that your order has been <strong>canceled</strong>.", "#ef4444"),
            _ => ("The status of your order has changed.", "#f59e0b"),
        };

        // Every dynamic value is HTML-encoded; only the fixed messages above carry markup.
        var encoder = HtmlEncoder.Default;
        return Template
            .Replace("{{badge}}", badge, StringComparison.Ordinal)
            .Replace("{{message}}", message, StringComparison.Ordinal)
            .Replace("{{name}}", encoder.Encode(statusChanged.CustomerName), StringComparison.Ordinal)
            .Replace("{{newStatus}}", encoder.Encode(statusChanged.NewStatus), StringComparison.Ordinal)
            .Replace("{{oldStatus}}", encoder.Encode(statusChanged.OldStatus), StringComparison.Ordinal)
            .Replace("{{shortId}}", ShortId(statusChanged), StringComparison.Ordinal)
            .Replace("{{changedAt}}", statusChanged.ChangedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{{total}}", statusChanged.TotalAmount.ToString("F2", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string ShortId(OrderStatusChangedEvent statusChanged) =>
        statusChanged.OrderId.ToString("N", CultureInfo.InvariantCulture)[..8].ToUpperInvariant();
}
