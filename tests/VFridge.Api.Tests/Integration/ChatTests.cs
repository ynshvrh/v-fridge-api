using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFridge.Api.Data;
using VFridge.Api.Tests.Integration.Infrastructure;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class ChatTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public ChatTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();

        var token = await BootstrapVerifiedUserAsync("chef", "chef@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task PostMessage_PassesUserCuisinePreference_To_Ai()
    {
        // Switch the cuisine via the public PATCH endpoint so we exercise the whole chain.
        var patch = await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { cuisinePreference = "ukrainian" }));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Ai.Reply = "borscht please";
        var resp = await _client.PostAsJsonAsync("/chat", new { content = "what to cook" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Ai.LastCuisinePreference.Should().Be("ukrainian");
    }

    [Fact]
    public async Task PostMessage_DefaultsTo_Any_WhenNoPreferenceSet()
    {
        _factory.Ai.Reply = "omelette";
        await _client.PostAsJsonAsync("/chat", new { content = "what to cook" });

        _factory.Ai.LastCuisinePreference.Should().Be("any");
    }

    [Fact]
    public async Task PostMessage_PassesUserPreferredLanguage_To_Ai()
    {
        var patch = await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { preferredLanguage = "uk" }));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        await _client.PostAsJsonAsync("/chat", new { content = "what to cook" });

        _factory.Ai.LastLanguage.Should().Be("uk");
    }

    [Fact]
    public async Task PostMessage_DefaultsLanguage_To_En_WhenNotSet()
    {
        await _client.PostAsJsonAsync("/chat", new { content = "what to cook" });

        _factory.Ai.LastLanguage.Should().Be("en");
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task PostMessage_PersistsBothSides_AndReturnsAiReply()
    {
        _factory.Ai.Reply = "Try a quick omelette.";

        var resp = await _client.PostAsJsonAsync("/chat", new { content = "What can I cook?" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("assistant");
        body.GetProperty("content").GetString().Should().Be("Try a quick omelette.");

        var history = await _client.GetFromJsonAsync<JsonElement>("/chat");
        history.GetArrayLength().Should().Be(2, "user prompt + assistant reply both stored");
        history[0].GetProperty("role").GetString().Should().Be("user");
        history[1].GetProperty("role").GetString().Should().Be("assistant");
    }

    [Fact]
    public async Task PostMessage_WithEmptyContent_Returns_ValidationError()
    {
        var resp = await _client.PostAsJsonAsync("/chat", new { content = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        // The handler returns its own ValidationProblem before DataAnnotations runs, using
        // the lowercase "content" key.
        body.GetProperty("errors").GetProperty("content")[0].GetString().Should().Contain("empty");
    }

    [Fact]
    public async Task PostMessage_Returns502Coded_WhenAllModelsFail()
    {
        // Null reply = every model in the pool failed. The endpoint must surface a coded 502
        // (so the client shows a localized "try again") and must NOT persist a fake reply.
        _factory.Ai.Reply = null!;

        var resp = await _client.PostAsJsonAsync("/chat", new { content = "anything" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("AI_UNAVAILABLE");

        // History stays clean — no user turn, no fake assistant turn persisted.
        var history = await _client.GetFromJsonAsync<JsonElement>("/chat");
        history.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Delete_ClearsHistory_ForCurrentUserOnly()
    {
        await _client.PostAsJsonAsync("/chat", new { content = "hello" });

        var deleteResp = await _client.DeleteAsync("/chat");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var deleteBody = await deleteResp.Content.ReadFromJsonAsync<JsonElement>();
        deleteBody.GetProperty("deleted").GetInt32().Should().Be(2);

        var history = await _client.GetFromJsonAsync<JsonElement>("/chat");
        history.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task RateLimit_Kicks_In_After_5_Calls_In_A_Window()
    {
        // 5 permitted then a 429 on the 6th. The fake AI returns instantly so we don't blow timeouts.
        _factory.Ai.Reply = "ok";

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var r = await _client.PostAsJsonAsync("/chat", new { content = $"hi {i}" });
            statuses.Add(r.StatusCode);
        }

        statuses.Take(5).Should().AllSatisfy(s => s.Should().Be(HttpStatusCode.OK));
        statuses[5].Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task<string> BootstrapVerifiedUserAsync(string username, string email, string password)
    {
        await _client.PostAsJsonAsync("/auth/signup", new { username, email, password });
        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO email_verifications (user_id, verified_at) SELECT id, NOW() FROM users WHERE email = {0} ON CONFLICT DO NOTHING",
                email);
        }
        var login = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        using var tempClient = _factory.CreateClient();
        tempClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var fr = await tempClient.PostAsJsonAsync("/fridges", new { name = $"{username}'s fridge" });
        fr.EnsureSuccessStatusCode();

        return token;
    }
}
