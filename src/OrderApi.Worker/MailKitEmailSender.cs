using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace OrderApi.Worker;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Defaults to the local Mailpit started by docker-compose: no real e-mail leaves the machine.</summary>
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "no-reply@order-api.local";
}

public sealed class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
