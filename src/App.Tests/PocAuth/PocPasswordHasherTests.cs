using App.Api.PocAuth;
using AwesomeAssertions;

namespace App.Tests.PocAuth;

public sealed class PocPasswordHasherTests
{
    private static readonly byte[] VectorSalt = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");

    [Fact]
    public void Derive_SpikeVector_MatchesPbkdf2Sha512At210000()
    {
        var key = PocPasswordHasher.Derive("poc-demo-pw", VectorSalt, 210_000);

        Convert.ToHexString(key).ToLowerInvariant()
            .Should().Be("13bb45fd4e2509c7d685230b8fb6e21bbae829aa1e47fd015326c437bfe6339b");
    }

    [Fact]
    public void DefaultIterations_Is220000()
    {
        PocPasswordHasher.DefaultIterations.Should().Be(220_000);
    }

    [Fact]
    public void Hash_UsesDefaultIterationsAndAPerCallSalt()
    {
        var first = PocPasswordHasher.Hash("Ch@nageM3");
        var second = PocPasswordHasher.Hash("Ch@nageM3");

        first.Iterations.Should().Be(220_000);
        first.Salt.Should().NotBe(second.Salt);
        first.Hash.Should().NotBe(second.Hash);
        Convert.FromBase64String(first.Salt).Should().HaveCount(16);
        Convert.FromBase64String(first.Hash).Should().HaveCount(32);
    }

    [Fact]
    public void Hash_MatchesIndependentDerivationAt220000()
    {
        var (salt, hash, iterations) = PocPasswordHasher.Hash("Ch@nageM3");

        var expected = PocPasswordHasher.Derive("Ch@nageM3", Convert.FromBase64String(salt), 220_000);

        iterations.Should().Be(220_000);
        hash.Should().Be(Convert.ToBase64String(expected));
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var (salt, hash, iterations) = PocPasswordHasher.Hash("Ch@nageM3");

        PocPasswordHasher.Verify("Ch@nageM3", salt, hash, iterations).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var (salt, hash, iterations) = PocPasswordHasher.Hash("Ch@nageM3");

        PocPasswordHasher.Verify("wrong", salt, hash, iterations).Should().BeFalse();
    }

    [Fact]
    public void Verify_HonorsTheIterationCountStoredPerRow()
    {
        var (salt, hash, _) = PocPasswordHasher.Hash("Ch@nageM3", iterations: 1_000);

        PocPasswordHasher.Verify("Ch@nageM3", salt, hash, 1_000).Should().BeTrue();
        PocPasswordHasher.Verify("Ch@nageM3", salt, hash, 220_000).Should().BeFalse();
    }
}
