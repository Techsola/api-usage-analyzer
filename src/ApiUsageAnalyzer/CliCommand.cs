using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ApiUsageAnalyzer.SourceControl;
using ApiUsageAnalyzer.Utils;
using Microsoft.VisualStudio.Services.Common;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ApiUsageAnalyzer;

[Description("Produces a report of API usage across code repositories, with version and target framework statistics, listing unused/removed APIs, and links to each usage. The output file is <assembly-name>.kdl in the current working directory, and the file is rewritten in real time as the analysis proceeds.")]
public sealed class CliCommand : AsyncCommand<CliCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<assembly-name>")]
        [Description("The name of the assembly defining the APIs whose usage will be reported")]
        public required string AssemblyName { get; set; }

        [CommandOption("--gh <users>")]
        [Description("The GitHub users which own the repositories to be discovered. Repeat --gh to specify more than one. Currently, only public repositories are supported.")]
        public string[] GitHubUsers { get; set; } = [];

        [CommandOption("--azdo <urls>")]
        [Description("The Azure DevOps project collection URLs from which repositories are discovered. Repeat --azdo to specify more than one. Currently, only Git repositories are supported.")]
        public string[] AzureDevOpsProjectCollections { get; set; } = [];

        [CommandOption("--defining-repo-name <name>")]
        [Description("If specified, only repositories with this name will be searched for the project that defines the API assembly.")]
        public string? DefiningRepoName { get; set; }

        [CommandOption("--defining-repo-url <url>")]
        [Description("The remote URL of an repository containing the project that defines the API assembly. The repository's current HEAD will be used to define the set of available APIs in the API assembly which will be used for the unused/removed API comparison.")]
        public string? DefiningRepoUrl { get; set; }

        [CommandOption("--repo-package-id-filter <regex>")]
        [Description("If specified, repositories will be skipped unless they contain a project with a direct reference to a NuGet package with an ID that matches this regular expression.")]
        public Regex? RepoPackageIdFilter { get; set; }

        [CommandOption("--open-in-vs-code"), DefaultValue(false)]
        [Description("If specified, the output file will automatically open in VS Code. VS Code can monitor the live updates to the results.")]
        public bool OpenInVSCode { get; set; }

        public override ValidationResult Validate()
        {
            if (DefiningRepoName is not null && DefiningRepoUrl is not null)
                return ValidationResult.Error("--defining-repo-name and --defining-repo-url may not be specified together.");

            return base.Validate();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var resultFile = Path.GetFullPath(settings.AssemblyName + ".kdl");

        if (settings.OpenInVSCode)
        {
            Console.WriteLine($"Opening result file {resultFile} in VS Code");
            File.WriteAllText(resultFile, "");
            Process.Start(new ProcessStartInfo("code", [resultFile]) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        }

        var resultFormatter = new RealtimeResultFormatter(
            settings.AssemblyName,
            newResult => File.WriteAllText(resultFile, newResult),
            updateInterval: TimeSpan.FromSeconds(5));

        var analysisCompleted = false;

        using (var localWorkspace = new LocalWorkspace())
        using (ConsoleUtils.HandleCtrlC(out var cancellationToken))
        {
            try
            {
                Console.WriteLine("Discovering repositories...");

                var repositoryLoadTasks =
                    settings.AzureDevOpsProjectCollections.Select(url =>
                        SourceControlHosts.GetAzureDevOpsRepositoriesAsync(url, new VssCredentials(), cancellationToken))
                    .Concat(settings.GitHubUsers.Select(owner =>
                        SourceControlHosts.GetGitHubPublicRepositoriesAsync(owner, cancellationToken)));

                var codeRepositories = (await Task.WhenAll(repositoryLoadTasks))
                    .SelectMany(repositories => repositories)
                    .ToImmutableArray();

                await new Analyzer(settings, resultFormatter, localWorkspace, codeRepositories, cancellationToken).RunAsync();

                analysisCompleted = true;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await resultFormatter.CompleteAsync();
        Console.WriteLine(analysisCompleted ? "Analysis complete." : "Analysis canceled.");

        return analysisCompleted ? 0 : 1;
    }
}
