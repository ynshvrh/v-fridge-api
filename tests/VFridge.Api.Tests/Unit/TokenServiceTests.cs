using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Unit;

public class TokenServiceTests
{
    private static TokenService Build(JwtOptions? opts = null)
    {
        opts ??= new JwtOptions
        {
            Secret = "0123456789abcdef0123456789abcdef", // exactly 32 chars
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenMinutes = 5,
            RefreshTokenDays = 7
        };
        return new TokenService(Options.Create(opts));
    }

    [Fact]
    public void IssueAccessToken_Carries_Expected_Claims()
    {
        var svc = Build();

        var (raw, expires) = svc.IssueAccessToken(userId: 42, username: "yanosh", email: "y@example.com");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        jwt.Subject.Should().Be("42");
        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.UniqueName).Value.Should().Be("yanosh");
        jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value.Should().Be("y@example.com");
        jwt.Claims.Should().ContainSingle(c => c.Type == JwtRegisteredClaimNames.Jti, "jti must be unique per token");

        expires.Should().BeAfter(DateTime.UtcNow);
        expires.Should().BeBefore(DateTime.UtcNow.AddMinutes(6));
    }

    [Fact]
    public void IssueAccessToken_Throws_When_Secret_TooShort()
    {
        var svc = Build(new JwtOptions { Secret = "too-short", Issuer = "i", Audience = "a", AccessTokenMinutes = 5, RefreshTokenDays = 7 });

        var act = () => svc.IssueAccessToken(1, "u", "e@e");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Jwt:Secret*");
    }

    [Fact]
    public void GenerateRefreshToken_Returns_Distinct_Values()
    {
        var svc = Build();

        var batch = Enumerable.Range(0, 100).Select(_ => svc.GenerateRefreshToken()).ToList();
        batch.Distinct().Should().HaveCount(100, "every refresh token must be unique");
        batch.Should().AllSatisfy(t => t.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Hash_Is_Deterministic_And_Matches_Sha256_Hex_Length()
    {
        var svc = Build();

        var a = svc.Hash("hello");
        var b = svc.Hash("hello");
        a.Should().Be(b);
        a.Length.Should().Be(64, "SHA-256 hex is 32 bytes = 64 hex chars");
    }

    [Fact]
    public void Hash_DistinguishesInputs()
    {
        var svc = Build();
        svc.Hash("a").Should().NotBe(svc.Hash("b"));
    }
}
