using System.IO.Enumeration;

namespace ApiUsageAnalyzer.SourceControl;

public sealed class LocalGitRepository : ICodeRepository
{
    private readonly string repoDirectory;

    private LocalGitRepository(string repoDirectory)
    {
        this.repoDirectory = repoDirectory;
    }

    public static async Task<LocalGitRepository> OpenAsync(string repoDirectory)
    {
        if (!Path.IsPathFullyQualified(repoDirectory))
            throw new ArgumentException("The repository directory must be an absolute path.", nameof(repoDirectory));

        if (await GitUtils.GetRepoTopLevelDirectoryAsync(repoDirectory) != repoDirectory)
            throw new ArgumentException($"The specified path '{repoDirectory}' is a subfolder of a Git repository. Specify the Git repository directly.", nameof(repoDirectory));

        return new LocalGitRepository(repoDirectory);
    }

    public string Name => Path.GetFileName(repoDirectory)!;

    public string BrowserUrl => repoDirectory;

    public async Task CheckoutAsync(string localPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await GitUtils.ShallowCloneAsync(repoDirectory, localPath);
    }

    public Task<List<string>> GetAllFilePathsAsync(CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<List<string>>(cancellationToken)
            : Task.FromResult(new FileSystemEnumerable<string>(repoDirectory, (ref entry) => entry.ToFullPath(), new EnumerationOptions { RecurseSubdirectories = true })
            {
                ShouldIncludePredicate = (ref entry) => !entry.IsDirectory,
                ShouldRecursePredicate = (ref entry) => !entry.FileName.Equals(".git", StringComparison.OrdinalIgnoreCase),
            }.ToList());
    }

    public Task<Stream> GetItemContentAsync(string path, CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<Stream>(cancellationToken)
            : Task.FromResult<Stream>(File.OpenRead(path));
    }

    public string GetSourceCodeBrowserUrl(string path, (int Line, int LineEnd, int LineStartColumn, int LineEndColumn)? selection = null)
    {
        return Path.Join(repoDirectory, path);
    }
}
