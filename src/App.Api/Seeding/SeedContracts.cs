namespace App.Api.Seeding;

public interface ISeedData
{
    string Version { get; }
    Task EnsureSeededAsync(CancellationToken cancellationToken);
}

public interface ISeedStore
{
    string Kind { get; }
    string Name { get; }
    Task ClearAsync(CancellationToken cancellationToken);
    Task<int> CountAsync(CancellationToken cancellationToken);
}

public sealed class EmptySeedData : ISeedData
{
    public string Version => "0";
    public Task EnsureSeededAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
