namespace ApiUsageAnalyzer.Utils;

public sealed class AsyncAutoResetEvent
{
    private readonly Lock @lock = new();
    private Task? currentWait;
    private TaskCompletionSource? taskCompletionSource;

    public void Set()
    {
        lock (@lock)
        {
            if (currentWait is null)
            {
                currentWait = Task.CompletedTask;
            }
            else if (!currentWait.IsCompleted)
            {
                currentWait = null;
                taskCompletionSource!.TrySetResult();
            }
        }
    }

    public Task WaitAsync()
    {
        lock (@lock)
        {          
            if (currentWait is null)
            {
                taskCompletionSource = new();
                currentWait = taskCompletionSource.Task;
            }
            else if (currentWait.IsCompleted)
            {
                var originalTask = currentWait;
                currentWait = null;
                return originalTask;
            }

            return currentWait;
        }
    }
}
