using System.Net;
using App.Api.Functions;
using App.Core.Models;
using App.Core.Storage;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace App.Tests.Functions;

public sealed class ItemsFunctionTests
{
    private readonly IBlobJsonStore<Item> _store = Substitute.For<IBlobJsonStore<Item>>();
    private readonly ItemsFunction _function;

    public ItemsFunctionTests()
    {
        _function = new ItemsFunction(NullLogger<ItemsFunction>.Instance, _store);
    }

    [Fact]
    public async Task GetItems_NoItemsStored_Returns200WithEmptyArray()
    {
        // Arrange
        _store.ListKeysAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<string>());
        var request = TestHttpRequestData.CreateRequest();

        // Act
        var response = await _function.GetItems(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await TestHttpRequestData.ReadBodyAsync<List<Item>>(response);
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItems_MultipleItems_ReturnsNewestCreatedAtFirst()
    {
        // Arrange
        var oldest = new Item { Id = "1", Name = "Oldest", CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var middle = new Item { Id = "2", Name = "Middle", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var newest = new Item { Id = "3", Name = "Newest", CreatedAt = DateTimeOffset.UtcNow };

        _store.ListKeysAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<string> { "1", "2", "3" });
        _store.GetAsync("1", Arg.Any<CancellationToken>()).Returns(oldest);
        _store.GetAsync("2", Arg.Any<CancellationToken>()).Returns(middle);
        _store.GetAsync("3", Arg.Any<CancellationToken>()).Returns(newest);
        var request = TestHttpRequestData.CreateRequest();

        // Act
        var response = await _function.GetItems(request, CancellationToken.None);

        // Assert
        var items = await TestHttpRequestData.ReadBodyAsync<List<Item>>(response);
        items!.Select(i => i.Id).Should().Equal("3", "2", "1");
    }

    [Fact]
    public async Task GetItems_StoreThrows_Returns500WithErrorBody()
    {
        // Arrange
        _store.ListKeysAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("storage down")));
        var request = TestHttpRequestData.CreateRequest();

        // Act
        var response = await _function.GetItems(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetItem_ItemExists_Returns200WithItem()
    {
        // Arrange
        var item = new Item { Id = "42", Name = "Found", CreatedAt = DateTimeOffset.UtcNow };
        _store.GetAsync("42", Arg.Any<CancellationToken>()).Returns(item);
        var request = TestHttpRequestData.CreateRequest();

        // Act
        var response = await _function.GetItem(request, "42", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHttpRequestData.ReadBodyAsync<Item>(response);
        body!.Id.Should().Be("42");
    }

    [Fact]
    public async Task GetItem_ItemDoesNotExist_Returns404WithErrorBody()
    {
        // Arrange
        _store.GetAsync("missing", Arg.Any<CancellationToken>()).Returns((Item?)null);
        var request = TestHttpRequestData.CreateRequest();

        // Act
        var response = await _function.GetItem(request, "missing", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Contain("missing");
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData("../../secrets")]
    [InlineData("a/b")]
    public async Task GetItem_MalformedId_Returns400WithErrorBody(string id)
    {
        // Arrange
        _store.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Item?>(new ArgumentException("Key must not contain path separators or '..'.", "key")));
        var request = TestHttpRequestData.CreateRequest();

        // Act
        var response = await _function.GetItem(request, id, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        var error = body.RootElement.GetProperty("error").GetString();
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().NotContain("Parameter");
    }

    [Fact]
    public async Task CreateItem_ValidBody_Returns201WithLocationHeaderAndCreatedItem()
    {
        // Arrange
        var request = TestHttpRequestData.CreateRequest("POST", jsonBody: new { name = "  Widget  ", description = "A widget" });

        // Act
        var response = await _function.CreateItem(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.TryGetValues("Location", out var locations).Should().BeTrue();
        var location = locations!.Single();

        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        var id = body.RootElement.GetProperty("id").GetString();
        id.Should().MatchRegex("^[0-9a-f]{32}$");
        location.Should().Be($"/api/items/{id}");
        body.RootElement.GetProperty("name").GetString().Should().Be("Widget");
        body.RootElement.GetProperty("description").GetString().Should().Be("A widget");
        body.RootElement.TryGetProperty("createdAt", out _).Should().BeTrue();

        await _store.Received(1).PutAsync(id!, Arg.Is<Item>(i => i.Name == "Widget"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateItem_NameBlank_Returns400WithErrorBody(string? name)
    {
        // Arrange
        var request = TestHttpRequestData.CreateRequest("POST", jsonBody: new { name });

        // Act
        var response = await _function.CreateItem(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Be("Name is required.");
        await _store.DidNotReceive().PutAsync(Arg.Any<string>(), Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateItem_MissingBody_Returns400WithErrorBody()
    {
        // Arrange
        var request = TestHttpRequestData.CreateRequest("POST");

        // Act
        var response = await _function.CreateItem(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateItem_NameExactly100Chars_Returns201()
    {
        // Arrange
        var name = new string('a', 100);
        var request = TestHttpRequestData.CreateRequest("POST", jsonBody: new { name });

        // Act
        var response = await _function.CreateItem(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateItem_NameExceeds100Chars_Returns400WithErrorBody()
    {
        // Arrange
        var name = new string('a', 101);
        var request = TestHttpRequestData.CreateRequest("POST", jsonBody: new { name });

        // Act
        var response = await _function.CreateItem(request, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Be("Name must be 100 characters or fewer.");
    }

    [Fact]
    public async Task DeleteItem_ItemExists_Returns204NoContent()
    {
        // Arrange
        _store.DeleteAsync("42", Arg.Any<CancellationToken>()).Returns(true);
        var request = TestHttpRequestData.CreateRequest("DELETE");

        // Act
        var response = await _function.DeleteItem(request, "42", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteItem_ItemDoesNotExist_Returns404WithErrorBody()
    {
        // Arrange
        _store.DeleteAsync("missing", Arg.Any<CancellationToken>()).Returns(false);
        var request = TestHttpRequestData.CreateRequest("DELETE");

        // Act
        var response = await _function.DeleteItem(request, "missing", CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        body.RootElement.GetProperty("error").GetString().Should().Contain("missing");
    }

    [Theory]
    [InlineData("a..b")]
    [InlineData("../../secrets")]
    [InlineData("a/b")]
    public async Task DeleteItem_MalformedId_Returns400WithErrorBody(string id)
    {
        // Arrange
        _store.DeleteAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new ArgumentException("Key must not contain path separators or '..'.", "key")));
        var request = TestHttpRequestData.CreateRequest("DELETE");

        // Act
        var response = await _function.DeleteItem(request, id, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await TestHttpRequestData.ReadBodyAsJsonAsync(response);
        var error = body.RootElement.GetProperty("error").GetString();
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().NotContain("Parameter");
    }
}
