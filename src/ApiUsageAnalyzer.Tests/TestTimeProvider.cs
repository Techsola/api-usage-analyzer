namespace ApiUsageAnalyzer.Tests;

public class TestTimeProvider : TimeProvider
{
    private readonly List<TestTimer> timers = [];
    public IReadOnlyList<TestTimer> Timers => timers.AsReadOnly();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(callback, state, dueTime, period);
        timers.Add(timer);
        return timer;
    }

    public sealed class TestTimer : ITimer
    {
        private readonly TimerCallback callback;
        private readonly object? state;

        public TestTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            this.state = state;
        }

        public TimeSpan DueTime { get; private set; }
        public TimeSpan Period { get; private set; }

        public void Fire()
        {
            if (DueTime == Timeout.InfiniteTimeSpan)
                throw new InvalidOperationException("The timer is not due.");

            DueTime = Period;
            callback(state);
        }

        bool ITimer.Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            Period = period;
            return true;
        }

        void IDisposable.Dispose()
        {
        }

        ValueTask IAsyncDisposable.DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
