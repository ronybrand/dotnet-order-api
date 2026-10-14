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

    /// <summary>Off by default for the local Mailpit; enable it (STARTTLS) for any real relay.</summary>
    public bool UseStartTls { get; set; }

    public string? UserName { get; set; }
    public string? Password { get; set; }
}

public sealed class MailKitEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        // The address itself is PII: the exception message never includes it.
        message.To.Add(MailboxAddress.TryParse(recipient, out var to)
            ? to
            : throw new PermanentEmailException("Recipient address is not a valid mailbox"));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(
            settings.Host,
            settings.Port,
            settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.UserName))
        {
            await client.AuthenticateAsync(settings.UserName, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
