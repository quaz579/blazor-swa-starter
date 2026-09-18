using System.Net;
using System.Text;
using App.Core.Models;
using App.Web.Services;
using AwesomeAssertions;

namespace App.Tests.Web;

public sealed class ItemsApiClientTests
{
    private static ItemsApiClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(handle)) { BaseAddress = new Uri("https://api.test/") };
        return new ItemsApiClient(httpClient);
    }

    [Fact]
    public async Task CheckHealthAsync_HealthEndpointReturns200_ReturnsTrue()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await client.CheckHealthAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHealthAsync_HealthEndpointReturns500_ReturnsFalse()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await client.CheckHealthAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckHealthAsync_NetworkFailure_ReturnsFalse()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("boom"));

        var result = await client.CheckHealthAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetItemsAsync_ApiReturns200_ReturnsDeserializedItems()
    {
        var items = new List<Item> { new() { Id = "1", Name = "Widget", CreatedAt = DateTimeOffset.UtcNow } };
        var client = CreateClient((_, _) => Task.FromResult(FakeApiResponses.Ok(items)));

        var result = await client.GetItemsAsync();

        result.Should().ContainSingle(i => i.Id == "1" && i.Name == "Widget");
    }

    [Fact]
    public async Task GetItemsAsync_ApiReturns500WithErrorBody_ThrowsApiExceptionCarryingStatusAndMessage()
    {
        var client = CreateClient((_, _) => Task.FromResult(FakeApiResponses.Error(HttpStatusCode.InternalServerError, "Failed to retrieve items")));

        var act = async () => await client.GetItemsAsync();

        var assertion = await act.Should().ThrowAsync<ApiException>();
        assertion.Which.StatusCode.Should().Be(500);
        assertion.Which.Message.Should().Be("Failed to retrieve items");
    }

    [Fact]
    public async Task CreateItemAsync_ApiReturns400WithErrorBody_ThrowsApiExceptionCarrying400()
    {
        var client = CreateClient((_, _) => Task.FromResult(FakeApiResponses.Error(HttpStatusCode.BadRequest, "Name is required.")));

        var act = async () => await client.CreateItemAsync(new CreateItemRequest { Name = "" });

        var assertion = await act.Should().ThrowAsync<ApiException>();
        assertion.Which.StatusCode.Should().Be(400);
        assertion.Which.Message.Should().Be("Name is required.");
    }

    [Fact]
    public async Task CreateItemAsync_ApiReturns500WithNonJsonBody_ThrowsApiExceptionWithGenericMessage()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>oops</html>", Encoding.UTF8, "text/html"),
        }));

        var act = async () => await client.CreateItemAsync(new CreateItemRequest { Name = "Widget" });

        var assertion = await act.Should().ThrowAsync<ApiException>();
        assertion.Which.StatusCode.Should().Be(500);
        assertion.Which.Message.Should().Be("Request failed with status 500.");
    }

    [Fact]
    public async Task DeleteItemAsync_ApiReturns404_ThrowsApiExceptionCarrying404()
    {
        var client = CreateClient((_, _) => Task.FromResult(FakeApiResponses.Error(HttpStatusCode.NotFound, "Item 'x' was not found.")));

        var act = async () => await client.DeleteItemAsync("x");

        var assertion = await act.Should().ThrowAsync<ApiException>();
        assertion.Which.StatusCode.Should().Be(404);
    }
}
