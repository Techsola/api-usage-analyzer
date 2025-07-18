namespace ApiUsageAnalyzer;

public sealed record DiscoveredApi(
    string Api,
    string DeclarationUrl,
    string TargetFramework,
    bool ExcludeFromUnusedReport);

