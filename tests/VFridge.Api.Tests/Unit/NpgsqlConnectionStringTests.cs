using VFridge.Api.Infrastructure;

namespace VFridge.Api.Tests.Unit;

public class NpgsqlConnectionStringTests
{
    [Fact]
    public void PlainKeyValue_PassesThrough_Unchanged()
    {
        const string raw = "Host=localhost;Port=5432;Database=app;Username=u;Password=p;SslMode=Disable;";
        NpgsqlConnectionString.Normalize(raw).Should().Be(raw);
    }

    [Fact]
    public void LibpqUri_Rewrites_To_KeyValue_Form()
    {
        const string raw = "postgresql://alice:secret@db.example.com:6543/app?sslmode=require";

        var result = NpgsqlConnectionString.Normalize(raw);

        result.Should().Contain("Host=db.example.com");
        result.Should().Contain("Port=6543");
        result.Should().Contain("Database=app");
        result.Should().Contain("Username=alice");
        result.Should().Contain("Password=secret");
        result.Should().Contain("SslMode=Require");
        result.Should().Contain("Pooling=true");
    }

    [Fact]
    public void Missing_Port_Defaults_To_5432()
    {
        var result = NpgsqlConnectionString.Normalize("postgresql://u:p@host/db");
        result.Should().Contain("Port=5432");
    }

    [Fact]
    public void PasswordWithSpecialChars_IsUrlDecoded()
    {
        // %23 = '#', %40 = '@'. Real-world Neon URLs include encoded passwords.
        var result = NpgsqlConnectionString.Normalize("postgresql://u:p%23%40w@host/db?sslmode=require");
        result.Should().Contain("Password=p#@w");
    }

    [Theory]
    [InlineData("require", "Require")]
    [InlineData("verify-ca", "VerifyCA")]
    [InlineData("verify-full", "VerifyFull")]
    [InlineData("disable", "Disable")]
    [InlineData("bogus-value", "Require")] // unknown values fall back to Require
    public void SslMode_Is_Mapped_From_Query(string libpq, string expected)
    {
        var result = NpgsqlConnectionString.Normalize($"postgresql://u:p@host/db?sslmode={libpq}");
        result.Should().Contain($"SslMode={expected}");
    }

    [Fact]
    public void UnknownQueryParams_Are_Silently_Dropped()
    {
        // channel_binding is the param Neon adds that breaks Npgsql 10. We don't carry it through.
        var result = NpgsqlConnectionString.Normalize("postgresql://u:p@host/db?sslmode=require&channel_binding=require");
        result.Should().NotContain("channel_binding");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_IsReturnedAsIs(string raw)
    {
        NpgsqlConnectionString.Normalize(raw).Should().Be(raw);
    }
}
