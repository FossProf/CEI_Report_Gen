namespace CEI.ReportImporter.Core.Models;

public enum HistoricalImportItemStatus
{
    Ready,
    Duplicate,
    Conflict,
    ParseError,
    MissingData,
    Imported,
    Skipped
}
