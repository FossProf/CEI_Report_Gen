using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportDraftFactory
{
    public static InspectionReport CreateBlank(Project project)
    {
        return new InspectionReport
        {
            Number = ProjectStore.SynchronizeNextReportNumber(project),
            Status = ReportStatus.Draft,
            Date = DateTime.Today,
            CreatedUtc = DateTime.UtcNow,
            Photos = new List<Photo>(),
            OutputFileName = string.Empty
        };
    }

    public static InspectionReport CreateFromExisting(Project project, InspectionReport source)
    {
        return new InspectionReport
        {
            Number = ProjectStore.SynchronizeNextReportNumber(project),
            Status = ReportStatus.Draft,
            Date = DateTime.Today,
            CreatedUtc = DateTime.UtcNow,
            Locations = source.Locations,
            Inspectors = source.Inspectors,
            PersonnelOnSite = source.PersonnelOnSite,
            DescriptionOfWork = source.DescriptionOfWork,
            DrawingsReviewed = source.DrawingsReviewed,
            Photos = new List<Photo>(),
            OutputFileName = string.Empty
        };
    }
}
