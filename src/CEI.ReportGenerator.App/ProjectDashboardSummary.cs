using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App;

public sealed class ProjectDashboardSummary
{
    public required string ProjectName { get; init; }

    public required string ProjectNumber { get; init; }

    public required string Owner { get; init; }

    public required string ContractManager { get; init; }

    public required string GeneralContractor { get; init; }

    public required int NextReportNumber { get; init; }

    public required int TotalReports { get; init; }

    public required int FinalReports { get; init; }

    public required int DraftReports { get; init; }

    public required int LoadIssueCount { get; init; }

    public required ProjectReadiness Readiness { get; init; }

    public string NextReportNumberText => ProjectLayout.FormatReportNumber(NextReportNumber);

    public bool HasReports => TotalReports > 0;

    public bool HasLoadIssues => LoadIssueCount > 0;

    public string LoadIssueMessage => LoadIssueCount switch
    {
        0 => string.Empty,
        1 => "1 report could not be loaded.",
        _ => $"{LoadIssueCount} reports could not be loaded."
    };

    public bool AttentionRequired => !Readiness.IsReady || HasLoadIssues;

    public string StatusText => AttentionRequired ? "Attention Required" : "Ready";
}

public static class ProjectDashboardSummaryBuilder
{
    public static ProjectDashboardSummary Build(Project project, ReportStore.ReportLoadResult loadResult)
    {
        var totalReports = loadResult.Reports.Count;
        var finalReports = loadResult.Reports.Count(r => r.Status == ReportStatus.Final);
        var draftReports = totalReports - finalReports;

        return new ProjectDashboardSummary
        {
            ProjectName = project.Name,
            ProjectNumber = project.Number,
            Owner = project.Owner,
            ContractManager = project.ContractManager,
            GeneralContractor = project.GeneralContractor,
            NextReportNumber = ReportStore.GetNextReportNumber(project),
            TotalReports = totalReports,
            FinalReports = finalReports,
            DraftReports = draftReports,
            LoadIssueCount = loadResult.Issues.Count,
            Readiness = ProjectReadinessEvaluator.Evaluate(project)
        };
    }
}
