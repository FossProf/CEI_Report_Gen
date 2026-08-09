namespace CEI.ReportGenerator.Core.Models;

public sealed class ReportSearchCriteria
{
    public string SearchText { get; init; } = string.Empty;

    public ReportStatus? Status { get; init; }

    public DateTime? FromDate { get; init; }

    public DateTime? ToDate { get; init; }

    public string? Weather { get; init; }
}
