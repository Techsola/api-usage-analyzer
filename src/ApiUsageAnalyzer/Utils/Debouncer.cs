namespace ApiUsageAnalyzer.Utils;

public sealed class Debouncer : IAsyncDisposable
{
    private readonly TimeSpan delay;
    private readonly Action action;
    private readonly ITimer timer;
    private bool isWaitingOrActing;

    public Debouncer(TimeSpan delay, Action action, TimeProvider? timeProvider = null)
    {
        this.delay = delay;
        this.action = action;

        timeProvider ??= TimeProvider.System;
        timer = timeProvider.CreateTimer(OnTimerCallback, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void OnTimerCallback(object? state)
    {
        try
        {
            action();
        }
        finally
        {
            Volatile.Write(ref isWaitingOrActing, false);
        }
    }

    /// <summary>
    /// Starts a timer that will invoke the action after the specified delay. If the timer is already running, or if the
    /// action is still executing, this call will have no effect.
    /// </summary>
    public void Signal()
    {
        if (!Interlocked.Exchange(ref isWaitingOrActing, true))
            timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public async ValueTask DisposeAsync()
    {
        await timer.DisposeAsync();
    }
}
