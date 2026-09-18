using App.Core.Models;
using AwesomeAssertions;

namespace App.Tests.Models;

public sealed class ItemTests
{
    [Fact]
    public void Constructor_Defaults_IdAndNameAreEmptyStringsNotNull()
    {
        // Arrange & Act
        var item = new Item();

        // Assert
        item.Id.Should().Be(string.Empty);
        item.Name.Should().Be(string.Empty);
        item.Description.Should().BeNull();
    }
}
