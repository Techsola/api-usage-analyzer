using ApiUsageAnalyzer.Utils;
using System.Collections.Immutable;

namespace ApiUsageAnalyzer;

public sealed class RealtimeResultFormatter
{
    private readonly string assemblyName;
    private readonly Action<string> formattedResultUpdated;
    private readonly DelayedProcessor<object> processor;
    private readonly Dictionary<string, List<DiscoveredReference>> referencesByApiName = new();
    private readonly Dictionary<string, List<DiscoveredApi>> availableApisByName = new();
    private readonly List<DiscoveredApiDeclarationSource> apiDeclarationSources = [];

    public RealtimeResultFormatter(string assemblyName, Action<string> formattedResultUpdated, TimeSpan updateInterval)
    {
        this.assemblyName = assemblyName;
        this.formattedResultUpdated = formattedResultUpdated;
        processor = new DelayedProcessor<object>(IngestAndCompile, updateInterval);
    }

    public void Enqueue(DiscoveredApiDeclarationSource discoveredApiDeclarationSource)
    {
        processor.Enqueue(discoveredApiDeclarationSource);
    }

    public void Enqueue(DiscoveredApi discoveredApi)
    {
        processor.Enqueue(discoveredApi);
    }

    public void Enqueue(DiscoveredReference discoveredReference)
    {
        processor.Enqueue(discoveredReference);
    }

    public async Task CompleteAsync()
    {
        await processor.CompleteAsync();
    }

    private void IngestAndCompile(ImmutableArray<object> queuedItems)
    {
        foreach (var queuedItem in queuedItems)
        {
            switch (queuedItem)
            {
                case DiscoveredApiDeclarationSource source:
                {
                    apiDeclarationSources.Add(source);
                    break;
                }
                case DiscoveredApi api:
                {
                    if (!availableApisByName.TryGetValue(api.Api, out var list))
                    {
                        list = [];
                        availableApisByName[api.Api] = list;
                    }
                    list.Add(api);
                    break;
                }
                case DiscoveredReference reference:
                {
                    if (!referencesByApiName.TryGetValue(reference.Api, out var list))
                    {
                        list = [];
                        referencesByApiName[reference.Api] = list;
                    }
                    list.Add(reference);
                    break;
                }
                default:
                {
                    throw new NotImplementedException(queuedItem.GetType().FullName);
                }
            }
        }

        var remainingApis = availableApisByName.ToDictionary();
        var (currentApiReferences, removedApiReferences) = referencesByApiName.Partition(referencedApi => remainingApis.Remove(referencedApi.Key));

        foreach (var api in remainingApis.Where(entry => entry.Value.All(discoveredApi => discoveredApi.ExcludeFromUnusedReport)).ToList())
            remainingApis.Remove(api.Key);

        using var writer = new StringWriter();
        var kdlWriter = new KdlWriter(writer);

        kdlWriter.StartNode("assembly");
        kdlWriter.WriteStringValue(assemblyName);

        kdlWriter.StartNode("current-api-source");
        foreach (var source in apiDeclarationSources)
        {
            kdlWriter.StartNode("repo");
            kdlWriter.WriteStringValue(source.RepositoryUrl);
            kdlWriter.EndNode();
            kdlWriter.StartNode("branch");
            kdlWriter.WriteStringValue(source.Branch);
            kdlWriter.EndNode();
            kdlWriter.StartNode("commit");
            kdlWriter.WriteStringValue(source.Commit);
            kdlWriter.EndNode();
        }
        kdlWriter.EndNode(); // current-api-source

        kdlWriter.StartNode("stats");

        kdlWriter.StartNode("unused-api-count");
        kdlWriter.WriteNumberValue(remainingApis.Count);
        kdlWriter.EndNode();

        kdlWriter.StartNode("removed-api-count");
        kdlWriter.WriteNumberValue(removedApiReferences.Count);
        kdlWriter.EndNode();

        kdlWriter.StartNode("used-api-count");
        kdlWriter.WriteNumberValue(currentApiReferences.Count);
        kdlWriter.EndNode();

        foreach (var byVersion in referencesByApiName.Values.SelectMany(list => list).GroupBy(r => r.ApiVersion).OrderBy(g => g.Key))
        {
            kdlWriter.StartNode("version");
            kdlWriter.WriteStringValue(byVersion.Key);

            foreach (var byRepo in byVersion.GroupBy(r => r.Repository).OrderBy(g => g.Key))
            {
                kdlWriter.StartNode("repo");
                kdlWriter.WriteStringValue(byRepo.Key);

                using (kdlWriter.EnterSingleLineMode())
                {
                    foreach (var tfm in byRepo.Select(r => r.TargetFramework).Distinct().Order())
                    {
                        kdlWriter.StartNode("tfm");
                        kdlWriter.WriteStringValue(tfm);
                        kdlWriter.EndNode();
                    }
                }

                kdlWriter.EndNode(); // repo
            }

            kdlWriter.EndNode(); // version
        }

        kdlWriter.EndNode(); // stats

        if (remainingApis.Any())
        {
            kdlWriter.WriteLine();
            kdlWriter.StartNode("unused-apis");
            WriteUnusedApis(kdlWriter, remainingApis);
            kdlWriter.EndNode();
        }

        if (removedApiReferences.Any())
        {
            kdlWriter.WriteLine();
            kdlWriter.StartNode("removed-apis");
            WriteApiReferences(kdlWriter, removedApiReferences);
            kdlWriter.EndNode();
        }

        if (currentApiReferences.Any())
        {
            kdlWriter.WriteLine();
            WriteApiReferences(kdlWriter, currentApiReferences);
        }

        kdlWriter.EndNode(); // assembly

        formattedResultUpdated(writer.ToString());
    }

