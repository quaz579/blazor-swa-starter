namespace App.Core.Models;

/// <summary>A single stored item. Persisted as one JSON blob per item.</summary>
public sealed class Item
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
