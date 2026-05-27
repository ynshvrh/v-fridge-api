using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VFridge.Api.Data;
using VFridge.Api.Tests.Integration.Infrastructure;

namespace VFridge.Api.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class PreferencesTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public PreferencesTests(PostgresFixture pg) => _pg = pg;

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
    public async Task Signup_DefaultsPreferredLanguage_To_En()
    {
        var token = await BootstrapVerifiedUserAsync("a@example.com", "secret123", preferredLanguage: null);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await _client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("preferredLanguage").GetString().Should().Be("en");
    }

    [Fact]
    public async Task Signup_AcceptsPreferredLanguage_Uk()
    {
        var token = await BootstrapVerifiedUserAsync("b@example.com", "secret123", preferredLanguage: "uk");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await _client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("preferredLanguage").GetString().Should().Be("uk");
    }

    [Fact]
    public async Task Signup_FallsBackToEn_WhenLanguageIsUnsupported()
    {
        var token = await BootstrapVerifiedUserAsync("c@example.com", "secret123", preferredLanguage: "fr");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await _client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("preferredLanguage").GetString().Should().Be("en");
    }

    [Fact]
    public async Task Patch_UpdatesPreferredLanguage_AndReflectsInMe()
    {
        var token = await BootstrapVerifiedUserAsync("d@example.com", "secret123", preferredLanguage: null);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var patch = await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { preferredLanguage = "uk" }));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await patch.Content.ReadFromJsonAsync<JsonElement>();
        patched.GetProperty("preferredLanguage").GetString().Should().Be("uk");

        var me = await _client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("preferredLanguage").GetString().Should().Be("uk");
    }

    [Fact]
    public async Task Patch_RejectsUnsupportedLanguage_With_400()
    {
        var token = await BootstrapVerifiedUserAsync("e@example.com", "secret123", preferredLanguage: null);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var patch = await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { preferredLanguage = "ru" }));
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await patch.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("UNSUPPORTED_LANGUAGE");
    }

    [Fact]
    public async Task Patch_Unauthorized_Without_Token()
    {
        var patch = await _client.PatchAsync("/auth/me/preferences",
            JsonContent.Create(new { preferredLanguage = "uk" }));
        patch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> BootstrapVerifiedUserAsync(string email, string password, string? preferredLanguage)
    {
        var username = email.Split('@')[0];
        var signupBody = preferredLanguage is null
            ? (object)new { username, email, password }
            : new { username, email, password, preferredLanguage };
        var signup = await _client.PostAsJsonAsync("/auth/signup", signupBody);
        signup.EnsureSuccessStatusCode();

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
}
