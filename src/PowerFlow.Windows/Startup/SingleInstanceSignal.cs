using System.Diagnostics;

namespace PowerFlow.Windows.Startup;

public sealed class SingleInstanceSignal : IDisposable
{
    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _registration;
    private int _disposed;

    private SingleInstanceSignal(EventWaitHandle @event, Action callback)
    {
        _event = @event;
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _event,
            static (state, timedOut) =>
            {
                if (!timedOut) ((Action)state!).Invoke();
            },
            callback,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static SingleInstanceSignal Listen(string name, Action callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        var @event = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        return new SingleInstanceSignal(@event, callback);
    }

    public static bool TrySignal(string name, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var wait = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        var timer = Stopwatch.StartNew();
        do
        {
            try
            {
                using var @event = EventWaitHandle.OpenExisting(name);
                return @event.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                if (timer.Elapsed >= wait) return false;
                Thread.Sleep(TimeSpan.FromMilliseconds(25));
            }
        } while (timer.Elapsed < wait);

        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registration.Unregister(null);
        _event.Dispose();
    }
}
