using ApiUsageAnalyzer.Utils;
using Shouldly;

namespace ApiUsageAnalyzer.Tests;

public class AsyncAutoResetEventTests
{
    [Test]
    public void The_event_is_initially_not_signaled()
    {
        var ev = new AsyncAutoResetEvent();
        ev.WaitAsync().IsCompleted.ShouldBeFalse();
    }

    [Test]
    public void The_event_can_be_signaled_before_waiting_starts()
    {
        var ev = new AsyncAutoResetEvent();
        ev.Set();
        ev.WaitAsync().IsCompleted.ShouldBeTrue();
    }

    [Test]
    public void The_event_can_be_signaled_after_waiting_starts()
    {
        var ev = new AsyncAutoResetEvent();
        var waitTask = ev.WaitAsync();
        ev.Set();
        waitTask.IsCompleted.ShouldBeTrue();
    }

    [Test]
    public void The_event_automatically_resets_when_signaled_after_the_first_wait_starts()
    {
        var ev = new AsyncAutoResetEvent();
        ev.WaitAsync();
        ev.Set();
        ev.WaitAsync().IsCompleted.ShouldBeFalse();
    }

    [Test]
    public void The_event_automatically_resets_when_signaled_before_the_first_wait_starts()
    {
        var ev = new AsyncAutoResetEvent();
        ev.Set();
        ev.WaitAsync();
        ev.WaitAsync().IsCompleted.ShouldBeFalse();
    }

    [Test]
    public void Setting_multiple_times_does_not_signal_more_than_one_wait()
    {
        var ev = new AsyncAutoResetEvent();
        ev.Set();
        ev.Set();
        ev.WaitAsync();
        ev.WaitAsync().IsCompleted.ShouldBeFalse();
    }

    [Test]
    public void Multiple_waits_for_the_same_signal_are_the_same_task()
    {
        var ev = new AsyncAutoResetEvent();
        ev.WaitAsync().ShouldBeSameAs(ev.WaitAsync());
        ev.Set();
        ev.WaitAsync().ShouldBeSameAs(ev.WaitAsync());
    }
}
