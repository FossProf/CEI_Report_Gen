using System.Text;
using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ReportSearchService
{
    public static IReadOnlyList<InspectionReport> Filter(
        IEnumerable<InspectionReport> reports,
        ReportSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(criteria);

        var terms = SplitSearchTerms(criteria.SearchText);

        return reports
            .Where(report => MatchesReport(report, criteria, terms))
            .ToList();
    }

    public static bool TryValidateCriteria(ReportSearchCriteria criteria, out string? message)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.FromDate.HasValue && criteria.ToDate.HasValue && criteria.FromDate.Value.Date > criteria.ToDate.Value.Date)
        {
            message = "From date must be on or before To date.";
            return false;
        }

        message = null;
        return true;
    }

    private static bool MatchesReport(InspectionReport report, ReportSearchCriteria criteria, IReadOnlyList<string> terms)
    {
        if (criteria.Status.HasValue && report.Status != criteria.Status.Value)
        {
            return false;
        }

        if (criteria.FromDate.HasValue && report.Date.Date < criteria.FromDate.Value.Date)
        {
            return false;
        }

        if (criteria.ToDate.HasValue && report.Date.Date > criteria.ToDate.Value.Date)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(criteria.Weather)
            && !string.Equals(report.Weather, criteria.Weather, StringComparison.Ordinal))
        {
            return false;
        }

        if (terms.Count == 0)
        {
            return true;
        }

        var searchableText = BuildSearchableText(report);
        return terms.All(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> SplitSearchTerms(string? searchText)
        => string.IsNullOrWhiteSpace(searchText)
            ? Array.Empty<string>()
            : searchText
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

    private static string BuildSearchableText(InspectionReport report)
    {
        var builder = new StringBuilder();

        Append(builder, report.Number.ToString());
        Append(builder, ProjectLayout.FormatReportNumber(report.Number));
        Append(builder, "#" + report.Number);
        Append(builder, "Report " + report.Number);
        Append(builder, "Report #" + report.Number);
        Append(builder, report.Date.ToString("yyyy-MM-dd"));
        Append(builder, report.Date.ToString("MMMM d, yyyy"));
        Append(builder, report.Temperature);
        Append(builder, report.Weather);
        Append(builder, report.Locations);
        Append(builder, report.Inspectors);
        Append(builder, report.PersonnelOnSite);
        Append(builder, report.DescriptionOfWork);
        Append(builder, report.DrawingsReviewed);
        Append(builder, report.Observations);
        Append(builder, report.NewDiscrepancies);
        Append(builder, report.PreviousDiscrepancies);
        Append(builder, report.OutputFileName);

        foreach (var photo in report.Photos)
        {
            Append(builder, photo.Caption);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(NormalizeWhitespace(value));
    }

    private static string NormalizeWhitespace(string value)
        => string.Join(" ", value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
