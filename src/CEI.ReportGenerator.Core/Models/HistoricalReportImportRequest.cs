namespace CEI.ReportGenerator.Core.Models;

public sealed record HistoricalReportImportRequest
{
    public int Number { get; init; }

    public DateTime Date { get; init; }

    public string Temperature { get; init; } = string.Empty;

    public string Weather { get; init; } = string.Empty;

    public string Locations { get; init; } = string.Empty;

    public string Inspectors { get; init; } = string.Empty;

    public string PersonnelOnSite { get; init; } = string.Empty;

    public string DescriptionOfWork { get; init; } = string.Empty;

    public string DrawingsReviewed { get; init; } = string.Empty;

    public string Observations { get; init; } = string.Empty;

    public string NewDiscrepancies { get; init; } = string.Empty;

    public string PreviousDiscrepancies { get; init; } = string.Empty;

    public string SourceDocumentPath { get; init; } = string.Empty;

    public DateTime? SourceCreatedUtc { get; init; }

    public string ParserProfile { get; init; } = "CEI-SPIN-Legacy-v1";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
