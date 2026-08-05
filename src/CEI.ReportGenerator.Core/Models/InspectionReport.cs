namespace CEI.ReportGenerator.Core.Models;

public enum ReportStatus
{
    Draft,
    Final
}

public sealed class InspectionReport
{
    public int Number { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Draft;

    public DateTime Date { get; set; } = DateTime.Today;

    public string Temperature { get; set; } = string.Empty;

    public string Weather { get; set; } = string.Empty;

    public string Locations { get; set; } = string.Empty;

    public string Inspectors { get; set; } = string.Empty;

    public string PersonnelOnSite { get; set; } = string.Empty;

    public string DescriptionOfWork { get; set; } = string.Empty;

    public string DrawingsReviewed { get; set; } = string.Empty;

    public string Observations { get; set; } = string.Empty;

    public string NewDiscrepancies { get; set; } = string.Empty;

    public string PreviousDiscrepancies { get; set; } = string.Empty;

    public List<Photo> Photos { get; set; } = new();

    public string OutputFileName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
