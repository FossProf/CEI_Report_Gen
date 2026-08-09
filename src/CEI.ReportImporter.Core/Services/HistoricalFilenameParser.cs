using System.Text.RegularExpressions;
using CEI.ReportImporter.Core.Models;

namespace CEI.ReportImporter.Core.Services;

public static class HistoricalFilenameParser
{
    private static readonly Regex DatePrefixRegex = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReportNumberRegex = new(
        @"(?:^|\s)(?:SPIN\s+)?Report\s*#(?<number>\d+)(?:\D|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static HistoricalFilenameParseResult Parse(string filePathOrName)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePathOrName);
        var warnings = new List<string>();

        DateTime? date = null;
        var dateMatch = DatePrefixRegex.Match(fileName);
        if (dateMatch.Success && TryParseDate(dateMatch.Groups["date"].Value, out var parsedDate))
        {
            date = parsedDate;
        }

        int? reportNumber = null;
        var reportMatch = ReportNumberRegex.Match(fileName);
        if (reportMatch.Success && int.TryParse(reportMatch.Groups["number"].Value, out var parsedNumber))
        {
            reportNumber = parsedNumber;
        }

        var projectName = string.Empty;
        if (dateMatch.Success && reportMatch.Success && reportMatch.Index > dateMatch.Index + dateMatch.Length)
        {
            projectName = fileName[(dateMatch.Index + dateMatch.Length)..reportMatch.Index].Trim();
            if (projectName.EndsWith("SPIN", StringComparison.OrdinalIgnoreCase))
            {
                projectName = projectName[..^4].TrimEnd();
            }
        }

        if (date is null)
        {
            warnings.Add("Inspection date could not be determined from the filename.");
        }

        if (reportNumber is null)
        {
            warnings.Add("Report number could not be determined from the filename.");
        }

        return new HistoricalFilenameParseResult
        {
            FileName = Path.GetFileName(filePathOrName),
            Date = date,
            ReportNumber = reportNumber,
            ProjectName = projectName,
            Warnings = warnings
        };
    }

    internal static bool TryParseDate(string value, out DateTime date)
        => DateTime.TryParseExact(
            value,
            new[] { "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "MMMM d, yyyy", "MMM d, yyyy" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out date);
}
