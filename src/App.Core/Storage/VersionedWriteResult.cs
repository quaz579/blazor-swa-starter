namespace App.Core.Storage;

public abstract record VersionedWriteResult
{
    private VersionedWriteResult()
    {
    }

    public sealed record Success(string ETag) : VersionedWriteResult;

    public sealed record Conflict : VersionedWriteResult
    {
        public static Conflict Instance { get; } = new();

        private Conflict()
        {
        }
    }
}
