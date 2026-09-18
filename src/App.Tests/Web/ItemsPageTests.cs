using System.Net;
using App.Core.Models;
using App.Web.Pages;
using App.Web.Services;
using AwesomeAssertions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace App.Tests.Web;

public sealed class ItemsPageTests : TestContext
{
    private void RegisterApiClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
    {
        var httpClient = new HttpClient(new DelegateHttpMessageHandler(handle)) { BaseAddress = new Uri("https://api.test/") };
        Services.AddSingleton(new ItemsApiClient(httpClient));
    }

    [Fact]
    public void Items_WhileLoading_ShowsLoadingIndicator()
    {
        // Arrange
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        RegisterApiClient((_, _) => tcs.Task);

        // Act
        var cut = RenderComponent<Items>();

        // Assert
        cut.Find("[data-testid='items-loading']").Should().NotBeNull();

        tcs.SetResult(FakeApiResponses.Ok(new List<Item>()));
        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));
    }

    [Fact]
    public void Items_ApiReturnsItems_RendersRowsWithNameAndDescription()
    {
        // Arrange
        var items = new List<Item>
        {
            new() { Id = "1", Name = "First", Description = "First desc", CreatedAt = DateTimeOffset.UtcNow },
            new() { Id = "2", Name = "Second", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) },
        };
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Ok(items)));

        // Act
        var cut = RenderComponent<Items>();

        // Assert
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='item-row']").Should().HaveCount(2));

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

        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));
    }

    [Fact]
    public void Items_ApiReturns500_ShowsErrorMessageIncludingStatusCode()
    {
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Error(HttpStatusCode.InternalServerError, "Failed to retrieve items")));

        var cut = RenderComponent<Items>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='items-error']").TextContent.Should().Contain("500"));
    }

    [Fact]
    public void Items_CreateForm_BlankName_ShowsValidationErrorWithoutCallingApi()
    {
        // Arrange
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
        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));

        // Act
        cut.Find("[data-testid='item-add-button']").Click();

        // Assert
        cut.Find("[data-testid='item-validation-error']").TextContent.Should().Be("Name is required.");
        postCount.Should().Be(0);
    }

    [Fact]
    public void Items_CreateForm_NameExceeds100Chars_ShowsValidationError()
    {
        // Arrange
        RegisterApiClient((_, _) => Task.FromResult(FakeApiResponses.Ok(new List<Item>())));
        var cut = RenderComponent<Items>();
        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));

        // Act
        cut.Find("[data-testid='item-name-input']").Input(new string('a', 101));
        cut.Find("[data-testid='item-add-button']").Click();

        // Assert
        cut.Find("[data-testid='item-validation-error']").TextContent.Should().Be("Name must be 100 characters or fewer.");
    }

    [Fact]
    public void Items_CreateForm_DescriptionExceeds1000Chars_ShowsValidationErrorWithoutCallingApi()
    {
        // Arrange
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
        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));

        // Act
        cut.Find("[data-testid='item-name-input']").Input("Widget");
        cut.Find("[data-testid='item-description-input']").Input(new string('a', 1001));
        cut.Find("[data-testid='item-add-button']").Click();

        // Assert
        cut.Find("[data-testid='item-validation-error']").TextContent.Should().Be("Description must be 1000 characters or fewer.");
        postCount.Should().Be(0);
    }

    [Fact]
    public void Items_CreateForm_ValidInput_CreatesItemAndReloadsList()
    {
        // Arrange
        var created = new Item { Id = "new-1", Name = "Widget", CreatedAt = DateTimeOffset.UtcNow };
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
        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));

        // Act
        cut.Find("[data-testid='item-name-input']").Input("Widget");
        cut.Find("[data-testid='item-add-button']").Click();

        // Assert
        cut.WaitForAssertion(() => cut.Find("[data-testid='item-row']"));
        postCount.Should().Be(1);
        cut.Find("[data-testid='item-name']").TextContent.Should().Be("Widget");
    }

    [Fact]
    public void Items_DeleteButtonClicked_CallsDeleteAndReloadsList()
    {
        // Arrange
        var item = new Item { Id = "del-1", Name = "ToDelete", CreatedAt = DateTimeOffset.UtcNow };
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
        cut.WaitForAssertion(() => cut.Find("[data-testid='item-row']"));

        // Act
        cut.Find("[data-testid='item-delete-button']").Click();

        // Assert
        cut.WaitForAssertion(() => cut.Find("[data-testid='items-empty']"));
        deleteCount.Should().Be(1);
    }
}
