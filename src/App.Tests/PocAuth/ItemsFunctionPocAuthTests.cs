using System.Net;
using App.Api.Functions;
using App.Api.PocAuth;
using App.Core.Models;
using App.Core.Storage;
using App.Tests.Functions;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace App.Tests.PocAuth;

[Collection("PocAuthEnv")]
public sealed class ItemsFunctionPocAuthTests : IDisposable
{
    private readonly string? _originalEnabled = Environment.GetEnvironmentVariable(PocAuthSettings.EnabledSetting);
    private readonly IBlobJsonStore<Item> _items = Substitute.For<IBlobJsonStore<Item>>();
    private readonly InMemoryPocAuthStore _store = new();
    private readonly PocAuthService _auth;
    private readonly ItemsFunction _function;

    public ItemsFunctionPocAuthTests()
    {
        _auth = new PocAuthService(_store, new TestTimeProvider(), []);
        _function = new ItemsFunction(NullLogger<ItemsFunction>.Instance, _items, _auth);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, _originalEnabled);

    [Fact]
    public async Task Writes_WhenAuthEnabledAndAnonymous_Return401AndDoNotTouchTheStore()
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, "true");

        var create = await _function.CreateItem(TestHttpRequestData.CreateRequest("POST", jsonBody: new { name = "x" }), CancellationToken.None);
        var delete = await _function.DeleteItem(TestHttpRequestData.CreateRequest("DELETE"), "abc", CancellationToken.None);

        create.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        delete.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _items.DidNotReceiveWithAnyArgs().PutAsync(default!, default!);
        await _items.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
    }

    [Fact]
    public async Task Writes_WhenAuthEnabledAndSignedIn_Succeed()
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, "true");
        var (token, _) = await _auth.CreateSessionAsync(new PocPrincipal("admin", ["admin"]));
        var req = TestHttpRequestData.CreateRequest("POST", jsonBody: new { name = "x" });
        req.Headers.Add("Cookie", $"poc_session={token}");

        var create = await _function.CreateItem(req, CancellationToken.None);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Writes_WhenAuthDisabled_BehaveExactlyAsBefore()
    {
        Environment.SetEnvironmentVariable(PocAuthSettings.EnabledSetting, "false");

        var create = await _function.CreateItem(TestHttpRequestData.CreateRequest("POST", jsonBody: new { name = "x" }), CancellationToken.None);

        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
