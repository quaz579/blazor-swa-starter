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

    [AzuriteFact]
    public async Task TablePocAuthStore_UpdatesPasswordsAndRevokesOtherSessionsForOneUser_AgainstAzurite()
    {
        var store = new TablePocAuthStore(Local);
        var username = $"azurite-{Guid.NewGuid():N}";
        var other = $"azurite-{Guid.NewGuid():N}";
        var (salt, hash, iterations) = PocPasswordHasher.Hash("pw", iterations: 1_000);
        await store.AddUserIfMissingAsync(new PocUserEntity { RowKey = PocUserEntity.KeyFor(username), Username = username, Roles = "user", Salt = salt, PasswordHash = hash, Iterations = iterations });

        var read = (await store.GetUserAsync(username.ToUpperInvariant()))!;
        (await store.UpdatePasswordAsync(read, salt: "new-salt", passwordHash: "new-hash", 2_000)).Should().Be(PasswordUpdateResult.Updated);
        (await store.UpdatePasswordAsync(read, salt: "stale-salt", passwordHash: "stale-hash", 1)).Should().Be(PasswordUpdateResult.Conflict);
        var ghost = new PocUserEntity { RowKey = $"missing-{Guid.NewGuid():N}", ETag = read.ETag };
        (await store.UpdatePasswordAsync(ghost, salt: "s", passwordHash: "h", iterations: 1)).Should().Be(PasswordUpdateResult.NotFound);
        var updated = (await store.GetUserAsync(username))!;
        (updated.PasswordHash, updated.Salt, updated.Iterations).Should().Be(("new-hash", "new-salt", 2_000));

        var keep = Guid.NewGuid().ToString("N");
        var drop = Guid.NewGuid().ToString("N");
        var foreign = Guid.NewGuid().ToString("N");
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        await store.PutSessionAsync(new PocSessionEntity { RowKey = keep, Username = username, ExpiresUtc = expires });
        await store.PutSessionAsync(new PocSessionEntity { RowKey = drop, Username = username, ExpiresUtc = expires });
        await store.PutSessionAsync(new PocSessionEntity { RowKey = foreign, Username = other, ExpiresUtc = expires });

        await store.DeleteSessionsForUserAsync(username, keep);

        (await store.GetSessionAsync(keep)).Should().NotBeNull();
        (await store.GetSessionAsync(drop)).Should().BeNull();
        (await store.GetSessionAsync(foreign)).Should().NotBeNull();
        await store.DeleteSessionAsync(keep);
        await store.DeleteSessionAsync(foreign);
    }

    [AzuriteFact]
    public async Task ChangePassword_EightConcurrentRequestsWithTheSameCurrentPassword_YieldsExactlyOneSuccessAndNoServerErrors()
    {
        var store = new TablePocAuthStore(Local);
        var username = $"azurite-{Guid.NewGuid():N}";
        var (salt, hash, iterations) = PocPasswordHasher.Hash("Original-pass-1", iterations: 1_000);
        var service = new PocAuthService(store, TimeProvider.System, [new PocSeedUser(username, username, ["user"], salt, hash, iterations)]);
        await service.SeedAsync();
        var principal = new PocPrincipal(username, ["user"]);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(() => service.ChangePasswordAsync(principal, null, "Original-pass-1", $"Concurrent-pass-{i}"))));

        outcomes.Count(o => o.Result == ChangePasswordResult.Changed).Should().Be(1);
        outcomes.Where(o => o.Result != ChangePasswordResult.Changed).Should().OnlyContain(o =>
            (o.Result == ChangePasswordResult.Conflict || o.Result == ChangePasswordResult.WrongCurrentPassword) && !string.IsNullOrWhiteSpace(o.Message));
        var winner = outcomes.Select((o, i) => (o, i)).Single(x => x.o.Result == ChangePasswordResult.Changed).i;
        (await service.AuthenticateAsync(username, $"Concurrent-pass-{winner}")).Should().NotBeNull();
    }
}
