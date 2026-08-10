namespace CEI.ReportImporter.Core.Models;

public sealed record HistoricalImportBatchResult
{
    public required Guid SessionId { get; init; }

    public required int SelectedCount { get; init; }

    public required int ImportedCount { get; init; }

    public required int SkippedCount { get; init; }

    public required int DuplicateCount { get; init; }

    public required int ErrorCount { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required bool Cancelled { get; init; }

    public required IReadOnlyList<HistoricalImportLogEntry> LogEntries { get; init; }
}
