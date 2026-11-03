using App.Api;
using AwesomeAssertions;

namespace App.Tests;

public class AppEnvironmentTests
{
    [Fact]
    public void Normalize_UnsetValue_ReturnsDevelopment()
    {
        var mode = AppEnvironment.Normalize(null);

        mode.Should().Be("development");
    }

    [Fact]
    public void Normalize_ProductionValue_ReturnsProduction()
    {
        var mode = AppEnvironment.Normalize("production");

        mode.Should().Be("production");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("prod")]
    [InlineData("staging")]
    [InlineData("development")]
    [InlineData("ProductionX")]
    [InlineData("arbitrary-value")]
    public void Normalize_NonProductionValue_ReturnsDevelopment(string rawValue)
    {
        var mode = AppEnvironment.Normalize(rawValue);

        mode.Should().Be("development");
    }

    [Theory]
    [InlineData(" Production ")]
    [InlineData("PRODUCTION")]
    public void Normalize_ProductionWithWhitespaceOrCasing_ReturnsProduction(string rawValue)
    {
        var mode = AppEnvironment.Normalize(rawValue);

        mode.Should().Be("production");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("\t\r\n", false)]
    [InlineData("production", true)]
    [InlineData(" Production ", true)]
    [InlineData("PRODUCTION", true)]
    [InlineData("prod", false)]
    [InlineData("staging", false)]
    [InlineData("development", false)]
    [InlineData("ProductionX", false)]
    [InlineData("arbitrary-value", false)]
    public void IsProductionValue_RawValue_MatchesNormalizedMode(string? rawValue, bool expected)
    {
        var isProduction = AppEnvironment.IsProductionValue(rawValue);

        isProduction.Should().Be(expected);
        isProduction.Should().Be(AppEnvironment.Normalize(rawValue) == "production");
    }
}
