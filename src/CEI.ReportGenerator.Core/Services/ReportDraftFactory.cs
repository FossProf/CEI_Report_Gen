using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportDraftFactory
{
    public static InspectionReport CreateBlank(Project project)
    {
        var nextAuthoritativeNumber = ProjectStore.SynchronizeNextReportNumber(project);
        return new InspectionReport
        {
            Number = ReportStore.GetFirstAvailableReportNumber(project, nextAuthoritativeNumber),
            Status = ReportStatus.Draft,
            Date = DateTime.Today,
            CreatedUtc = DateTime.UtcNow,
            Photos = new List<Photo>(),
            OutputFileName = string.Empty
        };
    }

    public static InspectionReport CreateFromExisting(Project project, InspectionReport source)
    {
        var suggestedNumber = ReportStore.GetFirstAvailableReportNumber(project, Math.Max(1, source.Number + 1));
        return new InspectionReport
        {
            Number = suggestedNumber,
            Status = ReportStatus.Draft,
            Date = DateTime.Today,
            CreatedUtc = DateTime.UtcNow,
            Locations = source.Locations,
            Inspectors = source.Inspectors,
            PersonnelOnSite = source.PersonnelOnSite,
            DescriptionOfWork = source.DescriptionOfWork,
            DrawingsReviewed = source.DrawingsReviewed,
            NewDiscrepancies = source.NewDiscrepancies,
            PreviousDiscrepancies = source.PreviousDiscrepancies,
            Photos = new List<Photo>(),
            OutputFileName = string.Empty
        };
    }
}
