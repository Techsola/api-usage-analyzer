using ApiUsageAnalyzer.SourceControl;
using ApiUsageAnalyzer.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Immutable;

namespace ApiUsageAnalyzer;

internal sealed class Analyzer(
    CliCommand.Settings settings,
    RealtimeResultFormatter resultFormatter,
    LocalWorkspace localWorkspace,
    ImmutableArray<ICodeRepository> codeRepositories,
    CancellationToken cancellationToken)
{
    private static readonly SymbolDisplayFormat NormalizedApiIdFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        ~SymbolDisplayGenericsOptions.None,
        SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        SymbolDisplayDelegateStyle.NameOnly,
        SymbolDisplayExtensionMethodStyle.StaticMethod,
        ~SymbolDisplayParameterOptions.None,
        SymbolDisplayPropertyStyle.NameOnly,
        ~SymbolDisplayLocalOptions.None,
        SymbolDisplayKindOptions.None,
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public async Task RunAsync()
    {
        await Task.WhenAll(DiscoverCurrentApisAsync(), DiscoverUsagesAsync());
    }

    private async Task DiscoverCurrentApisAsync()
    {
        var definingProjectPaths = new List<string>();

        if (settings.DefiningRepoUrl is not null)
        {
            if (!new Uri(settings.DefiningRepoUrl).IsFile)
                throw new NotSupportedException("Only local file paths are currently supported for the defining repository.");

            var repository = await LocalGitRepository.OpenAsync(settings.DefiningRepoUrl);

            if (!await RepositoryFilters.RepoBuildsLibraryAsync(repository, settings.AssemblyName, cancellationToken))
            {
                Console.Error.WriteLine($"No projects found in {settings.DefiningRepoUrl} that build the library {settings.AssemblyName}. Its current API surface will be unknown.");
            }
            else
            {
                var projectPaths = await GetDefiningProjectPathsAsync(repository);
                definingProjectPaths.AddRange(projectPaths);
            }
        }
        else
        {
            var definingProjectRepositories = settings.DefiningRepoName is null
                ? codeRepositories
                : [.. codeRepositories.Where(r => r.Name == settings.DefiningRepoName)];

            await Parallel.ForEachAsync(definingProjectRepositories, cancellationToken, async (repository, cancellationToken) =>
            {
                if (!await RepositoryFilters.RepoBuildsLibraryAsync(repository, settings.AssemblyName, cancellationToken))
                    return;

                var projectPaths = await GetDefiningProjectPathsAsync(repository);
                lock (definingProjectPaths)
                    definingProjectPaths.AddRange(projectPaths);
            });
        }

        if (definingProjectPaths is not [_])
        {
            Console.Error.WriteLine(definingProjectPaths is []
                ? $"No projects found that build the library {settings.AssemblyName}. Its current API surface will be unknown."
                : $"Multiple projects found that build the library {settings.AssemblyName}. Its current API surface will be unknown.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine("Restoring NuGet packages for project " + definingProjectPaths[0]);
        var restoreResult = await ProcessUtils.RunAsync("dotnet", workingDirectory: Path.GetDirectoryName(definingProjectPaths[0])!, ["restore", Path.GetFileName(definingProjectPaths[0])]);
        if (restoreResult.ExitCode != 0)
            Console.Error.WriteLine($"{restoreResult}{Environment.NewLine}Results may be incomplete (failed to restore NuGet packages for solution {definingProjectPaths[0]}).");

        using var workspace = MSBuildWorkspace.Create();
        Console.WriteLine("Opening project " + definingProjectPaths[0]);
        var firstTfmProject = await workspace.OpenProjectAsync(definingProjectPaths[0], cancellationToken: cancellationToken);
        var projects = firstTfmProject.Solution.Projects.Where(p => p.FilePath == firstTfmProject.FilePath).ToArray();

        foreach (var project in projects)
        {
            var targetFramework = await ProjectUtils.GetTargetFrameworkAsync(project, cancellationToken);
            Console.WriteLine($"Analyzing {targetFramework} API surface of {definingProjectPaths[0]}");
            var compilation = (await project.GetCompilationAsync(cancellationToken))!;

            var compileErrors = compilation.GetDiagnostics(cancellationToken).RemoveAll(d => d.Severity != DiagnosticSeverity.Error);
            if (compileErrors.Any())
            {
                Console.Error.WriteLine($"API declarations may be incorrect for {settings.AssemblyName} due to the following errors:"
                    + string.Concat(compileErrors.Select(e => Environment.NewLine + "  " + e)));
            }

            PublicApiSurface.GetDeclaredPublicApis(
                apiFound: args =>
                {
                    resultFormatter.Enqueue(new DiscoveredApi(
                        Api: args.Definition.ToDisplayString(NormalizedApiIdFormat),
                        DeclarationUrl: localWorkspace.GetBrowserUrl(args.Location.Path, (
                            args.Location.StartLinePosition.Line + 1,
                            args.Location.EndLinePosition.Line + 1,
                            args.Location.StartLinePosition.Character + 1,
                            args.Location.EndLinePosition.Character + 1)),
                        targetFramework,
                        ExcludeFromUnusedReport: args.Definition.IsOverride
                            // Accessors will be shown
                            || args.Definition is IPropertySymbol));
                },
                compilation,
                cancellationToken);
        }

        Console.WriteLine($"Current API surface of {settings.AssemblyName} is available.");
    }

    private async Task<ImmutableArray<string>> GetDefiningProjectPathsAsync(ICodeRepository repository)
    {
        Console.WriteLine("Cloning " + repository.BrowserUrl);

        string repoFolder;
        try
        {
            repoFolder = await localWorkspace.InitializeRepoAsync(repository, cancellationToken);
        }
        catch (ProcessExecutionException ex)
        {
            Console.Error.WriteLine($"Failed to clone repository {repository.BrowserUrl}: {ex.Message}");
            return [];
        }

        var projectPaths = ImmutableArray.CreateBuilder<string>();
        var branchStatus = await GitUtils.GetBranchStatusAsync(repoFolder);

        foreach (var projectPath in Directory.GetFiles(repoFolder, settings.AssemblyName + ".?*proj", SearchOption.AllDirectories))
        {
            if (!Path.GetFileNameWithoutExtension(projectPath).Equals(settings.AssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                // The original filter might match LibraryName.Tests.csproj, for example.
                continue;
            }

            Console.WriteLine($"Found project {localWorkspace.GetBrowserUrl(projectPath)} that builds the library {settings.AssemblyName}");
            projectPaths.Add(projectPath);

            resultFormatter.Enqueue(new DiscoveredApiDeclarationSource(
                repository.BrowserUrl,
                Branch: branchStatus.Head,
                Commit: branchStatus.Oid));
        }

        return projectPaths.DrainToImmutable();
    }

    private async Task DiscoverUsagesAsync()
    {
        await Parallel.ForEachAsync(codeRepositories, cancellationToken, async (repository, cancellationToken) =>
        {
            if (!await RepositoryFilters.RepoReferencesLibraryPackagesAsync(
                repository,
                packageId => settings.RepoPackageIdFilter is null || settings.RepoPackageIdFilter.IsMatch(packageId),
                cancellationToken))
            {
                return;
            }

            Console.WriteLine("Cloning " + repository.BrowserUrl);

            string repoFolder;
            try
            {
                repoFolder = await localWorkspace.InitializeRepoAsync(repository, cancellationToken);
            }
            catch (ProcessExecutionException ex)
            {
                Console.Error.WriteLine($"Failed to clone repository {repository.BrowserUrl}: {ex.Message}");
                return;
            }

            foreach (var solutionPath in Directory.GetFiles(repoFolder, "*.sln", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine("Restoring NuGet packages for solution " + solutionPath);
                var restoreResult = await ProcessUtils.RunAsync("dotnet", workingDirectory: Path.GetDirectoryName(solutionPath)!, ["restore", Path.GetFileName(solutionPath)]);
                if (restoreResult.ExitCode != 0)
                    Console.Error.WriteLine($"{restoreResult}{Environment.NewLine}Results may be incomplete (failed to restore NuGet packages for solution {solutionPath}).");

                using var workspace = MSBuildWorkspace.Create();
                Console.WriteLine("Opening solution " + solutionPath);
                var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);

                foreach (var project in solution.Projects)
                {
                    var targetFramework = await ProjectUtils.GetTargetFrameworkAsync(project, cancellationToken);

                    var referencesAssembly = project.MetadataReferences.OfType<PortableExecutableReference>()
                        .Select(r => r.FilePath)
                        .NotNull()
                        .Any(path => Path.GetFileNameWithoutExtension(path).Equals(settings.AssemblyName, StringComparison.OrdinalIgnoreCase))
                        || project.ProjectReferences.Any(r => solution.GetProject(r.ProjectId)!.AssemblyName == settings.AssemblyName);

                    if (!referencesAssembly)
                    {
                        Console.WriteLine($"Project {project.FilePath} has no direct dependency on {settings.AssemblyName} for target {targetFramework}, analyzing usage");
                        continue;
                    }

                    Console.WriteLine($"Project {project.FilePath} has a direct dependency on {settings.AssemblyName} for target {targetFramework}, analyzing usage");

                    var compilation = (await project.GetCompilationAsync(cancellationToken))!;

                    var compileErrors = compilation.GetDiagnostics(cancellationToken).RemoveAll(d => d.Severity != DiagnosticSeverity.Error);
                    if (compileErrors.Any())
                    {
                        Console.Error.WriteLine($"Results may be incomplete for {project.FilePath} due to the following errors:"
                            + string.Concat(compileErrors.Select(e => Environment.NewLine + "  " + e)));
                    }

                    FindAllReferences.FindReferences(
                        referenceFilter: assembly => assembly.Name.Equals(settings.AssemblyName, StringComparison.OrdinalIgnoreCase),
                        referenceFound: args =>
                        {
                            resultFormatter.Enqueue(new DiscoveredReference(
                                Api: args.Definition.ToDisplayString(NormalizedApiIdFormat),
                                ApiVersion: args.Definition.ContainingAssembly.Identity.Version.ToString(),
                                Repository: repository.BrowserUrl,
                                ReferencingSymbol: args.ReferencingSymbol.ToDisplayString(NormalizedApiIdFormat),
                                ReferenceUrl: args.Location.Path.EndsWith(".g.cs")
                                    ? null // Can't build a URL to a file that is generated during build
                                    : localWorkspace.GetBrowserUrl(args.Location.Path, (
                                        args.Location.StartLinePosition.Line + 1,
                                        args.Location.EndLinePosition.Line + 1,
                                        args.Location.StartLinePosition.Character + 1,
                                        args.Location.EndLinePosition.Character + 1)),
                                targetFramework));
                        },
                        compilation,
                        cancellationToken);

                    Console.WriteLine($"Finished analyzing project {project.FilePath} for {targetFramework}");
                }
            }
        });
    }
}
