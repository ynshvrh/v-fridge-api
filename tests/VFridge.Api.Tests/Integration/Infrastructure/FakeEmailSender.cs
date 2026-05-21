using System.Collections.Concurrent;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Integration.Infrastructure;

public sealed class FakeEmailSender : IEmailSender
{
    public sealed record SentEmail(string To, string Subject, string HtmlBody);

    public ConcurrentQueue<SentEmail> Outbox { get; } = new();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        Outbox.Enqueue(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }

    /// <summary>Most-recently sent email to the given address, or null if none.</summary>
    public SentEmail? LastTo(string email) => Outbox.LastOrDefault(e => e.To == email);
}
