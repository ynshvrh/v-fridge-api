using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;

namespace VFridge.Api.Services;

/// <summary>
/// Sends transactional email via the Resend HTTPS API (https://resend.com). This works on cloud
/// hosts that block outbound SMTP (Render free tier, Heroku, App Engine) since all traffic is
/// HTTPS on 443.
/// </summary>
public sealed class ResendEmailSender(
    HttpClient http,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _opts = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey) || string.IsNullOrWhiteSpace(_opts.From))
        {
            logger.LogWarning("Resend not configured (missing ApiKey or From) — logging email instead. To={To} Subject={Subject}",
                to, subject);
            return;
        }

        // Resend wants either a bare email or "Name <email>" in `from`. DisplayName is optional.
        var fromHeader = string.IsNullOrWhiteSpace(_opts.DisplayName)
            ? _opts.From
            : $"{_opts.DisplayName} <{_opts.From}>";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Content = JsonContent.Create(new ResendEmailRequest(
                From: fromHeader,
                To: [to],
                Subject: subject,
                Html: htmlBody))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            // Re-throw — AuthService and DailyMaintenanceWorker already wrap email sends in
            // try/catch, so the failure is logged with caller context.
            throw new InvalidOperationException(
                $"Resend API returned {(int)response.StatusCode}: {body}");
        }
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyList<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html);
}
