using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFridge.Api.Data;
using VFridge.Api.Services;
using VFridge.Api.Tests.Integration.Infrastructure;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class DailyMaintenanceWorkerTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public DailyMaintenanceWorkerTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task RunOnce_EmailsExpiryDigest_AndDeletesStaleUnverifiedUsers()
    {
        // === Seed ===
        // 1) Verified user with a product expiring tomorrow → should get a digest email.
        await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "verified",
            email = "verified@example.com",
            password = "secret123"
        });
        await ForceVerifyAsync("verified@example.com");
        var verifiedToken = await LoginAsync("verified@example.com", "secret123");

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/products"))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", verifiedToken);
            req.Content = JsonContent.Create(new
            {
                name = "Almost-gone milk",
                quantity = 1,
                unit = "l",
                expiryDate = tomorrow.ToString("yyyy-MM-dd"),
                category = "dairy"
            });
            var resp = await _client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        // 2) Verified user with a product expiring in 30 days → should NOT get an email.
        await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "fresh",
            email = "fresh@example.com",
            password = "secret123"
        });
        await ForceVerifyAsync("fresh@example.com");
        var freshToken = await LoginAsync("fresh@example.com", "secret123");

        var later = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/products"))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", freshToken);
            req.Content = JsonContent.Create(new
            {
                name = "Fresh yogurt",
                quantity = 1,
                unit = "pcs",
                expiryDate = later.ToString("yyyy-MM-dd"),
                category = "dairy"
            });
            var resp = await _client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        // 3) Old unverified user (created 8 days ago) → should be deleted.
        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO users (username, email, password, created_at) VALUES ('stale', 'stale@example.com', 'h', NOW() - INTERVAL '8 days')");
        }

        // 4) Recent unverified user (created 1 day ago) → must NOT be deleted yet.
        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO users (username, email, password, created_at) VALUES ('newbie', 'newbie@example.com', 'h', NOW() - INTERVAL '1 day')");
        }

        // Forget what the signup flow put in the outbox so we only assert on the digest.
        _factory.Emails.Outbox.Clear();

        // === Trigger ===
        var worker = _factory.Services.GetRequiredService<DailyMaintenanceWorker>();
        await worker.RunOnceAsync(CancellationToken.None);

        // === Assert ===
        // Only the verified user with a near-expiry product gets a digest.
        var digests = _factory.Emails.Outbox.ToList();
        digests.Should().ContainSingle()
            .Which.To.Should().Be("verified@example.com");
        digests[0].Subject.Should().Contain("expiring soon");
        digests[0].HtmlBody.Should().Contain("Almost-gone milk");

        // Stale unverified user is gone; the recent one stays.
        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
            var emails = await db.Users.Select(u => u.Email).ToListAsync();
            emails.Should().Contain("verified@example.com");
            emails.Should().Contain("fresh@example.com");
            emails.Should().Contain("newbie@example.com");
            emails.Should().NotContain("stale@example.com");
        }
    }

    private async Task ForceVerifyAsync(string email)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO email_verifications (user_id, verified_at) SELECT id, NOW() FROM users WHERE email = {0} ON CONFLICT DO NOTHING",
            email);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }
}
