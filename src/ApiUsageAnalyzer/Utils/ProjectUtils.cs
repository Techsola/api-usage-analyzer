using Microsoft.CodeAnalysis;
using System.Xml.Linq;

namespace ApiUsageAnalyzer.Utils;

internal static class ProjectUtils
{
    public static string? GetTargetFrameworkIfMultitargeting(Project project)
    {
        if (project.Name is [.., _, _, ')'])
        {
            var index = project.Name.LastIndexOf('(', project.Name.Length - 2);
            if (index != -1)
            {
                return project.Name[(index + 1)..^1];
            }
        }

        return null;
    }

    public static async Task<string> GetTargetFrameworkAsync(Project project, CancellationToken cancellationToken)
    {
        var targetFramework = GetTargetFrameworkIfMultitargeting(project);
        if (targetFramework is not null)
            return targetFramework;

        await using var stream = File.OpenRead(project.FilePath
            ?? throw new ArgumentException("The project does not have a file path.", nameof(project)));

        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var propertyGroups = document.Root!.ElementsWithLocalName("PropertyGroup");

        targetFramework = propertyGroups.SelectMany(pg => pg.ElementsWithLocalName("TargetFramework")).SingleOrDefault()?.Value;
        if (targetFramework is not null)
            return targetFramework;

        targetFramework = propertyGroups.SelectMany(pg => pg.ElementsWithLocalName("TargetFrameworks")).SingleOrDefault()?.Value;
        if (targetFramework is not null)
        {
            if (targetFramework.Contains(';', StringComparison.Ordinal))
                throw new InvalidOperationException("GetTargetFrameworkIfMultitargeting would have handled this if the project came from MSBuildWorkspace.");
            return targetFramework;
        }

        var targetFrameworkVersion = propertyGroups.SelectMany(pg => pg.ElementsWithLocalName("TargetFrameworkVersion")).SingleOrDefault()?.Value;
        if (targetFrameworkVersion is not null)
        {
            if (targetFrameworkVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                targetFrameworkVersion = targetFrameworkVersion[1..];

            var targetFrameworkIdentifier = propertyGroups.SelectMany(pg => pg.ElementsWithLocalName("TargetFrameworkIdentifier")).SingleOrDefault()?.Value;
            if (targetFrameworkIdentifier is not (null or ".NETFramework"))
                throw new NotImplementedException("Non-.NET Framework TargetFrameworkIdentifier");

            return "net" + targetFrameworkVersion.Replace(".", "");
        }

        throw new NotImplementedException("The project does not have a TargetFramework property or a TargetFrameworkVersion property.");
    }
}
