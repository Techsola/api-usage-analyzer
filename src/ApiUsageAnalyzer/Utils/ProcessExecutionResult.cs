using System.Collections.Immutable;
using System.Text;

namespace ApiUsageAnalyzer.Utils;

internal sealed record ProcessExecutionResult
{
    public required string FileName { get; init; }
    public required string WorkingDirectory { get; init; }
    public required ImmutableArray<string> Arguments { get; init; }
    public required int ExitCode { get; init; }
    public required string? StdOut { get; init; }
    public required string? StdErr { get; init; }

    public void ThrowIfError(bool throwIfStdErr = true)
    {
        if (ExitCode != 0 || throwIfStdErr && StdErr is not null)
            throw new ProcessExecutionException(ToString());
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append($"{Path.GetFileName(FileName)} ended with exit code {ExitCode}.");

        if (!string.IsNullOrWhiteSpace(StdErr))
            builder.AppendLine(" Stderr:").Append(StdErr);

        if (!string.IsNullOrWhiteSpace(StdOut))
        {
            if (string.IsNullOrWhiteSpace(StdErr))
                builder.Append(' ');
            else
                builder.AppendLine().AppendLine();

            builder.AppendLine("Stdout:").Append(StdOut);
        }

        return builder.ToString();
    }
}
