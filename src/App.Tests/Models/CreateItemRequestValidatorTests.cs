using App.Core.Models;
using AwesomeAssertions;

namespace App.Tests.Models;

public sealed class CreateItemRequestValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingName_ReturnsRequired(string? name)
    {
        CreateItemRequestValidator.Validate(new CreateItemRequest { Name = name }).Should().Be("Name is required.");
    }

    [Fact]
    public void Validate_NullRequest_ReturnsRequired()
    {
        CreateItemRequestValidator.Validate(null).Should().Be("Name is required.");
    }

    [Fact]
    public void Validate_NameOverLimitAfterTrim_ReturnsError()
    {
        var request = new CreateItemRequest { Name = new string('a', CreateItemRequestValidator.MaxNameLength + 1) };

        CreateItemRequestValidator.Validate(request).Should().Be("Name must be 100 characters or fewer.");
    }

    [Fact]
    public void Validate_NameAtLimitWithPadding_IsValid()
    {
        var request = new CreateItemRequest { Name = " " + new string('a', CreateItemRequestValidator.MaxNameLength) + " " };

        CreateItemRequestValidator.Validate(request).Should().BeNull();
    }

    [Fact]
    public void Validate_DescriptionOverLimit_ReturnsError()
    {
        var request = new CreateItemRequest { Name = "ok", Description = new string('d', CreateItemRequestValidator.MaxDescriptionLength + 1) };

        CreateItemRequestValidator.Validate(request).Should().Be("Description must be 1000 characters or fewer.");
    }
}
