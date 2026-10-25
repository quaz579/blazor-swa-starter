using App.Api.PocAuth;
using AwesomeAssertions;

namespace App.Tests.PocAuth;

[Collection("PocAuthEnv")]
public sealed class PocAuthSettingsTests : IDisposable
{
    private readonly string? _originalEnabled = Environment.GetEnvironmentVariable(PocAuthSettings.EnabledSetting);

    public void Dispose() => Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, _originalEnabled);

    [Theory]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("false", false)]
    [InlineData(" FaLsE ", false)]
    public void IsEnabled_ExplicitFlag_ReturnsNormalizedBoolean(string flag, bool expected)
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, flag);

        PocAuthSettings.IsEnabled.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid")]
    public void IsEnabled_MissingOrUnrecognizedFlag_FallsBackToSeedPresence(string? flag)
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, flag);

        PocAuthSettings.IsEnabled.Should().Be(PocAuthSeed.Users.Count > 0);
    }
}
