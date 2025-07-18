using System.Collections.Immutable;

namespace ApiUsageAnalyzer.Utils;

public sealed class DelayedProcessor<TWorkItem>
{
    private readonly Func<ImmutableArray<TWorkItem>, Task> processAsync;
    private readonly AsyncAutoResetEvent wakeUp = new();
    private readonly Debouncer debouncer;
    private readonly Task runTask;

    private readonly ImmutableArray<TWorkItem>.Builder queue = ImmutableArray.CreateBuilder<TWorkItem>();
    private bool isCompleted;

    public DelayedProcessor(Action<ImmutableArray<TWorkItem>> process, TimeSpan debounceInterval, TimeProvider? timeProvider = null)
        : this(items => { process(items); return Task.CompletedTask; }, debounceInterval, timeProvider)
    {
    }

    public DelayedProcessor(Func<ImmutableArray<TWorkItem>, Task> processAsync, TimeSpan debounceInterval, TimeProvider? timeProvider = null)
    {
        this.processAsync = processAsync;
        debouncer = new Debouncer(debounceInterval, action: wakeUp.Set, timeProvider);
        runTask = RunAsync();
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await wakeUp.WaitAsync();

            ImmutableArray<TWorkItem> newItems;
            bool isLastProcess;

            lock (queue)
            {
                newItems = queue.DrainToImmutable();
                isLastProcess = isCompleted;
            }

            if (newItems is not [])
                await processAsync(newItems);

            if (isLastProcess) break;
        }
    }

    public void Enqueue(params ReadOnlySpan<TWorkItem> items)
    {
        lock (queue)
        {
            if (isCompleted)
                throw new InvalidOperationException($"{nameof(Enqueue)} must not be called after {nameof(CompleteAsync)} has been called.");

            queue.AddRange(items);
        }

        debouncer.Signal();
    }

    public async Task CompleteAsync()
    {
        lock (queue)
        {
            if (isCompleted)
                throw new InvalidOperationException($"{nameof(CompleteAsync)} must not be called more than once.");

            isCompleted = true;
        }

        wakeUp.Set();
        await runTask;

        await debouncer.DisposeAsync();
    }
}
