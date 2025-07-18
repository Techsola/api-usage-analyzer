namespace ApiUsageAnalyzer.Utils;

internal static class ConsoleUtils
{
    public static IDisposable HandleCtrlC(out CancellationToken cancellationToken)
    {
        var subscription = new CancelKeyPressSubscription();
        cancellationToken = subscription.CancellationToken;
        return subscription;
    }

    private sealed class CancelKeyPressSubscription : IDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();

        public CancelKeyPressSubscription()
        {
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public CancellationToken CancellationToken => cancellationTokenSource.Token;

        public void Dispose()
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            if (!cancellationTokenSource.IsCancellationRequested)
            {
                Console.WriteLine("Canceling...");
                cancellationTokenSource.Cancel();
            }
        }
    }
}
