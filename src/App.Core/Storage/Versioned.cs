namespace App.Core.Storage;

public sealed record Versioned<T>(T Value, string ETag) where T : class;
