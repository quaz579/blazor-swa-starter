using System.Threading.Channels;

namespace App.Tests.Web;

internal sealed class ProbeTimeProvider : TimeProvider
{
    private readonly Channel<ProbeTimer> _timers = Channel.CreateUnbounded<ProbeTimer>();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ProbeTimer(callback, state, dueTime);
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            _timers.Writer.TryWrite(timer);
        }
        return timer;
    }

    public async Task<TimeSpan> FireNextAsync()
    {
        while (true)
        {
            var timer = await _timers.Reader.ReadAsync();
            if (timer.Fire())
            {
                return timer.DueTime;
            }
        }
    }

    private sealed class ProbeTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        private bool _disposed;
        public TimeSpan DueTime { get; } = dueTime;
        public bool Fire()
        {
            if (_disposed)
            {
                return false;
            }
            callback(state);
            return true;
        }
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
