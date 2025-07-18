using ApiUsageAnalyzer.Utils;
using Shouldly;

namespace ApiUsageAnalyzer.Tests;

public class DebouncerTests
{
    [Test]
    public async Task Debouncer_invokes_action_after_delay()
    {
        var timeProvider = new TestTimeProvider();

        var actionCallCount = 0;
        await using var debouncer = new Debouncer(TimeSpan.FromSeconds(1), () => actionCallCount++, timeProvider);
        var timer = timeProvider.Timers.ShouldHaveSingleItem();

        debouncer.Signal();
        timer.DueTime.ShouldBe(TimeSpan.FromSeconds(1));
        timer.Period.ShouldBe(Timeout.InfiniteTimeSpan);

        actionCallCount.ShouldBe(0);
        timer.Fire();
        actionCallCount.ShouldBe(1);
    }

    [Test]
    public async Task Signaling_multiple_times_during_delay_does_not_invoke_action_multiple_times()
    {
        var timeProvider = new TestTimeProvider();

        var actionCallCount = 0;
        await using var debouncer = new Debouncer(TimeSpan.FromSeconds(1), () => actionCallCount++, timeProvider);
        var timer = timeProvider.Timers.ShouldHaveSingleItem();

        debouncer.Signal();
        timer.DueTime.ShouldBe(TimeSpan.FromSeconds(1));
        timer.Period.ShouldBe(Timeout.InfiniteTimeSpan);

        debouncer.Signal();
        timer.DueTime.ShouldBe(TimeSpan.FromSeconds(1));
        timer.Period.ShouldBe(Timeout.InfiniteTimeSpan);

        debouncer.Signal();
        timer.DueTime.ShouldBe(TimeSpan.FromSeconds(1));
        timer.Period.ShouldBe(Timeout.InfiniteTimeSpan);

        actionCallCount.ShouldBe(0);
        timer.Fire();
        actionCallCount.ShouldBe(1);
        timer.DueTime.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    [Test]
    public async Task Signals_are_ignored_while_action_is_still_executing()
    {
        var timeProvider = new TestTimeProvider();

        using var actionBlocker = new ManualResetEventSlim(false);

        var actionCallCount = 0;
        var actionStarted = new TaskCompletionSource();
        await using var debouncer = new Debouncer(TimeSpan.FromSeconds(1), () =>
        {
            actionCallCount++;
            actionStarted.SetResult();
            actionBlocker.Wait();
        }, timeProvider);
        var timer = timeProvider.Timers.ShouldHaveSingleItem();

        debouncer.Signal();
        timer.DueTime.ShouldBe(TimeSpan.FromSeconds(1));

        var fireTask = Task.Run(timer.Fire);
        await actionStarted.Task;
        timer.DueTime.ShouldBe(Timeout.InfiniteTimeSpan);

        debouncer.Signal();
        timer.DueTime.ShouldBe(Timeout.InfiniteTimeSpan);

        actionBlocker.Set();
        await fireTask;

        actionCallCount.ShouldBe(1);
    }
}
