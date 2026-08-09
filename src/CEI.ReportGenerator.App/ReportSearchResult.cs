using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.App;

public sealed class ReportSearchResult
{
    public required InspectionReport Report { get; init; }

    public required string MatchField { get; init; }

    public required string MatchSnippet { get; init; }

    public required string MatchFullText { get; init; }

    public string MatchDisplay => $"{MatchField}: \"{MatchSnippet}\"";
}
