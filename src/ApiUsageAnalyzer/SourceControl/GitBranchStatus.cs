namespace ApiUsageAnalyzer.SourceControl;

public sealed record GitBranchStatus(string Oid, string Head, (string Upstream, int Ahead, int Behind)? Tracking);
