using App.Api.PocAuth;
using AwesomeAssertions;

namespace App.Tests.PocAuth;

public sealed class PocAuthModelsTests
{
    [Theory]
    [InlineData(" Requester@Example.COM ", "requester@example.com")]
    [InlineData("admin", "admin")]
    public void KeyFor_PaddedOrMixedCaseUsername_ReturnsNormalizedStorageKey(string username, string expected)
    {
        PocUserEntity.KeyFor(username).Should().Be(expected);
    }

    [Theory]
    [InlineData(" admin, ,user,, ", new[] { "admin", "user" })]
    [InlineData("", new string[0])]
    public void GetRoles_CommaSeparatedRoles_TrimsEntriesAndDropsEmptyRoles(string roles, string[] expected)
    {
        var user = new PocUserEntity { Roles = roles };

        user.GetRoles().Should().Equal(expected);
    }
}
