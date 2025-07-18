using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Collections.Immutable;

namespace ApiUsageAnalyzer.SourceControl;

public sealed class AzureDevOps
{
    public static async Task<ImmutableArray<ICodeRepository>> GetRepositoriesAsync(string collectionUrl, VssCredentials credentials, CancellationToken cancellationToken)
    {
        var connection = new VssConnection(new Uri(collectionUrl), credentials);
        var gitClient = await connection.GetClientAsync<GitHttpClient>(cancellationToken);

        var repositories = await gitClient.GetRepositoriesAsync(cancellationToken: cancellationToken);

        return [.. from repository in repositories select new AzureDevOpsGitRepository(gitClient, repository)];
    }
}
