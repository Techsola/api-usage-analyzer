using ApiUsageAnalyzer.SourceControl;
using System.Xml.Linq;

namespace ApiUsageAnalyzer;

public static class RepositoryFilters
{
    public static async Task<bool> RepoBuildsLibraryAsync(ICodeRepository codeRepository, string libraryAssemblyName, CancellationToken cancellationToken)
    {
        foreach (var path in await codeRepository.GetAllFilePathsAsync(cancellationToken))
        {
            if (Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(path).Equals(libraryAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<bool> RepoReferencesLibraryPackagesAsync(ICodeRepository codeRepository, Func<string, bool> libraryPackageIdFilter, CancellationToken cancellationToken)
    {
        var paths = await codeRepository.GetAllFilePathsAsync(cancellationToken);

        foreach (var path in paths)
        {
            if (string.Equals(Path.GetFileName(path), "packages.config", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = await codeRepository.GetItemContentAsync(path, cancellationToken);
                var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

                var hasDirectPackagesPassingFilter = document.Root!.ElementsWithLocalName("package").Any(p =>
                    p.Attribute("id")?.Value is { } id && libraryPackageIdFilter(id));

                if (hasDirectPackagesPassingFilter) return true;
            }

            if (Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = await codeRepository.GetItemContentAsync(path, cancellationToken);
                var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

                var hasDirectPackagesPassingFilter = document.Root!.ElementsWithLocalName("ItemGroup")
                    .SelectMany(pg => pg.ElementsWithLocalName("PackageReference"))
                    .SelectMany(pr => pr.Attribute("Include")?.Value is { } includes
                        ? includes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : [])
                    .Any(libraryPackageIdFilter);

                if (hasDirectPackagesPassingFilter) return true;
            }
        }

        if (paths.Any(path => string.Equals(Path.GetFileName(path), "project.json", StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException("Legacy project.json support");

        return false;
    }
}
