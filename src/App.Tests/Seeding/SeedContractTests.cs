using System.Net;
using System.Text.Json;
using App.Api;
using App.Api.Functions;
using App.Api.Seeding;
using App.Core.Models;
using App.Core.Storage;
using App.Tests.Storage;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace App.Tests.Seeding;

[Collection(StorageConnectionEnvironmentCollection.Name)]
public sealed class SeedContractTests : IDisposable
{
    private readonly string? _environment = Environment.GetEnvironmentVariable("APP_ENVIRONMENT");
    private readonly string? _enabled = Environment.GetEnvironmentVariable("TEST_RESET_ENABLED");

    public SeedContractTests()
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", "development");
        Environment.SetEnvironmentVariable("TEST_RESET_ENABLED", "true");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", _environment);
        Environment.SetEnvironmentVariable("TEST_RESET_ENABLED", _enabled);
    }

    private static SeedService Service(ISeedData? seed = null, params ISeedStore[] stores) =>
        new(seed ?? new EmptySeedData(), stores, NullLogger<SeedService>.Instance);

    private static HttpRequest Request(string peer = "127.0.0.1", string? forwarded = null, string method = "POST")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Method = method;
        if (forwarded is not null) context.Request.Headers["X-Forwarded-For"] = forwarded;
        return context.Request;
    }

    [Fact]
    public async Task EnsureSeededAsync_CalledTwice_UpsertsIdenticalFixedRows()
    {
        var rows = new Dictionary<string, string>();
        var store = Substitute.For<IBlobJsonStore<Item>>();
        store.PutAsync(Arg.Any<string>(), Arg.Any<Item>(), Arg.Any<CancellationToken>())
            .Returns(call => { rows[call.Arg<string>()] = JsonSerializer.Serialize(call.Arg<Item>()); return Task.CompletedTask; });
        var seed = new ItemsSeedData(store);
        await seed.EnsureSeededAsync(default);
        var first = rows.ToDictionary();
        await seed.EnsureSeededAsync(default);
        rows.Should().BeEquivalentTo(first);
        rows.Keys.Should().BeEquivalentTo("fixture-middle", "fixture-newest", "fixture-oldest");
        await store.Received(6).PutAsync(Arg.Any<string>(), Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("production", "true", "127.0.0.1", null, "POST")]
    [InlineData("production", null, "127.0.0.1", null, "POST")]
    [InlineData("development", null, "127.0.0.1", null, "POST")]
    [InlineData("development", "false", "127.0.0.1", null, "POST")]
    [InlineData("development", "TRUE", "127.0.0.1", null, "POST")]
    [InlineData("development", "true", "203.0.113.1", "127.0.0.1", "POST")]
    [InlineData("development", "true", "127.0.0.1", "127.0.0.1, 203.0.113.1", "POST")]
    [InlineData("development", "true", "127.0.0.1", "unknown", "POST")]
    [InlineData("development", "true", "127.0.0.1", "", "POST")]
    [InlineData("development", "true", "127.0.0.1", null, "GET")]
    public async Task Reset_GuardFails_ReturnsBlank404WithoutTouchingStores(string environment, string? enabled, string peer, string? forwarded, string method)
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", environment);
        Environment.SetEnvironmentVariable("TEST_RESET_ENABLED", enabled);
        var store = Substitute.For<ISeedStore>();
        var result = await new SeedFunctions(Service(null, store)).Reset(Request(peer, forwarded, method), default);
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(404);
        store.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("127.0.0.1", null)]
    [InlineData("::1", "127.0.0.1, ::1")]
    [InlineData("::ffff:127.0.0.1", "::ffff:127.0.0.1")]
    public async Task Reset_LoopbackWithEmptyProduct_ReturnsVersionZeroAndEmptyStores(string peer, string? forwarded)
    {
        var response = await new SeedFunctions(Service()).Reset(Request(peer, forwarded), default);
        var seed = ((OkObjectResult)response).Value.Should().BeOfType<SeedResult>().Subject;
        seed.Should().BeEquivalentTo(new SeedResult("0", [], [], []));
    }

    [Theory]
    [InlineData("203.0.113.1", "127.0.0.1")]
    [InlineData("127.0.0.1", "203.0.113.1")]
    public async Task Reset_ForwardedMiddlewareChangedRequest_ValidatesOriginalPeerAndAllOriginalHops(string originalPeer, string originalHop)
    {
        var request = Request();
        request.HttpContext.Features.Set(new SeedRequestOrigin(IPAddress.Parse(originalPeer), [originalHop]));
        var result = await new SeedFunctions(Service()).Reset(request, default);
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData("{\"X-Forwarded-For\":\"203.0.113.1\"}", 404)]
    [InlineData("{\"x-forwarded-for\":\"127.0.0.1, 203.0.113.1\"}", 404)]
    [InlineData("{\"X-Forwarded-For\":\"203.0.113.1, 127.0.0.1\"}", 404)]
    [InlineData("{\"X-Forwarded-For\":\"127.0.0.1, ::1\"}", 200)]
    [InlineData("{}", 200)]
    [InlineData("invalid", 404)]
    [InlineData("{\"X-Forwarded-For\":null}", 404)]
    [InlineData(null, 404)]
    public async Task Reset_HostProxyReplacedForwardedHeaders_ValidatesOriginalInvocationHeaders(string? originalHeaders, int expectedStatus)
    {
        var context = Substitute.For<FunctionContext>();
        var binding = Substitute.For<BindingContext>();
        var data = new Dictionary<string, object?>();
        if (originalHeaders is not null) data["Headers"] = originalHeaders;
        binding.BindingData.Returns(data);
        context.BindingContext.Returns(binding);
        var result = await new SeedFunctions(Service()).Reset(Request("127.0.0.1", "127.0.0.1"), default, context);
        ((IStatusCodeActionResult)result).StatusCode.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task Configure_ForwardedHeadersRewritten_SnapshotsOriginalPeerAndChain()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var builder = new Microsoft.AspNetCore.Builder.ApplicationBuilder(services);
        var filter = new SeedRequestOriginFilter();
        filter.Configure(app => app.Run(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Headers.Remove("X-Forwarded-For");
            return Task.CompletedTask;
        }))(builder);
        var request = Request("203.0.113.1", "127.0.0.1, 203.0.113.2");
        await builder.Build()(request.HttpContext);
        var origin = request.HttpContext.Features.Get<SeedRequestOrigin>()!;
        origin.Peer.Should().Be(IPAddress.Parse("203.0.113.1"));
        origin.ForwardedFor.Should().Equal("127.0.0.1", " 203.0.113.2");
    }

    [Fact]
    public async Task Reset_OverlappingResetAndWrite_Returns409AndHoldsWriteUntilResetCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore("table", "widgets") { Count = 5 };
        var seed = new ControlledSeed(async () =>
        {
            entered.SetResult();
            await release.Task;
            await ResetGate.Shared.WriteAsync(() => { store.Count = 1; return Task.CompletedTask; }, default);
        });
        var functions = new SeedFunctions(Service(seed, store));
        var first = functions.Reset(Request(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task? write = null;
        try
        {
            var second = await functions.Reset(Request(), default);
            second.Should().BeOfType<ConflictObjectResult>();
            JsonSerializer.Serialize(((ConflictObjectResult)second).Value).Should().Be("{\"busy\":true}");
            write = ResetGate.Shared.WriteAsync(() => { store.Count++; return Task.CompletedTask; }, default);
            write.IsCompleted.Should().BeFalse();
        }
        finally { release.TrySetResult(); }
        (await first).Should().BeOfType<OkObjectResult>();
        await write!.WaitAsync(TimeSpan.FromSeconds(10));
        store.Count.Should().Be(2);
    }

    [Fact]
    public async Task PutAsync_ResetBlocked_HoldsBlobUploadUntilResetCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var container = Substitute.For<Azure.Storage.Blobs.BlobContainerClient>();
        var blob = Substitute.For<Azure.Storage.Blobs.BlobClient>();
        container.GetBlobClient("items/queued.json").Returns(blob);
        var store = new App.Api.Storage.BlobJsonStore<Item>(container,
            new App.Api.Storage.BlobJsonStoreOptions<Item> { Prefix = "items" },
            NullLogger<App.Api.Storage.BlobJsonStore<Item>>.Instance);
        var reset = ResetGate.Shared.TryResetAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
            return new object();
        }, default);
        await entered.Task;
        var write = store.PutAsync("queued", new Item { Id = "queued", Name = "queued" });
        try
        {
            write.IsCompleted.Should().BeFalse();
            container.ReceivedCalls().Should().BeEmpty();
        }
        finally { release.TrySetResult(); }
        await reset;
        await write.WaitAsync(TimeSpan.FromSeconds(10));
        await blob.Received(1).UploadAsync(Arg.Any<BinaryData>(), true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reset_SeedThrows_ReleasesWriteGateAndResetReservation()
    {
        var calls = 0;
        using var service = Service(new ControlledSeed(() => ++calls == 1
            ? Task.FromException(new InvalidOperationException("seed failed")) : Task.CompletedTask));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResetAsync(default));
        var written = false;
        await ResetGate.Shared.WriteAsync(() => { written = true; return Task.CompletedTask; }, default)
            .WaitAsync(TimeSpan.FromSeconds(10));
        written.Should().BeTrue();
        (await service.ResetAsync(default)).Should().NotBeNull();
    }

    [Fact]
    public async Task Reset_InFlightWrite_WaitsBeforeClearing()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new MemoryStore("blob", "business");
        var write = ResetGate.Shared.WriteAsync(async () => { entered.SetResult(); await release.Task; store.Count = 9; }, default);
        await entered.Task;
        var reset = Service(null, store).ResetAsync(default);
        var lateWrite = ResetGate.Shared.WriteAsync(() => { store.Count++; return Task.CompletedTask; }, default);
        try
        {
            reset.IsCompleted.Should().BeFalse();
            lateWrite.IsCompleted.Should().BeFalse();
        }
        finally { release.SetResult(); }
        await write;
        (await reset)!.Blobs["business"].Should().Be(0);
        await lateWrite.WaitAsync(TimeSpan.FromSeconds(10));
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_CanceledWhileResetRuns_PropagatesCancellationAndAllowsLaterWrites()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reset = ResetGate.Shared.TryResetAsync(async () => { entered.SetResult(); await release.Task; return new object(); }, default);
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        var written = false;
        var write = ResetGate.Shared.WriteAsync(() => { written = true; return Task.CompletedTask; }, cancellation.Token);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
            written.Should().BeFalse();
        }
        finally { release.TrySetResult(); }
        await reset;
        await ResetGate.Shared.WriteAsync(() => { written = true; return Task.CompletedTask; }, default)
            .WaitAsync(TimeSpan.FromSeconds(10));
        written.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_CanceledWhileDrainingWrite_ReleasesReservationForTheNextReset()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = ResetGate.Shared.WriteAsync(async () => { entered.SetResult(); await release.Task; }, default);
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        using var service = Service();
        var reset = service.ResetAsync(cancellation.Token);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reset);
        }
        finally { release.TrySetResult(); }
        await write;
        (await service.ResetAsync(default).WaitAsync(TimeSpan.FromSeconds(10))).Should().NotBeNull();
    }

    [Fact]
    public async Task Reset_RegisteredEmptyStores_ReportsEveryStoreSorted()
    {
        var result = await Service(null, new MemoryStore("table", "zeta"), new MemoryStore("blob", "assets"), new MemoryStore("table", "alpha")).ResetAsync(default);
        result!.Stores.Should().Equal("blob:assets", "table:alpha", "table:zeta");
        result.Tables.Should().BeEquivalentTo(new Dictionary<string, int> { ["alpha"] = 0, ["zeta"] = 0 });
        result.Blobs.Should().BeEquivalentTo(new Dictionary<string, int> { ["assets"] = 0 });
    }

    [Fact]
    public async Task Ready_StartupSeedBlocked_Returns503Then200AfterCompletion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = Service(new ControlledSeed(async () => { entered.SetResult(); await release.Task; }));
        var functions = new SeedFunctions(service);
        ((ObjectResult)functions.Ready(Request())).StatusCode.Should().Be(503);
        await service.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { ((ObjectResult)functions.Ready(Request())).StatusCode.Should().Be(503); }
        finally { release.TrySetResult(); }
        await service.ExecuteTask!;
        var ready = functions.Ready(Request()).Should().BeOfType<OkObjectResult>().Subject;
        JsonSerializer.Serialize(ready.Value).Should().Be("{\"ready\":true,\"seed_version\":\"test\"}");
        await service.StopAsync(default);
    }

    [Fact]
    public async Task Ready_StartupSeedThrows_Remains503AndLogsFailure()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<SeedService>>();
        using var service = new SeedService(new ControlledSeed(() => throw new InvalidOperationException("seed failed")), [], logger);
        await service.StartAsync(default);
        await service.ExecuteTask!;
        ((ObjectResult)new SeedFunctions(service).Ready(Request())).StatusCode.Should().Be(503);
        logger.ReceivedCalls().Should().Contain(call => call.GetMethodInfo().Name == "Log");
    }

    [Theory]
    [InlineData("production", typeof(EmptySeedData))]
    [InlineData("development", typeof(ItemsSeedData))]
    public void Register_RuntimeMode_RegistersSampleOnlyOutsideProduction(string environment, Type expected)
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", environment);
        var services = new ServiceCollection();
        ItemsSeedData.Register(services);
        services.AddSeedContract();
        services.Single(descriptor => descriptor.ServiceType == typeof(ISeedData)).ImplementationType.Should().Be(expected);
    }

    [Fact]
    public async Task EnsureSeededAsync_NoProductRegistration_BuildsAndReadiesWithoutItemsOrStorage()
    {
        using var provider = new ServiceCollection().AddLogging().AddSeedContract().BuildServiceProvider();
        using var service = provider.GetRequiredService<SeedService>();
        await service.StartAsync(default);
        await service.ExecuteTask!;
        service.Ready.Should().BeTrue();
        (await service.ResetAsync(default))!.Should().BeEquivalentTo(new SeedResult("0", [], [], []));
    }

    [Fact]
    public async Task Ready_SeedDependencyConstructionThrows_LogsFailureAndKeepsHostAvailableWith503()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<SeedService>>();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(logger);
        services.AddSingleton<ISeedData>(_ => throw new InvalidOperationException("seed dependency failed"));
        services.AddSeedContract();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<SeedService>();
        await service.StartAsync(default);
        await service.ExecuteTask!;
        ((ObjectResult)new SeedFunctions(service).Ready(Request())).StatusCode.Should().Be(503);
        logger.ReceivedCalls().Should().Contain(call => call.GetMethodInfo().Name == "Log"
            && Equals(call.GetArguments()[0], Microsoft.Extensions.Logging.LogLevel.Error));
    }

    [Fact]
    public async Task Reset_ProductionWithUnavailableStoreConfiguration_Returns404WithoutResolvingStores()
    {
        Environment.SetEnvironmentVariable("APP_ENVIRONMENT", "production");
        var resolved = false;
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<ISeedStore>(_ =>
        {
            resolved = true;
            throw new App.Api.Storage.MissingPreviewStorageException();
        });
        services.AddSeedContract();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<SeedService>();
        await service.StartAsync(default);
        await service.ExecuteTask!;
        service.Ready.Should().BeTrue();
        service.Version.Should().Be("0");
        var result = await new SeedFunctions(service).Reset(Request(), default);
        result.Should().BeOfType<StatusCodeResult>().Which.StatusCode.Should().Be(404);
        resolved.Should().BeFalse();
    }

    private sealed class ControlledSeed(Func<Task> run) : ISeedData
    {
        public string Version => "test";
        public Task EnsureSeededAsync(CancellationToken cancellationToken) => run();
    }

    private sealed class MemoryStore(string kind, string name) : ISeedStore
    {
        public string Kind => kind;
        public string Name => name;
        public int Count { get; set; }
        public Task ClearAsync(CancellationToken cancellationToken) { Count = 0; return Task.CompletedTask; }
        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Count);
    }
}
