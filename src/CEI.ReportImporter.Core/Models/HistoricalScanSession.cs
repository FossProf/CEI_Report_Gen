namespace CEI.ReportImporter.Core.Models;

public sealed record HistoricalScanSession
{
    public required Guid SessionId { get; init; }

    public required string SourceFolder { get; init; }

    public required bool IncludeSubfolders { get; init; }

    public required DateTime StartedUtc { get; init; }

    public required DateTime CompletedUtc { get; init; }

    public required string ParserProfile { get; init; }

    public required IReadOnlyList<HistoricalScanResult> Results { get; init; }

    public int FilesDiscovered => Results.Count;

    public int ParsedCount => Results.Count(result => result.ParseSucceeded);

    public int FailedCount => Results.Count(result => !result.ParseSucceeded);

    public int WarningCount => Results.Sum(result => result.WarningCount);
}
