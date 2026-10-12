using App.Api.PocAuth;
using AwesomeAssertions;

namespace App.Tests.PocAuth;

public sealed class PocPasswordPolicyTests
{
    [Theory]
    [InlineData("Ch@nageM3")]
    [InlineData("abcdefg1")]
    [InlineData("correct horse battery staple 9")]
    public void Validate_AcceptableCandidates_ReturnNull(string candidate) =>
        PocPasswordPolicy.Validate(candidate, "something-else-1").Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a1")]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    public void Validate_WeakOrMissingCandidates_ReturnAMessage(string? candidate) =>
        PocPasswordPolicy.Validate(candidate).Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void Validate_TooLong_ReturnsAMessage() =>
        PocPasswordPolicy.Validate(new string('a', PocPasswordPolicy.MaxLength) + "1").Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void Validate_SameAsCurrent_ReturnsAMessage() =>
        PocPasswordPolicy.Validate("Ch@nageM3", "Ch@nageM3").Should().NotBeNullOrWhiteSpace();
}
