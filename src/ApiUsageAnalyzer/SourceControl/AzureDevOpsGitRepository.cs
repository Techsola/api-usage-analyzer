using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using System.Text;
using System.Web;

namespace ApiUsageAnalyzer.SourceControl;

public sealed class AzureDevOpsGitRepository(GitHttpClient client, GitRepository repository) : ICodeRepository
{
    public string Name => repository.Name;
    public string BrowserUrl => repository.WebUrl;

    public async Task<List<string>> GetAllFilePathsAsync(CancellationToken cancellationToken)
    {
        List<GitItem> items;
        try
        {
            items = await client.GetItemsAsync(repository.Id, recursionLevel: VersionControlRecursionType.Full, includeContentMetadata: false, cancellationToken: cancellationToken);
        }
        catch (VssServiceException ex) when (ex.GetVssErrorCode() == 403403/*Cannot find any branches*/)
        {
            return [];
        }

        return [.. from item in items where !item.IsFolder select item.Path];
    }

    public async Task<Stream> GetItemContentAsync(string path, CancellationToken cancellationToken)
    {
        return await client.GetItemContentAsync(repository.Id, path, cancellationToken: cancellationToken);
    }

    public async Task CheckoutAsync(string localPath, CancellationToken cancellationToken)
    {
        await GitUtils.ShallowCloneAsync(repository.RemoteUrl, localPath);
    }

    public string GetSourceCodeBrowserUrl(string path, (int Line, int LineEnd, int LineStartColumn, int LineEndColumn)? selection = null)
    {
        // Normalize path slashes to the default direction when browsing in Azure DevOps
        var encodedPath = HttpUtility.UrlEncode(path.Replace('\\', '/'));
        // Undo encoding slashes for aesthetics, matching the defaults seen when browsing in Azure DevOps
        encodedPath = encodedPath.Replace("%2f", "/", StringComparison.OrdinalIgnoreCase);

        var query = new StringBuilder("?version=GBmain&path=");
        query.Append(encodedPath);

        if (selection is not null)
        {
            query.Append("&line=").Append(selection.Value.Line);
            query.Append("&lineEnd=").Append(selection.Value.LineEnd);
            query.Append("&lineStartColumn=").Append(selection.Value.LineStartColumn);
            query.Append("&lineEndColumn=").Append(selection.Value.LineEndColumn);
        }

        return new UriBuilder(repository.WebUrl) { Port = -1, Query = query.ToString() }.ToString();
    }
}
