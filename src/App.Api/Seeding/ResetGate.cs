namespace App.Api.Seeding;

public sealed class ResetGate
{
    public static ResetGate Shared { get; } = new();
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly AsyncLocal<bool> _writeOwner = new();
    private readonly object _state = new();
    private TaskCompletionSource? _resetCompleted;

    public async Task<T> WriteAsync<T>(Func<Task<T>> write, CancellationToken cancellationToken)
    {
        if (_writeOwner.Value)
            return await write();
        while (true)
        {
            Task wait;
            bool acquired;
            lock (_state)
            {
                acquired = _resetCompleted is null;
                wait = acquired ? _writes.WaitAsync(cancellationToken) : _resetCompleted!.Task;
            }
            if (acquired)
            {
                await wait;
                break;
            }
            await wait.WaitAsync(cancellationToken);
        }
        try
        {
            _writeOwner.Value = true;
            return await write();
        }
        finally
        {
            _writeOwner.Value = false;
            _writes.Release();
        }
    }

    public Task WriteAsync(Func<Task> write, CancellationToken cancellationToken) =>
        WriteAsync(async () => { await write(); return true; }, cancellationToken);

    public async Task<T?> TryResetAsync<T>(Func<Task<T>> reset, CancellationToken cancellationToken) where T : class
    {
        Task acquire;
        TaskCompletionSource completed;
        lock (_state)
        {
            if (_resetCompleted is not null) return null;
            completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _resetCompleted = completed;
            acquire = _writes.WaitAsync(cancellationToken);
        }
        try
        {
            await acquire;
            try
            {
                _writeOwner.Value = true;
                return await reset();
            }
            finally
            {
                _writeOwner.Value = false;
                _writes.Release();
            }
        }
        finally
        {
            lock (_state)
            {
                _resetCompleted = null;
                completed.SetResult();
            }
        }
    }
}
