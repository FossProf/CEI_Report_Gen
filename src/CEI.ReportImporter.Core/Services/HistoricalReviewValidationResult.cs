namespace CEI.ReportImporter.Core.Services;

public sealed record HistoricalReviewValidationResult(
    bool CanMarkReady,
    IReadOnlyList<string> Messages);
