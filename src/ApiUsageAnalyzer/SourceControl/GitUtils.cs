using ApiUsageAnalyzer.Utils;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using Windows.Win32.Foundation;

namespace ApiUsageAnalyzer.SourceControl;

internal static class GitUtils
{
    private static async Task<ProcessExecutionResult> ExecuteGitAsync(string workingDirectory, ImmutableArray<string> arguments)
    {
        try
        {
            return await ProcessUtils.RunAsync("git", workingDirectory, arguments);
        }
        catch (Win32Exception ex) when ((WIN32_ERROR)ex.NativeErrorCode == WIN32_ERROR.ERROR_DIRECTORY)
        {
            throw new DirectoryNotFoundException(null, ex);
        }
    }

    public static async Task<string> GetRepoTopLevelDirectoryAsync(string containedDirectory)
    {
        var result = await ExecuteGitAsync(containedDirectory, ["rev-parse", "--show-toplevel"]);
        result.ThrowIfError();

        return result.StdOut!.Replace('/', '\\');
    }

    public static async Task<GitBranchStatus> GetBranchStatusAsync(string repositoryPath)
    {
        var result = await ExecuteGitAsync(repositoryPath, ["status", "--porcelain=2", "--branch"]);
        result.ThrowIfError();

        var oid = "";
        var head = "";
        var upstream = (string?)null;
        var ahead = 0;
        var behind = 0;

        foreach (var line in result.StdOut.EnumerateLines())
        {
            if (!line.StartsWith('#'))
                continue;

            var headerContents = line["#".Length..].TrimStart();

            var fieldSeparator = headerContents.IndexOf(' ');
            if (fieldSeparator == -1)
                continue;

            var value = headerContents[(fieldSeparator + " ".Length)..];

            switch (headerContents[..fieldSeparator])
            {
                case "branch.oid":
                    oid = value.ToString();
                    break;
                case "branch.head":
                    head = value.ToString();
                    break;
                case "branch.upstream":
                    upstream = value.ToString();
                    break;
                case "branch.ab":
                    if (value.StartsWith('+') && value.IndexOf(" -") is (not -1) and { } valueSeparator)
                    {
                        _ = int.TryParse(value["+".Length..valueSeparator], NumberStyles.None, CultureInfo.InvariantCulture, out ahead);
                        _ = int.TryParse(value[(valueSeparator + " -".Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out behind);
                    }
                    break;
            }
        }

        return new GitBranchStatus(oid, head, upstream is null ? null : (upstream, ahead, behind));
    }

    public static async Task ShallowCloneAsync(string url, string localPath)
    {
        var result = await ExecuteGitAsync(
            workingDirectory: Path.GetDirectoryName(localPath)!,
            ["clone", "--depth", "1", url, Path.GetFileName(localPath)]);

        result.ThrowIfError(throwIfStdErr: false);
    }
}
