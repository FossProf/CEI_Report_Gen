using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.App;

public static class ReportMatchSnippetBuilder
{
    private const int TargetSnippetLength = 100;

    public static ReportSearchResult? Build(InspectionReport report, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(report);

        var terms = SplitSearchTerms(searchText);
        if (terms.Count == 0)
        {
            return null;
        }

        var bestCandidate = EnumerateCandidates(report)
            .Select((candidate, index) => new RankedCandidate(candidate, index, CountMatchedTerms(candidate.Text, terms)))
            .Where(candidate => candidate.MatchedTerms > 0)
            .OrderByDescending(candidate => candidate.MatchedTerms)
            .ThenBy(candidate => candidate.Priority)
            .FirstOrDefault();

        if (bestCandidate is null)
        {
            return null;
        }

        var normalizedText = NormalizeWhitespace(bestCandidate.Candidate.Text);
        return new ReportSearchResult
        {
            Report = report,
            MatchField = bestCandidate.Candidate.FieldName,
            MatchSnippet = BuildSnippet(normalizedText, terms),
            MatchFullText = normalizedText
        };
    }

    private static IEnumerable<SearchFieldCandidate> EnumerateCandidates(InspectionReport report)
    {
        yield return new SearchFieldCandidate("Observations", report.Observations);
        yield return new SearchFieldCandidate("New Discrepancies", report.NewDiscrepancies);
        yield return new SearchFieldCandidate("Previous Discrepancies", report.PreviousDiscrepancies);
        yield return new SearchFieldCandidate("Description of Work", report.DescriptionOfWork);
        yield return new SearchFieldCandidate("Location", report.Locations);
        yield return new SearchFieldCandidate("Drawings Reviewed", report.DrawingsReviewed);
        yield return new SearchFieldCandidate("Personnel On Site", report.PersonnelOnSite);
        yield return new SearchFieldCandidate("Inspector", report.Inspectors);
        yield return new SearchFieldCandidate("Weather", report.Weather);
        yield return new SearchFieldCandidate("Temperature", report.Temperature);

        foreach (var photo in report.Photos)
        {
            yield return new SearchFieldCandidate("Photo Caption", photo.Caption);
        }

        yield return new SearchFieldCandidate("File Name", report.OutputFileName);
        yield return new SearchFieldCandidate("Report Number", BuildReportNumberText(report.Number));
        yield return new SearchFieldCandidate("Date", report.Date.ToString("yyyy-MM-dd") + " " + report.Date.ToString("MMMM d, yyyy"));
    }

    private static string BuildReportNumberText(int reportNumber)
        => string.Join(
            " ",
            reportNumber.ToString(),
            ProjectLayout.FormatReportNumber(reportNumber),
            "#" + reportNumber,
            "Report " + reportNumber,
            "Report #" + reportNumber);

    private static IReadOnlyList<string> SplitSearchTerms(string? searchText)
        => string.IsNullOrWhiteSpace(searchText)
            ? Array.Empty<string>()
            : searchText
                .Trim()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

    private static int CountMatchedTerms(string? sourceText, IReadOnlyList<string> terms)
    {
        var normalized = NormalizeWhitespace(sourceText);
        if (string.IsNullOrEmpty(normalized))
        {
            return 0;
        }

        return terms.Count(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSnippet(string text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= TargetSnippetLength)
        {
            return text;
        }

        var matchIndex = FindFirstMatchIndex(text, terms);
        if (matchIndex < 0)
        {
            return TrimSnippet(text, 0, TargetSnippetLength);
        }

        var start = Math.Max(0, matchIndex - (TargetSnippetLength / 2));
        var length = Math.Min(TargetSnippetLength, text.Length - start);

        if (start > 0)
        {
            while (start < text.Length && start > 0 && !char.IsWhiteSpace(text[start - 1]) && !char.IsWhiteSpace(text[start]))
            {
                start++;
                length = Math.Min(TargetSnippetLength, text.Length - start);
            }
        }

        var end = Math.Min(text.Length, start + length);
        while (end < text.Length && end > start && !char.IsWhiteSpace(text[end - 1]) && !char.IsWhiteSpace(text[end]))
        {
            end--;
        }

        return TrimSnippet(text, start, Math.Max(1, end - start));
    }

    private static int FindFirstMatchIndex(string text, IReadOnlyList<string> terms)
    {
        var matches = terms
            .Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .ToList();
        return matches.Count == 0 ? -1 : matches.Min();
    }

    private static string TrimSnippet(string text, int start, int length)
    {
        var safeStart = Math.Max(0, Math.Min(start, text.Length));
        var safeLength = Math.Max(0, Math.Min(length, text.Length - safeStart));
        var snippet = text.Substring(safeStart, safeLength).Trim();

        if (safeStart > 0)
        {
            snippet = "..." + snippet;
        }

        if (safeStart + safeLength < text.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static string NormalizeWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed record SearchFieldCandidate(string FieldName, string? Text);

    private sealed record RankedCandidate(SearchFieldCandidate Candidate, int Priority, int MatchedTerms);
}
