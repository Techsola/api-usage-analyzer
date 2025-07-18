using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace ApiUsageAnalyzer.Utils;

internal static class ProcessUtils
{
    public static async Task<ProcessExecutionResult> RunAsync(string fileName, string workingDirectory, ImmutableArray<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        process.StandardInput.Close();

        var stdOut = (StringBuilder?)null;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;

            if (stdOut is null)
                stdOut = new StringBuilder();
            else
                stdOut.AppendLine();

            stdOut.Append(e.Data);
        };

        var stdErr = (StringBuilder?)null;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;

            if (stdErr is null)
                stdErr = new StringBuilder();
            else
                stdErr.AppendLine();

            stdErr.Append(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return new ProcessExecutionResult
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            ExitCode = process.ExitCode,
            StdOut = stdOut?.ToString(),
            StdErr = stdErr?.ToString(),
        };
    }
}
