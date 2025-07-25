using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ApiUsageAnalyzer.SourceControl;

public sealed class GitHubRepository(HttpClient client, string user, string name, string browserUrl, string cloneUrl) : ICodeRepository
{
    public string Name => name;

    public string BrowserUrl => browserUrl;

    public async Task CheckoutAsync(string localPath, CancellationToken cancellationToken)
    {
        await GitUtils.ShallowCloneAsync(cloneUrl, localPath);
    }

    public async Task<List<string>> GetAllFilePathsAsync(CancellationToken cancellationToken)
    {        
        using var document = await client.GetFromJsonAsync<JsonDocument>($"/repos/{user}/{name}/git/trees/HEAD?recursive=1", cancellationToken);

        if (document.RootElement.GetProperty("truncated").GetBoolean())
            throw new NotImplementedException("Support for handling truncated results is not yet implemented");

        return [..
            from item in document.RootElement.GetProperty("tree").EnumerateArray()
            where item.GetProperty("type").GetString() == "blob"
            select item.GetProperty("path").GetString()];
    }

    public async Task<Stream> GetItemContentAsync(string path, CancellationToken cancellationToken)
    {
        return await client.GetStreamAsync($"https://raw.githubusercontent.com/{user}/{name}/HEAD/{path}", cancellationToken);
    }

    public string GetSourceCodeBrowserUrl(string path, (int Line, int LineEnd, int LineStartColumn, int LineEndColumn)? selection = null)
    {
        var builder = new StringBuilder();
        builder.Append($"{browserUrl}/blob/HEAD/{path.Replace('\\', '/')}");

        if (selection is not null)
        {
            builder.Append($"#L{selection.Value.Line}");
            if (selection.Value.LineEnd > selection.Value.Line)
                builder.Append($"-L{selection.Value.LineEnd}");
        }

        return builder.ToString();
    }
}
