using App.Api.PocAuth;
using Xunit;

namespace App.Tests.PocAuth;

public sealed class PocAuthSeedGeneratedTests
{
    private static readonly
        (string Username, string DisplayName, string Roles)[] ExpectedUsers =
    [
        ("pat@example.com", "Requester", "user"),
        ("admin", "Admin", "admin"),
    ];

    [Fact]
    public async Task GeneratedUsersAndRolesAreSeededByTheDefaultService()
    {
        Assert.Equal(ExpectedUsers.Length, PocAuthSeed.Users.Count);
        for (var index = 0; index < ExpectedUsers.Length; index++)
        {
            var expected = ExpectedUsers[index];
            var actual = PocAuthSeed.Users[index];
            Assert.Equal(expected.Username, actual.Username);
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.Roles, string.Join(',', actual.Roles));
            Assert.False(string.IsNullOrWhiteSpace(actual.Salt));
            Assert.False(string.IsNullOrWhiteSpace(actual.PasswordHash));
            Assert.True(actual.Iterations > 0);
        }

        var store = new InMemoryPocAuthStore();
        var service = new PocAuthService(store, TimeProvider.System);
        await service.SeedAsync();
        Assert.Equal(ExpectedUsers.Length, store.Users.Count);
        foreach (var seed in PocAuthSeed.Users)
        {
            var stored = Assert.IsType<PocUserEntity>(
                store.Users.GetValueOrDefault(PocUserEntity.KeyFor(seed.Username)));
            Assert.Equal(seed.Username.Trim(), stored.Username);
            Assert.Equal(seed.DisplayName, stored.DisplayName);
            Assert.Equal(string.Join(',', seed.Roles), stored.Roles);
            Assert.Equal(seed.Salt, stored.Salt);
            Assert.Equal(seed.PasswordHash, stored.PasswordHash);
            Assert.Equal(seed.Iterations, stored.Iterations);
        }
    }
}
