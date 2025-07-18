namespace ApiUsageAnalyzer.SourceControl;

public interface ICodeRepository
{
    string Name { get; }
    string BrowserUrl { get; }

    string GetSourceCodeBrowserUrl(string path, (int Line, int LineEnd, int LineStartColumn, int LineEndColumn)? selection = null);

    Task<List<string>> GetAllFilePathsAsync(CancellationToken cancellationToken);
    Task<Stream> GetItemContentAsync(string path, CancellationToken cancellationToken);
    Task CheckoutAsync(string localPath, CancellationToken cancellationToken);
}
