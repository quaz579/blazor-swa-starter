namespace App.Core.Models;

/// <summary>Request body for creating an <see cref="Item"/>.</summary>
public sealed class CreateItemRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}