    private static void WriteUnusedApis(KdlWriter kdlWriter, IEnumerable<KeyValuePair<string, List<DiscoveredApi>>> apis)
    {
        foreach (var (apiName, declarations) in apis.OrderBy(g => g.Key))
        {
            kdlWriter.StartNode("api");
            kdlWriter.WriteStringValue(apiName, forceQuotes: true);

            kdlWriter.WritePropertyName("url");
            kdlWriter.WriteStringValue(declarations.First().DeclarationUrl);

            using (kdlWriter.EnterSingleLineMode())
            {
                foreach (var tfm in declarations.Select(r => r.TargetFramework).Distinct().Order())
                {
                    kdlWriter.StartNode("tfm");
                    kdlWriter.WriteStringValue(tfm);
                    kdlWriter.EndNode();
                }
            }

            kdlWriter.EndNode(); // api
        }
    }

    private void WriteApiReferences(KdlWriter kdlWriter, IEnumerable<KeyValuePair<string, List<DiscoveredReference>>> apiReferences)
    {
        foreach (var (apiName, references) in apiReferences.OrderBy(g => g.Key))
        {
            kdlWriter.StartNode("api");
            kdlWriter.WriteStringValue(apiName, forceQuotes: true);

            if (availableApisByName.TryGetValue(apiName, out var apiDeclarations))
            {
                kdlWriter.WritePropertyName("url");
                kdlWriter.WriteStringValue(apiDeclarations.First().DeclarationUrl);
            }

            foreach (var byVersion in references.GroupBy(r => r.ApiVersion).OrderBy(g => g.Key))
            {
                kdlWriter.StartNode("version");
                kdlWriter.WriteStringValue(byVersion.Key);

                foreach (var byRepo in byVersion.GroupBy(r => r.Repository).OrderBy(g => g.Key))
                {
                    kdlWriter.StartNode("repo");
                    kdlWriter.WriteStringValue(byRepo.Key);

                    var representedTfms = new List<string>();

                    foreach (var byReference in byRepo.Where(r => r.ReferenceUrl is not null).GroupBy(r => r.ReferenceUrl!).OrderBy(g => g.Key))
                    {
                        kdlWriter.StartNode("reference");
                        kdlWriter.WriteStringValue(byReference.Key);

                        using (kdlWriter.EnterSingleLineMode())
                        {
                            foreach (var tfm in byReference.Select(r => r.TargetFramework).Distinct().Order())
                            {
                                if (!representedTfms.Contains(tfm))
                                    representedTfms.Add(tfm);

                                kdlWriter.StartNode("tfm");
                                kdlWriter.WriteStringValue(tfm);
                                kdlWriter.EndNode();
                            }
                        }

                        kdlWriter.EndNode(); // reference
                    }

                    var generatedReferenceTfms = byRepo.Where(r => r.ReferenceUrl is null).Select(r => r.TargetFramework).ToHashSet();
                    if (!generatedReferenceTfms.IsSubsetOf(representedTfms))
                    {
                        // At least one of the target frameworks had _only_ generated source references.
                        kdlWriter.StartNode("reference");
                        kdlWriter.WriteStringValue("in-generated-source");

                        using (kdlWriter.EnterSingleLineMode())
                        {
                            foreach (var tfm in generatedReferenceTfms.Order())
                            {
                                kdlWriter.StartNode("tfm");
                                kdlWriter.WriteStringValue(tfm);
                                kdlWriter.EndNode();
                            }
                        }

                        kdlWriter.EndNode(); // reference
                    }

                    kdlWriter.EndNode(); // repo
                }

                kdlWriter.EndNode(); // version
            }

            kdlWriter.EndNode(); // api
        }
    }
}
