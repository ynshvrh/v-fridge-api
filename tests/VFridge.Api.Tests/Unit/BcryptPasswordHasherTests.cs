using VFridge.Api.Services;

namespace VFridge.Api.Tests.Unit;

public class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_Then_Verify_Succeeds()
    {
        const string password = "correct horse battery staple";

        var hash = _hasher.Hash(password);

        _hasher.Verify(password, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = _hasher.Hash("right");
        _hasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_Is_Salted_Each_Call()
    {
        var a = _hasher.Hash("same");
        var b = _hasher.Hash("same");
        a.Should().NotBe(b, "BCrypt embeds a random salt per call");
        _hasher.Verify("same", a).Should().BeTrue();
        _hasher.Verify("same", b).Should().BeTrue();
    }

    [Fact]
    public void Verify_DoesNotThrow_On_GarbageHash()
    {
        // Real-world: legacy rows with corrupted hashes shouldn't 500 the request.
        _hasher.Verify("whatever", "not-a-bcrypt-hash").Should().BeFalse();
    }

    [Fact]
    public void Produces_Bcrypt_Hash_Compatible_With_NextJs_Output()
    {
        // The hash must start with one of the $2a$ / $2b$ / $2y$ prefixes — bcryptjs (Next.js side)
        // and BCrypt.Net both accept these, so a future library swap won't silently break sign-in
        // for users who registered earlier.
        var hash = _hasher.Hash("anything");
        hash.Should().StartWith("$2");
    }
}
