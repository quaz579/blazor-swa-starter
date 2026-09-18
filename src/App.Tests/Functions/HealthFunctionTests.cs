using App.Api.Functions;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Functions;

public sealed class HealthFunctionTests
{
    private readonly HealthFunction _function = new(NullLogger<HealthFunction>.Instance);

    [Fact]
    public async Task GetHealth_Always_Returns200WithOkStatusAndUtcTimestamp()
    {
        // Arrange
        var request = TestHttpRequestData.CreateRequest();
        var before = DateTimeOffset.UtcNow;

        // Act
        var response = await _function.GetHealth(request, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("ok");

        var utc = body.RootElement.GetProperty("utc").GetDateTimeOffset();
        utc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
