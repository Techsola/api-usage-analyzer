using ApiUsageAnalyzer.SourceControl;
using ApiUsageAnalyzer.Utils;

namespace ApiUsageAnalyzer;

public sealed class LocalWorkspace : IDisposable
{
    private readonly string workspaceFolder;
    private readonly Dictionary<ICodeRepository, Task<string>> repoInitialization = new();

    public LocalWorkspace()
    {
        workspaceFolder = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(workspaceFolder);
    }

    public string GetBrowserUrl(string localPath, (int Line, int LineEnd, int LineStartColumn, int LineEndColumn)? selection = null)
    {
        if (!Path.IsPathRooted(localPath))
            throw new ArgumentException("The local path must not be a relative path.", nameof(localPath));

        (ICodeRepository Repository, string Folder)[] currentRepositories;

        lock (repoInitialization)
        {
            currentRepositories = [.. repoInitialization
                .Where(p => p.Value.IsCompletedSuccessfully)
                .Select(p => (Repository: p.Key, Folder: p.Value.Result!))];
        }

        foreach (var (repository, folder) in currentRepositories)
        {
            var relativePath = Path.GetRelativePath(folder, localPath);
            var isContainedInFolder = relativePath != localPath && relativePath is not ['.', '.', ..] or not [_, _, '\\' or '/', ..];
            if (isContainedInFolder)
                return repository.GetSourceCodeBrowserUrl(relativePath, selection);
        }

        throw new ArgumentException("The local path does not belong to any initialized repository.", nameof(localPath));
    }

    public Task<string> InitializeRepoAsync(ICodeRepository codeRepository, CancellationToken cancellationToken)
    {
        TaskCompletionSource<string> taskCompletionSource;

        lock (repoInitialization)
        {
            if (repoInitialization.TryGetValue(codeRepository, out var existingTask))
                return existingTask;

            taskCompletionSource = new();
            repoInitialization.Add(codeRepository, taskCompletionSource.Task);
        }

        taskCompletionSource.Mirror(CheckoutAsync());
        return taskCompletionSource.Task;

        async Task<string> CheckoutAsync()
        {
            var subfolderName = codeRepository.Name;

            for (var suffix = 2; !FileUtils.TryCreateChildDirectory(workspaceFolder, subfolderName); suffix++)
            {
                subfolderName = $"{codeRepository.Name}_{suffix}";
            }

            var repoFolder = Path.Join(workspaceFolder, subfolderName);
            await codeRepository.CheckoutAsync(repoFolder, cancellationToken);
            return repoFolder;
        }
    }

    public void Dispose()
    {
        FileUtils.DeleteDirectory(workspaceFolder, recursive: true, deleteReadonlyFiles: true);
    }
}
