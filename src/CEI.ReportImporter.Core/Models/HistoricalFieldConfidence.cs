namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalFieldConfidence
{
    public HistoricalConfidenceLevel ReportNumber { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel Date { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel ProjectName { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel Temperature { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel Weather { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel Locations { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel Inspectors { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel PersonnelOnSite { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel DescriptionOfWork { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel DrawingsReviewed { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel Observations { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel NewDiscrepancies { get; init; } = HistoricalConfidenceLevel.Low;

    public HistoricalConfidenceLevel PreviousDiscrepancies { get; init; } = HistoricalConfidenceLevel.Low;
}
