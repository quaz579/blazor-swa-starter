using System.Net;
using System.Text;
using App.Core.Models;
using App.Web.Services;
using AwesomeAssertions;
using NSubstitute;

namespace App.Tests.Web;

public sealed class ItemsApiClientTests
{
    private static ItemsApiClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(handle)) { BaseAddress = new Uri("https://api.test/") };
        return new ItemsApiClient(httpClient);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{},null]")]
    public async Task GetItemsAsync_ResponseContainsNullItem_ThrowsApiExceptionPreserving200(string json)
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

        var act = () => client.GetItemsAsync();

        var exception = await act.Should().ThrowExactlyAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(200);
        exception.Which.Message.Should().Be("The API returned an item list containing a null item.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetItemsAsync_ErrorBodyReadFails_PropagatesTheOperationalFailure(bool cancelled)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("error-body stream failed");
        var stream = Substitute.For<Stream>();
        stream.CanRead.Returns(true);
        stream.ReadAsync(Arg.Any<Memory<byte>>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (cancelled)
            {
                cancellation.Cancel();
                return ValueTask.FromCanceled<int>(cancellation.Token);
            }
            return ValueTask.FromException<int>(failure);
        });
        using var content = new LateFailureContent(stream);
        await content.ReadAsStreamAsync();
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content }));

        var act = () => client.GetItemsAsync(cancellation.Token);

        if (cancelled)
        {
            var exception = await act.Should().ThrowExactlyAsync<TaskCanceledException>();
            exception.Which.CancellationToken.Should().Be(cancellation.Token);
        }
        else
        {
            var exception = await act.Should().ThrowExactlyAsync<IOException>();
            exception.Which.Should().BeSameAs(failure);
        }
    }

    private sealed class LateFailureContent : HttpContent
    {
        private readonly Stream _stream;

        public LateFailureContent(Stream stream) => _stream = stream;

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes("{}")).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task CreateItemAsync_CreatedResponseContainsNull_ThrowsApiExceptionPreserving201()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        }));

        var act = () => client.CreateItemAsync(new CreateItemRequest { Name = "Widget" });

        var exception = await act.Should().ThrowAsync<ApiException>();
        exception.Which.StatusCode.Should().Be(201);
        exception.Which.Message.Should().Be("The API returned an empty body for a created item.");
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
