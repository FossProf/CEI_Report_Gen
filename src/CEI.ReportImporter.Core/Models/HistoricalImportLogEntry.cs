namespace CEI.ReportImporter.Core.Models;

public sealed record HistoricalImportLogEntry
{
    public required DateTime TimestampUtc { get; init; }

    public required string SourceFileName { get; init; }

    public required int ReportNumber { get; init; }

    public required HistoricalImportItemStatus Result { get; init; }

    public required string Reason { get; init; }
}
