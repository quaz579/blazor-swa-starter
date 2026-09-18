using App.Core.Models;
using AwesomeAssertions;

namespace App.Tests.Models;

public sealed class CreateItemRequestTests
{
    [Fact]
    public void Constructor_Defaults_NameAndDescriptionAreNull()
    {
        // Arrange & Act
        var request = new CreateItemRequest();

        // Assert
        request.Name.Should().BeNull();
        request.Description.Should().BeNull();
    }
}
