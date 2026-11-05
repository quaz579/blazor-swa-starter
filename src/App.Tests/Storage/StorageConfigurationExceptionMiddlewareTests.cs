using System.Text.Json;
using App.Api.Storage;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using NSubstitute;

namespace App.Tests.Storage;

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class StorageConfigurationExceptionMiddlewareTests
{
    [Theory]
    [InlineData("preview")]
    [InlineData("production")]
    [InlineData("unknown")]
    public async Task Invoke_StorageConfigurationException_Returns503WithSafeJsonBody(string mode)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var middleware = new StorageConfigurationExceptionMiddleware(_ => httpContext);
        var functionContext = Substitute.For<FunctionContext>();

        await middleware.Invoke(functionContext, _ => Task.FromException(
            new StorageConnectionConfigurationException("Storage configuration is invalid.", mode)));

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        httpContext.Response.ContentType.Should().StartWith("application/json");
        httpContext.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(httpContext.Response.Body);
        body.RootElement.GetProperty("error").GetString().Should().Be("Storage configuration is invalid.");
        body.RootElement.GetProperty("storageMode").GetString().Should().Be(mode);
        body.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("error", "storageMode");
    }
}
