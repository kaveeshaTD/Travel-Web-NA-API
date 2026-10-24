using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Unit;

public class PasswordHasherTests
{
    private readonly PasswordHasher _cheap = new(iterations: 1_000);

    [Fact]
    public void Correct_password_verifies()
    {
        var hash = _cheap.Hash("Admin@12345");

        Assert.True(_cheap.Verify("Admin@12345", hash));
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        var hash = _cheap.Hash("Admin@12345");

        Assert.False(_cheap.Verify("admin@12345", hash));
        Assert.False(_cheap.Verify(string.Empty, hash));
    }

    [Fact]
    public void Same_password_gets_a_different_salt_each_time()
    {
        Assert.NotEqual(_cheap.Hash("Customer123"), _cheap.Hash("Customer123"));
    }

    [Fact]
    public void Hash_uses_the_versioned_format_and_hides_the_password()
    {
        var hash = _cheap.Hash("Customer123");

        Assert.StartsWith("v1.1000.", hash, StringComparison.Ordinal);
        Assert.Equal(4, hash.Split('.').Length);
        Assert.DoesNotContain("Customer123", hash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("v2.1000.AAAA.AAAA")]
    [InlineData("v1.abc.AAAA.AAAA")]
    [InlineData("v1.1000.!!!.!!!")]
    public void Malformed_hashes_never_verify(string hash)
    {
        Assert.False(_cheap.Verify("anything", hash));
    }

    [Fact]
    public void Weaker_hashes_need_a_rehash_and_current_ones_do_not()
    {
        var current = new PasswordHasher();

        Assert.True(current.NeedsRehash(_cheap.Hash("x1234567")));
        Assert.False(current.NeedsRehash(current.Hash("x1234567")));
        Assert.True(current.NeedsRehash("garbage"));
    }
}
