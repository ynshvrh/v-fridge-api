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
public class AuthFlowTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private TestWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public AuthFlowTests(PostgresFixture pg) => _pg = pg;

    public Task InitializeAsync()
    {
        _factory = new TestWebApplicationFactory(_pg.ConnectionString);
        _client = _factory.CreateClient();
        return _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task FullChain_Signup_Then_Verify_Then_Login_Then_Refresh_Then_Logout()
    {
        // 1. Signup
        var signup = await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "yanosh",
            email = "yanosh@example.com",
            password = "secret123"
        });
        signup.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Login should refuse — email is not verified yet
        var blockedLogin = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "yanosh@example.com",
            password = "secret123"
        });
        blockedLogin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var blockedBody = await blockedLogin.Content.ReadFromJsonAsync<JsonElement>();
        blockedBody.GetProperty("code").GetString().Should().Be("EMAIL_NOT_VERIFIED");

        // 3. Pull the raw verification token straight from the outbox.
        var lastEmail = _factory.Emails.LastTo("yanosh@example.com");
        lastEmail.Should().NotBeNull();
        var rawToken = ExtractTokenFromEmail(lastEmail!.HtmlBody);

        // 4. Verify endpoint (the JSON one the SPA calls) — returns a token pair
        var verify = await _client.PostAsJsonAsync("/auth/verify-email", new { token = rawToken });
        verify.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await verify.Content.ReadFromJsonAsync<JsonElement>();
        verifyBody.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        verifyBody.GetProperty("user").GetProperty("emailVerified").GetBoolean().Should().BeTrue();

        // 5. Login now succeeds and we get a fresh pair
        var login = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "yanosh@example.com",
            password = "secret123"
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var access = loginBody.GetProperty("accessToken").GetString()!;
        var refresh = loginBody.GetProperty("refreshToken").GetString()!;

        // 6. /auth/me with the access token returns the user
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var me = await _client.GetFromJsonAsync<JsonElement>("/auth/me");
        me.GetProperty("username").GetString().Should().Be("yanosh");

        // 7. Refresh rotates the refresh token (the old one stops working)
        _client.DefaultRequestHeaders.Authorization = null;
        var refreshed = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshedBody = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
        var newRefresh = refreshedBody.GetProperty("refreshToken").GetString()!;
        newRefresh.Should().NotBe(refresh);

        // The original refresh token should now be revoked.
        var staleRefresh = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        staleRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var staleBody = await staleRefresh.Content.ReadFromJsonAsync<JsonElement>();
        staleBody.GetProperty("code").GetString().Should().Be("REFRESH_INVALID");

        // 8. Logout revokes the new refresh token too.
        var logout = await _client.PostAsJsonAsync("/auth/logout", new { refreshToken = newRefresh });
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var revokedRefresh = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = newRefresh });
        revokedRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signup_DuplicateEmail_Returns_EMAIL_EXISTS_Code()
    {
        await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "first",
            email = "dup@example.com",
            password = "secret123"
        });

        var second = await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "second",
            email = "dup@example.com",
            password = "secret456"
        });

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("EMAIL_EXISTS");
    }

    [Fact]
    public async Task VerifyEmail_ExpiredToken_Returns_TOKEN_EXPIRED()
    {
        // Sign the user up so a token exists, then mutate the token's expiry directly in the DB.
        await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "exp",
            email = "exp@example.com",
            password = "secret123"
        });

        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE email_verification_tokens SET expires_at = NOW() - INTERVAL '1 hour'");
        }

        var rawToken = ExtractTokenFromEmail(_factory.Emails.LastTo("exp@example.com")!.HtmlBody);
        var verify = await _client.PostAsJsonAsync("/auth/verify-email", new { token = rawToken });

        verify.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("TOKEN_EXPIRED");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns_BAD_CREDENTIALS()
    {
        await SignupAndVerifyAsync("bad@example.com", "right-password");

        var attempt = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "bad@example.com",
            password = "wrong-password"
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await attempt.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("BAD_CREDENTIALS");
    }

    [Fact]
    public async Task Signup_WithPasswordExceeding72Chars_FailsValidation()
    {
        var longPassword = new string('a', 73);
        var response = await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = "longpass",
            email = "longpass@example.com",
            password = longPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadAvatar_WithInvalidMagicBytes_Returns_INVALID_FILE_HEADER()
    {
        await SignupAndVerifyAsync("avatar@example.com", "password123");
        var login = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "avatar@example.com",
            password = "password123"
        });
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        var access = loginBody.GetProperty("accessToken").GetString()!;

        using var content = new MultipartFormDataContent();
        var fakeFileContent = new ByteArrayContent(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C });
        fakeFileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fakeFileContent, "file", "fake.png");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var response = await _client.PostAsync("/auth/me/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("INVALID_FILE_HEADER");
    }

    private async Task SignupAndVerifyAsync(string email, string password)
    {
        await _client.PostAsJsonAsync("/auth/signup", new
        {
            username = email.Split('@')[0],
            email,
            password
        });
        var token = ExtractTokenFromEmail(_factory.Emails.LastTo(email)!.HtmlBody);
        await _client.PostAsJsonAsync("/auth/verify-email", new { token });
    }

    private static string ExtractTokenFromEmail(string html)
    {
        // The template embeds the token as a URL query parameter; ride that.
        const string marker = "verify-email?token=";
        var idx = html.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Verification email did not embed a token URL");
        var start = idx + marker.Length;
        var end = html.IndexOfAny(new[] { '"', ' ', '\n', '<' }, start);
        return Uri.UnescapeDataString(html[start..end]);
    }
}
