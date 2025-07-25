using Microsoft.Azure.Pipelines.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;

namespace ApiUsageAnalyzer.SourceControl;

public static class SourceControlHosts
{
    public static async Task<ImmutableArray<ICodeRepository>> GetAzureDevOpsRepositoriesAsync(string collectionUrl, VssCredentials credentials, CancellationToken cancellationToken)
    {
        var connection = new VssConnection(new Uri(collectionUrl), credentials);
        var gitClient = await connection.GetClientAsync<GitHttpClient>(cancellationToken);

        var repositories = await gitClient.GetRepositoriesAsync(cancellationToken: cancellationToken);

        return [.. from repository in repositories select new AzureDevOpsGitRepository(gitClient, repository)];
    }

    public static async Task<ImmutableArray<ICodeRepository>> GetGitHubPublicRepositoriesAsync(string user, CancellationToken cancellationToken)
    {
        var client = new HttpClient(new RateLimitHandler())
        {
            BaseAddress = new Uri("https://api.github.com"),
            DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue(new ProductHeaderValue("test")) } },
        };

        using var document = await client.GetFromJsonAsync<JsonDocument>($"/users/{user}/repos", cancellationToken);

        return [..
            from repository in document.RootElement.EnumerateArray()
            select new GitHubRepository(
                client,
                user,
                name: repository.GetProperty("name").GetString(),
                browserUrl: repository.GetProperty("html_url").GetString(),
                cloneUrl: repository.GetProperty("clone_url").GetString())];
    }
}
