namespace CEI.ReportImporter.Core.Models;

public sealed record HistoricalFieldExtractions
{
    public FieldExtraction<int?> ReportNumber { get; init; } = new();

    public FieldExtraction<DateTime?> InspectionDate { get; init; } = new();

    public FieldExtraction<string> Temperature { get; init; } = new();

    public FieldExtraction<string> Weather { get; init; } = new();

    public FieldExtraction<string> Locations { get; init; } = new();

    public FieldExtraction<string> Inspectors { get; init; } = new();

    public FieldExtraction<string> PersonnelOnSite { get; init; } = new();

    public FieldExtraction<string> DescriptionOfWork { get; init; } = new();

    public FieldExtraction<string> DrawingsReviewed { get; init; } = new();

    public FieldExtraction<string> Observations { get; init; } = new();

    public FieldExtraction<string> NewDiscrepancies { get; init; } = new();

    public FieldExtraction<string> PreviousDiscrepancies { get; init; } = new();
}
