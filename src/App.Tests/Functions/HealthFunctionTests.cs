using App.Api.Functions;
using App.Tests.PocAuth;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace App.Tests.Functions;

public sealed class HealthFunctionTests
{
    private readonly TestTimeProvider _time = new();

    [Fact]
    public async Task GetHealth_Always_Returns200WithOkStatusAndUtcTimestamp()
    {
        var request = TestHttpRequestData.CreateRequest();
        var function = new HealthFunction(NullLogger<HealthFunction>.Instance, _time);

        var response = await function.GetHealth(request, CancellationToken.None);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("status").GetString().Should().Be("ok");

        var utc = body.RootElement.GetProperty("utc").GetDateTimeOffset();
        utc.Should().Be(_time.GetUtcNow());
    }
}
