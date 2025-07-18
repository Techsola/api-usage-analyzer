namespace ApiUsageAnalyzer;

public sealed record DiscoveredApiDeclarationSource(
    string RepositoryUrl,
    string Branch,
    string Commit);

