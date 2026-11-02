using System.Net;
using System.Text;
using App.Core.Models;
using App.Web.Pages;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class ItemsPageTests : TestContext
{
    private void RegisterApiClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) =>
        Services.AddSingleton(new ItemsApiClient(DelegateHttpMessageHandler.CreateClient(handle)));

    [Theory]
    [InlineData("[null]", "null item")]
    [InlineData("null", "null item list")]
    [InlineData("[{\"id\":null,\"name\":\"Widget\"}]", "null id or name")]
    [InlineData("[{\"id\":\"item-id\",\"name\":null}]", "null id or name")]
    public void Items_InvalidListResponse_ShowsErrorWithoutRenderingRows(string json, string error)
    {
        RegisterApiClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

        var cut = RenderComponent<Items>();

        cut.Find("[data-testid='items-error']").TextContent.Should().Contain(error);
        cut.FindAll("[data-testid='item-row']").Should().BeEmpty();
    }

    [Fact]
    public async Task Items_WhileLoading_ShowsLoadingIndicator()
    {
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        RegisterApiClient((_, _) => tcs.Task);

        var cut = RenderComponent<Items>();

        cut.Find("[data-testid='items-loading']").Should().NotBeNull();

        await RenderCompletion.AfterAsync(cut, () => cut.FindAll("[data-testid=items-empty]").Count == 1,
            () => cut.InvokeAsync(() => tcs.SetResult(FakeApiResponses.Ok(new List<Item>()))));
        cut.Find("[data-testid='items-empty']").Should().NotBeNull();
    }

    [Fact]
    public void Items_ApiReturnsItems_RendersRowsWithNameAndDescription()
    {
        var items = new List<Item>
        {
            new() { Id = "1", Name = "First", Description = "First desc", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") },
            new() { Id = "2", Name = "Second", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z").AddMinutes(-1) },
        };
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Ok(items)));

        var cut = RenderComponent<Items>();

        cut.FindAll("[data-testid='item-row']").Should().HaveCount(2);

        var firstRow = cut.Find("[data-item-id='1']");
        firstRow.QuerySelector("[data-testid='item-name']")!.TextContent.Should().Be("First");
        firstRow.QuerySelector("[data-testid='item-description']")!.TextContent.Should().Be("First desc");

        var secondRow = cut.Find("[data-item-id='2']");
        secondRow.QuerySelector("[data-testid='item-description']").Should().BeNull();
    }

    [Fact]
    public void Items_ApiReturnsEmptyList_ShowsEmptyMessage()
    {
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Ok(new List<Item>())));

        var cut = RenderComponent<Items>();

        cut.Find("[data-testid='items-empty']").Should().NotBeNull();
    }

    [Fact]
    public void Items_ApiReturns500_ShowsErrorMessageIncludingStatusCode()
    {
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Error(HttpStatusCode.InternalServerError, "Failed to retrieve items")));

        var cut = RenderComponent<Items>();

        cut.Find("[data-testid='items-error']").TextContent.Should().Contain("500");
    }

    [Fact]
    public async Task Items_CreateForm_BlankName_ShowsValidationErrorWithoutCallingApi()
    {
        var postCount = 0;
        RegisterApiClient((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                postCount++;
            }
            return Task.FromResult(FakeApiResponses.Ok(new List<Item>()));
        });
        var cut = RenderComponent<Items>();
        cut.Find("[data-testid='items-empty']").Should().NotBeNull();

        await cut.Find("[data-testid='item-add-button']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='item-validation-error']").TextContent.Should().Be("Name is required.");
        postCount.Should().Be(0);
    }

    [Fact]
    public async Task Items_CreateForm_NameExceeds100Chars_ShowsValidationError()
    {
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Ok(new List<Item>())));
        var cut = RenderComponent<Items>();
        cut.Find("[data-testid='items-empty']").Should().NotBeNull();

        cut.Find("[data-testid='item-name-input']").Input(new string('a', 101));
        await cut.Find("[data-testid='item-add-button']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='item-validation-error']").TextContent.Should().Be("Name must be 100 characters or fewer.");
    }

    [Fact]
    public async Task Items_CreateForm_DescriptionExceeds1000Chars_ShowsValidationErrorWithoutCallingApi()
    {
        var postCount = 0;
        RegisterApiClient((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                postCount++;
            }
            return Task.FromResult(FakeApiResponses.Ok(new List<Item>()));
        });
        var cut = RenderComponent<Items>();
        cut.Find("[data-testid='items-empty']").Should().NotBeNull();

        cut.Find("[data-testid='item-name-input']").Input("Widget");
        cut.Find("[data-testid='item-description-input']").Input(new string('a', 1001));
        await cut.Find("[data-testid='item-add-button']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='item-validation-error']").TextContent.Should().Be("Description must be 1000 characters or fewer.");
        postCount.Should().Be(0);
    }

    [Fact]
    public async Task Items_CreateForm_ValidInput_CreatesItemAndReloadsList()
    {
        var created = new Item { Id = "new-1", Name = "Widget", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") };
        var postCount = 0;
        RegisterApiClient((req, _) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                postCount++;
                return Task.FromResult(FakeApiResponses.Created(created));
            }
            return Task.FromResult(FakeApiResponses.Ok(postCount == 0 ? new List<Item>() : new List<Item> { created }));
        });
        var cut = RenderComponent<Items>();
        cut.Find("[data-testid='items-empty']").Should().NotBeNull();

        cut.Find("[data-testid='item-name-input']").Input("Widget");
        await cut.Find("[data-testid='item-add-button']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='item-row']").Should().NotBeNull();
        postCount.Should().Be(1);
        cut.Find("[data-testid='item-name']").TextContent.Should().Be("Widget");
    }

    [Fact]
    public async Task Items_DeleteButtonClicked_CallsDeleteAndReloadsList()
    {
        var item = new Item { Id = "del-1", Name = "ToDelete", CreatedAt = DateTimeOffset.Parse("2026-01-01T12:00:00Z") };
        var deleteCount = 0;
        RegisterApiClient((req, _) =>
        {
            if (req.Method == HttpMethod.Delete)
            {
                deleteCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(FakeApiResponses.Ok(deleteCount == 0 ? new List<Item> { item } : new List<Item>()));
        });
        var cut = RenderComponent<Items>();
        cut.Find("[data-testid='item-row']").Should().NotBeNull();

        await cut.Find("[data-testid='item-delete-button']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='items-empty']").Should().NotBeNull();
        deleteCount.Should().Be(1);
    }

    [Fact]
    public void Items_TransportFailureOnLoad_ShowsInlineErrorWithoutStatusCode()
    {
        RegisterApiClient((_, _) => throw new HttpRequestException("connection reset"));

        var cut = RenderComponent<Items>();

        {
            var error = cut.Find("[data-testid='items-error']").TextContent;
            error.Should().Contain("Could not reach the API");
            error.Should().NotContain("HTTP");
        }
    }

    [Fact]
    public void Items_TimeoutOnLoad_ShowsInlineError()
    {
        RegisterApiClient((_, _) => throw new TaskCanceledException("timed out"));

        var cut = RenderComponent<Items>();

        cut.Find("[data-testid='items-error']").TextContent.Should().Contain("Could not reach the API");
    }

    [Fact]
    public void Items_MalformedBodyOnLoad_ShowsInlineError()
    {
        RegisterApiClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"),
        }));

        var cut = RenderComponent<Items>();

        cut.Find("[data-testid='items-error']").TextContent.Should().Contain("unexpected response");
    }

    [Fact]
    public async Task Items_TransportFailureOnCreate_ShowsInlineError()
    {
        RegisterApiClient((req, _) => req.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : Task.FromResult(FakeApiResponses.Ok(new List<Item>())));
        var cut = RenderComponent<Items>();
        cut.Find("[data-testid='items-empty']").Should().NotBeNull();
        cut.Find("[data-testid='item-name-input']").Input("Widget");

        await cut.Find("[data-testid='item-add-button']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.Find("[data-testid='items-error']").TextContent.Should().Contain("Could not reach the API");
    }
}
