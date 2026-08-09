using System.Text.RegularExpressions;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using CEI.ReportImporter.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CEI.ReportImporter.Core.Services;

public sealed class HistoricalDocumentParser : IHistoricalReportParser
{
    private static readonly string[] SectionOrder =
    {
        "Location(s) Inspected",
        "Cornerstone Inspector(s)",
        "Personnel On Site",
        "Description and Location(s) of Work Inspected",
        "Drawing Sheets and Sections Related to This Work",
        "General Observations / Remarks",
        "Discrepancies and Direction Given",
        "Observations on Correction of Discrepancies Noted in Previous Inspections"
    };

    private static readonly Dictionary<string, string> SectionFieldMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Location(s) Inspected"] = "Locations",
        ["Cornerstone Inspector(s)"] = "Inspectors",
        ["Personnel On Site"] = "PersonnelOnSite",
        ["Description and Location(s) of Work Inspected"] = "DescriptionOfWork",
        ["Drawing Sheets and Sections Related to This Work"] = "DrawingsReviewed",
        ["General Observations / Remarks"] = "Observations",
        ["Discrepancies and Direction Given"] = "NewDiscrepancies",
        ["Observations on Correction of Discrepancies Noted in Previous Inspections"] = "PreviousDiscrepancies"
    };

    private static readonly Dictionary<string, string> NormalizedSectionHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        [NormalizeLabel("Location(s) Inspected")] = "Location(s) Inspected",
        [NormalizeLabel("Cornerstone Inspector(s)")] = "Cornerstone Inspector(s)",
        [NormalizeLabel("Personnel On Site")] = "Personnel On Site",
        [NormalizeLabel("Description and Location(s) of Work Inspected")] = "Description and Location(s) of Work Inspected",
        [NormalizeLabel("Drawing Sheets and Sections Related to This Work")] = "Drawing Sheets and Sections Related to This Work",
        [NormalizeLabel("General Observations / Remarks")] = "General Observations / Remarks",
        [NormalizeLabel("General Observations/Remarks")] = "General Observations / Remarks",
        [NormalizeLabel("Discrepancies and Direction Given")] = "Discrepancies and Direction Given",
        [NormalizeLabel("Observations on Correction of Discrepancies Noted in Previous Inspections")] = "Observations on Correction of Discrepancies Noted in Previous Inspections",
        [NormalizeLabel("Observations/Remarks on Correction of Discrepancies Noted in Previous Inspections")] = "Observations on Correction of Discrepancies Noted in Previous Inspections"
    };

    private static readonly Regex ReportNumberRegex = new(
        @"Special\s+Inspection\s+Report\s*#\s*(?<number>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string ProfileName => HistoricalReportImportService.DefaultParserProfile;

    public static HistoricalReportParseResult Parse(string filePath)
        => ParseCore(filePath);

    HistoricalReportParseResult IHistoricalReportParser.Parse(string documentPath)
        => ParseCore(documentPath);

    private static HistoricalReportParseResult ParseCore(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var fileName = Path.GetFileName(filePath);
        var filenameInfo = HistoricalFilenameParser.Parse(filePath);
        var warnings = new List<string>(filenameInfo.Warnings);

        try
        {
            using var document = WordprocessingDocument.Open(filePath, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                return Failed(filePath, "The document body could not be read.");
            }

            var tableValues = ExtractTableValues(body);
            var sectionValues = ExtractSectionValues(body);
            var allParagraphs = body.Descendants<Paragraph>()
                .Select(p => NormalizeWhitespace(p.InnerText))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            var reportNumberCandidates = new HashSet<int>();
            foreach (var paragraph in allParagraphs)
            {
                foreach (Match match in ReportNumberRegex.Matches(paragraph))
                {
                    if (int.TryParse(match.Groups["number"].Value, out var number))
                    {
                        reportNumberCandidates.Add(number);
                    }
                }
            }

            var documentReportNumber = reportNumberCandidates.Count == 1
                ? reportNumberCandidates.Single()
                : (int?)null;
            if (reportNumberCandidates.Count > 1)
            {
                warnings.Add("Multiple report numbers were detected in the document.");
            }

            var documentDate = ParseDate(FindValue(tableValues, "Inspection Date"));
            var projectName = FindValue(tableValues, "Project Name");
            var temperature = FindValue(tableValues, "Temperature");
            var weather = FindValue(tableValues, "Weather");
            var locations = GetValue("Locations", sectionValues, tableValues);
            var inspectors = GetValue("Inspectors", sectionValues, tableValues);
            var personnel = GetValue("PersonnelOnSite", sectionValues, tableValues);
            var description = GetValue("DescriptionOfWork", sectionValues, tableValues);
            var drawings = GetValue("DrawingsReviewed", sectionValues, tableValues);
            var observations = GetValue("Observations", sectionValues, tableValues);
            var newDiscrepancies = GetValue("NewDiscrepancies", sectionValues, tableValues);
            var previousDiscrepancies = GetValue("PreviousDiscrepancies", sectionValues, tableValues);

            if (string.IsNullOrWhiteSpace(weather))
            {
                warnings.Add("Missing weather.");
            }

            if (string.IsNullOrWhiteSpace(observations))
            {
                warnings.Add("Missing observations.");
            }

            if (documentReportNumber is not null && filenameInfo.ReportNumber is not null && documentReportNumber != filenameInfo.ReportNumber)
            {
                warnings.Add($"Filename/document mismatch for report number: filename #{filenameInfo.ReportNumber}, document #{documentReportNumber}.");
            }

            if (documentDate is not null && filenameInfo.Date is not null && documentDate.Value.Date != filenameInfo.Date.Value.Date)
            {
                warnings.Add($"Filename/document mismatch for inspection date: filename {filenameInfo.Date:yyyy-MM-dd}, document {documentDate:yyyy-MM-dd}.");
            }

            var resolvedReportNumber = documentReportNumber ?? filenameInfo.ReportNumber;
            var resolvedDate = documentDate ?? filenameInfo.Date;
            var resolvedProjectName = !string.IsNullOrWhiteSpace(projectName) ? projectName : filenameInfo.ProjectName;

            if (resolvedReportNumber is null || resolvedDate is null)
            {
                return Failed(filePath, "The document does not contain the minimum CEI report identity fields.", warnings);
            }

            var fieldConfidence = BuildFieldConfidence(
                documentReportNumber,
                filenameInfo.ReportNumber,
                documentDate,
                filenameInfo.Date,
                projectName,
                filenameInfo.ProjectName,
                temperature,
                weather,
                locations,
                inspectors,
                personnel,
                description,
                drawings,
                observations,
                newDiscrepancies,
                previousDiscrepancies);
            var overallConfidence = BuildOverallConfidence(fieldConfidence, warnings);

            var request = new HistoricalReportImportRequest
            {
                Number = resolvedReportNumber.Value,
                Date = resolvedDate.Value,
                Temperature = temperature,
                Weather = weather,
                Locations = locations,
                Inspectors = inspectors,
                PersonnelOnSite = personnel,
                DescriptionOfWork = description,
                DrawingsReviewed = drawings,
                Observations = observations,
                NewDiscrepancies = newDiscrepancies,
                PreviousDiscrepancies = previousDiscrepancies,
                SourceDocumentPath = filePath,
                ParserProfile = HistoricalReportImportService.DefaultParserProfile,
                Warnings = warnings
            };

            var status = warnings.Count == 0
                ? HistoricalReportParseStatus.Parsed
                : HistoricalReportParseStatus.ParsedWithWarnings;

            return new HistoricalReportParseResult
            {
                Status = status,
                FilePath = filePath,
                FileName = fileName,
                ParserProfile = HistoricalReportImportService.DefaultParserProfile,
                OverallConfidence = overallConfidence,
                FieldConfidence = fieldConfidence,
                Request = request,
                ProjectName = resolvedProjectName,
                ReportNumber = resolvedReportNumber,
                Date = resolvedDate,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return Failed(filePath, ex.Message);
        }
    }

    private static HistoricalReportParseResult Failed(string filePath, string message, IEnumerable<string>? warnings = null)
        => new()
        {
            Status = HistoricalReportParseStatus.Failed,
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            ParserProfile = HistoricalReportImportService.DefaultParserProfile,
            OverallConfidence = HistoricalConfidenceLevel.Low,
            FieldConfidence = new HistoricalFieldConfidence(),
            Warnings = warnings is null ? Array.Empty<string>() : warnings.ToList(),
            FailureMessage = message
        };

    private static Dictionary<string, string> ExtractTableValues(Body body)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in body.Descendants<TableRow>())
        {
            var cells = row.Elements<TableCell>()
                .Select(cell => NormalizeWhitespace(cell.InnerText))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (cells.Count < 2)
            {
                continue;
            }

            for (var i = 0; i + 1 < cells.Count; i += 2)
            {
                if (!TryMapLabel(cells[i], out var mapped))
                {
                    continue;
                }

                values[mapped] = cells[i + 1];
            }
        }

        return values;
    }

    private static Dictionary<string, string> ExtractSectionValues(Body body)
    {
        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => NormalizeWhitespace(p.InnerText))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (!TryMatchSectionHeading(paragraphs[i], out var heading, out var inlineValue))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(inlineValue))
            {
                sections[SectionFieldMap[heading]] = inlineValue;
                continue;
            }

            var collected = new List<string>();
            for (var j = i + 1; j < paragraphs.Count; j++)
            {
                if (TryMatchSectionHeading(paragraphs[j], out _, out _))
                {
                    break;
                }

                if (Regex.IsMatch(paragraphs[j], @"^Photo\s+\d+", RegexOptions.IgnoreCase))
                {
                    break;
                }

                if (LooksLikeSignatureBlock(paragraphs[j]))
                {
                    break;
                }

                if (j + 1 < paragraphs.Count && LooksLikeSignatureBlock(paragraphs[j + 1]))
                {
                    break;
                }

                collected.Add(paragraphs[j]);
            }

            sections[SectionFieldMap[heading]] = string.Join(Environment.NewLine, collected).Trim();
        }

        return sections;
    }

    private static bool TryMapLabel(string value, out string mapped)
    {
        var normalized = NormalizeLabel(value);
        mapped = normalized switch
        {
            "project name" => "Project Name",
            "inspection date" => "Inspection Date",
            "temperature f" => "Temperature",
            "temperature" => "Temperature",
            "weather" => "Weather",
            "location s" => "Locations",
            "locations inspected" => "Locations",
            "location inspected" => "Locations",
            "cornerstone inspector s" => "Inspectors",
            "cornerstone inspectors" => "Inspectors",
            "cornerstone inspector" => "Inspectors",
            "personnel on site" => "PersonnelOnSite",
            "description and locations of work inspected" => "DescriptionOfWork",
            "description and location of work inspected" => "DescriptionOfWork",
            "drawing sheets and sections related to this work" => "DrawingsReviewed",
            "general observations remarks" => "Observations",
            "discrepancies and direction given" => "NewDiscrepancies",
            "observations on correction of discrepancies noted in previous inspections" => "PreviousDiscrepancies",
            _ => string.Empty
        };

        return mapped.Length > 0;
    }

    private static bool TryMatchSectionHeading(string paragraph, out string heading, out string inlineValue)
    {
        heading = string.Empty;
        inlineValue = string.Empty;

        var headingText = paragraph;
        var colonIndex = paragraph.IndexOf(':');
        if (colonIndex >= 0)
        {
            headingText = paragraph[..colonIndex];
            inlineValue = paragraph[(colonIndex + 1)..].Trim();
        }

        var normalizedHeading = NormalizeLabel(headingText);
        if (NormalizedSectionHeadings.TryGetValue(normalizedHeading, out var canonicalHeading))
        {
            heading = canonicalHeading;
            return true;
        }

        return false;
    }

    private static string GetValue(string field, IReadOnlyDictionary<string, string> sections, IReadOnlyDictionary<string, string> tables)
        => sections.TryGetValue(field, out var sectionValue) && !string.IsNullOrWhiteSpace(sectionValue)
            ? sectionValue
            : tables.TryGetValue(field, out var tableValue)
                ? tableValue
                : string.Empty;

    private static string FindValue(IReadOnlyDictionary<string, string> tables, string field)
        => tables.TryGetValue(field, out var value) ? value : string.Empty;

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value;
        var semicolonIndex = cleaned.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            cleaned = cleaned[..semicolonIndex];
        }

        return HistoricalFilenameParser.TryParseDate(cleaned.Trim(), out var parsed)
            ? parsed
            : null;
    }

    private static HistoricalFieldConfidence BuildFieldConfidence(
        int? documentReportNumber,
        int? filenameReportNumber,
        DateTime? documentDate,
        DateTime? filenameDate,
        string documentProjectName,
        string filenameProjectName,
        string temperature,
        string weather,
        string locations,
        string inspectors,
        string personnel,
        string description,
        string drawings,
        string observations,
        string newDiscrepancies,
        string previousDiscrepancies)
        => new()
        {
            ReportNumber = CompareIdentity(documentReportNumber.HasValue, filenameReportNumber.HasValue, documentReportNumber == filenameReportNumber),
            Date = CompareIdentity(documentDate.HasValue, filenameDate.HasValue, documentDate?.Date == filenameDate?.Date),
            ProjectName = CompareIdentity(!string.IsNullOrWhiteSpace(documentProjectName), !string.IsNullOrWhiteSpace(filenameProjectName),
                string.Equals(documentProjectName, filenameProjectName, StringComparison.OrdinalIgnoreCase)),
            Temperature = ConfidenceForOptional(temperature),
            Weather = ConfidenceForOptional(weather),
            Locations = ConfidenceForRequired(locations),
            Inspectors = ConfidenceForRequired(inspectors),
            PersonnelOnSite = ConfidenceForRequired(personnel),
            DescriptionOfWork = ConfidenceForRequired(description),
            DrawingsReviewed = ConfidenceForRequired(drawings),
            Observations = ConfidenceForRequired(observations),
            NewDiscrepancies = ConfidenceForOptional(newDiscrepancies),
            PreviousDiscrepancies = ConfidenceForOptional(previousDiscrepancies)
        };

    private static HistoricalConfidenceLevel CompareIdentity(bool hasDocumentValue, bool hasFilenameValue, bool valuesMatch)
    {
        if (hasDocumentValue && hasFilenameValue)
        {
            return valuesMatch ? HistoricalConfidenceLevel.High : HistoricalConfidenceLevel.Medium;
        }

        if (hasDocumentValue || hasFilenameValue)
        {
            return HistoricalConfidenceLevel.Medium;
        }

        return HistoricalConfidenceLevel.Low;
    }

    private static HistoricalConfidenceLevel ConfidenceForRequired(string value)
        => string.IsNullOrWhiteSpace(value) ? HistoricalConfidenceLevel.Low : HistoricalConfidenceLevel.High;

    private static HistoricalConfidenceLevel ConfidenceForOptional(string value)
        => string.IsNullOrWhiteSpace(value) ? HistoricalConfidenceLevel.Medium : HistoricalConfidenceLevel.High;

    private static HistoricalConfidenceLevel BuildOverallConfidence(HistoricalFieldConfidence confidence, IReadOnlyCollection<string> warnings)
    {
        if (confidence.ReportNumber == HistoricalConfidenceLevel.Low || confidence.Date == HistoricalConfidenceLevel.Low)
        {
            return HistoricalConfidenceLevel.Low;
        }

        if (warnings.Any(w => w.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
            || confidence.ProjectName == HistoricalConfidenceLevel.Medium)
        {
            return HistoricalConfidenceLevel.Medium;
        }

        return HistoricalConfidenceLevel.High;
    }

    private static string NormalizeLabel(string value)
    {
        var cleaned = value
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("°", string.Empty, StringComparison.Ordinal)
            .Replace("(", " ", StringComparison.Ordinal)
            .Replace(")", " ", StringComparison.Ordinal)
            .Replace("/", " ", StringComparison.Ordinal);
        return NormalizeWhitespace(cleaned).ToLowerInvariant();
    }

    private static bool LooksLikeSignatureBlock(string paragraph)
        => paragraph.Contains("Special Inspector", StringComparison.OrdinalIgnoreCase)
            || paragraph.Contains("Project Manager", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
