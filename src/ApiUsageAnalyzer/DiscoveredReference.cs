namespace ApiUsageAnalyzer;

public sealed record DiscoveredReference(
    string Api,
    string ApiVersion,
    string Repository,
    string ReferencingSymbol,
    string? ReferenceUrl,
    string TargetFramework);
