using App.Api.PocAuth;
using App.Api.Storage;
using AwesomeAssertions;

namespace App.Tests.Storage;

public sealed class StorageClientsTests
{
    private const string Local = "UseDevelopmentStorage=true";

    [Fact]
    public void CreateTableClient_DevelopmentStorage_TargetsAzuriteTablePort()
    {
        var client = StorageClients.CreateTableClient(Local, "things");

        client.Name.Should().Be("things");
        client.Uri.Port.Should().Be(10002);
    }

    [Fact]
    public void CreateQueueClient_DevelopmentStorage_TargetsAzuriteQueuePort()
    {
        var client = StorageClients.CreateQueueClient(Local, "work");

        client.Name.Should().Be("work");
        client.Uri.Port.Should().Be(10001);
    }

    [AzuriteFact]
    public async Task TablePocAuthStore_RoundTripsUsersAndSessions_AgainstAzurite()
    {
        var store = new TablePocAuthStore(Local);
        var username = $"azurite-{Guid.NewGuid():N}";
        var (salt, hash, iterations) = PocPasswordHasher.Hash("pw", iterations: 1_000);
        var user = new PocUserEntity { RowKey = PocUserEntity.KeyFor(username), Username = username, Roles = "user", Salt = salt, PasswordHash = hash, Iterations = iterations };

        (await store.AddUserIfMissingAsync(user)).Should().BeTrue();
        (await store.AddUserIfMissingAsync(user)).Should().BeFalse();
        (await store.GetUserAsync(username.ToUpperInvariant()))!.Iterations.Should().Be(1_000);

        var tokenHash = Guid.NewGuid().ToString("N");
        await store.PutSessionAsync(new PocSessionEntity { RowKey = tokenHash, Username = username, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) });
        (await store.GetSessionAsync(tokenHash))!.Username.Should().Be(username);
        await store.DeleteSessionAsync(tokenHash);
        (await store.GetSessionAsync(tokenHash)).Should().BeNull();
    }
}
