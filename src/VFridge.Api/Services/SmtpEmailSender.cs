using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using VFridge.Api.Configuration;

namespace VFridge.Api.Services;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _opts = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.SmtpHost) || string.IsNullOrWhiteSpace(_opts.Username))
        {
            logger.LogWarning("SMTP not configured — logging email instead. To={To} Subject={Subject}\n{Body}",
                to, subject, htmlBody);
            return;
        }

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_opts.DisplayName, _opts.From));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var secure = _opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(_opts.SmtpHost, _opts.SmtpPort, secure, ct);
        await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
