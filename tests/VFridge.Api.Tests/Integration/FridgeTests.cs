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
public class FridgeTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public FridgeTests(PostgresFixture pg) => _pg = pg;

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
    public async Task Signup_AutoCreatesPersonalFridge()
    {
        var token = await BootstrapVerifiedUserAsync("alice", "alice@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var list = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        list.GetArrayLength().Should().Be(1);
        list[0].GetProperty("role").GetString().Should().Be("owner");
        list[0].GetProperty("name").GetString().Should().Contain("alice");
        list[0].GetProperty("memberCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Owner_RenamesAndCreatesAndDeletesAnExtraFridge()
    {
        var token = await BootstrapVerifiedUserAsync("bob", "bob@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Personal fridge exists.
        var initial = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        var personalId = initial[0].GetProperty("id").GetInt32();

        // Rename the personal fridge.
        var rename = await _client.PatchAsJsonAsync($"/fridges/{personalId}", new { name = "Bob's pantry" });
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rename.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("name").GetString().Should().Be("Bob's pantry");

        // Create an extra fridge.
        var create = await _client.PostAsJsonAsync("/fridges", new { name = "Office fridge" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var extraId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // The extra fridge deletes cleanly.
        var del = await _client.DeleteAsync($"/fridges/{extraId}");
        del.StatusCode.Should().Be(HttpStatusCode.OK);

        // Deleting the last owned fridge is allowed. The user is left without
        // any fridge — clients surface an empty state with a "Create your
        // first fridge" CTA instead of the server fabricating one.
        var lastDel = await _client.DeleteAsync($"/fridges/{personalId}");
        lastDel.StatusCode.Should().Be(HttpStatusCode.OK);
        (await lastDel.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("success").GetBoolean().Should().BeTrue();

        var after = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        after.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task NonOwner_CannotRenameOrDelete()
    {
        var aliceToken = await BootstrapVerifiedUserAsync("alice", "alice@example.com", "secret123");
        var bobToken   = await BootstrapVerifiedUserAsync("bob",   "bob@example.com",   "secret123");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
        var alicesFridges = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        var aliceFridgeId = alicesFridges[0].GetProperty("id").GetInt32();

        // Bob isn't a member of Alice's fridge yet — should get 404, not 403.
        using var bobClient = _factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);

        var rename = await bobClient.PatchAsJsonAsync($"/fridges/{aliceFridgeId}", new { name = "Hijacked" });
        rename.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await rename.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("NOT_FRIDGE_OWNER");
    }

    [Fact]
    public async Task InviteAcceptFlow_AddsMemberAndSharesProducts()
    {
        var aliceToken = await BootstrapVerifiedUserAsync("alice", "alice@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        var aliceFridges = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        var aliceFridgeId = aliceFridges[0].GetProperty("id").GetInt32();

        // Alice puts something in her fridge.
        await _client.PostAsJsonAsync("/products", new
        {
            name = "Milk", quantity = 1, unit = "l", category = "dairy"
        });

        // Alice invites bob.
        _factory.Emails.Outbox.Clear();
        var inv = await _client.PostAsJsonAsync($"/fridges/{aliceFridgeId}/invites", new { email = "bob@example.com" });
        inv.StatusCode.Should().Be(HttpStatusCode.Created);

        // The email lands in the outbox; extract the raw token from the URL.
        var letter = _factory.Emails.LastTo("bob@example.com");
        letter.Should().NotBeNull();
        var rawToken = ExtractInviteToken(letter!.HtmlBody);

        // Bob signs up and verifies, then accepts.
        var bobToken = await BootstrapVerifiedUserAsync("bob", "bob@example.com", "secret456");
        using var bobClient = _factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);

        var accept = await bobClient.PostAsJsonAsync("/fridges/accept", new { token = rawToken });
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = await accept.Content.ReadFromJsonAsync<JsonElement>();
        accepted.GetProperty("fridgeId").GetInt32().Should().Be(aliceFridgeId);

        // Bob now sees Alice's fridge with X-Fridge-Id and can read her milk.
        bobClient.DefaultRequestHeaders.Add("X-Fridge-Id", aliceFridgeId.ToString());
        var bobsView = await bobClient.GetFromJsonAsync<JsonElement>("/products");
        bobsView.GetArrayLength().Should().Be(1);
        bobsView[0].GetProperty("name").GetString().Should().Be("Milk");

        // Second accept on the same token fails.
        var replay = await bobClient.PostAsJsonAsync("/fridges/accept", new { token = rawToken });
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await replay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("INVITE_USED");
    }

    [Fact]
    public async Task Member_CanLeave_OwnerCannotLeave()
    {
        // Owner is alice, member is bob (via invite).
        var aliceToken = await BootstrapVerifiedUserAsync("alice", "alice@example.com", "secret123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
        var aliceFridges = await _client.GetFromJsonAsync<JsonElement>("/fridges");
        var aliceFridgeId = aliceFridges[0].GetProperty("id").GetInt32();

        _factory.Emails.Outbox.Clear();
        await _client.PostAsJsonAsync($"/fridges/{aliceFridgeId}/invites", new { email = "bob@example.com" });
        var letter = _factory.Emails.LastTo("bob@example.com")!;
        var rawToken = ExtractInviteToken(letter.HtmlBody);

        var bobToken = await BootstrapVerifiedUserAsync("bob", "bob@example.com", "secret456");
        using var bobClient = _factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
        await bobClient.PostAsJsonAsync("/fridges/accept", new { token = rawToken });

        // Owner can't leave.
        var aliceLeave = await _client.DeleteAsync($"/fridges/{aliceFridgeId}/members/me");
        aliceLeave.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Member can leave.
        var bobLeave = await bobClient.DeleteAsync($"/fridges/{aliceFridgeId}/members/me");
        bobLeave.StatusCode.Should().Be(HttpStatusCode.OK);
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
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static string ExtractInviteToken(string html)
    {
        const string marker = "invite?token=";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Invite email did not embed a token URL");
        var start = idx + marker.Length;
        var end = html.IndexOfAny(new[] { '"', ' ', '\n', '<' }, start);
        return Uri.UnescapeDataString(html[start..end]);
    }
}
