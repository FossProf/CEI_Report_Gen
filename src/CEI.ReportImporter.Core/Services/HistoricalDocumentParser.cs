using System.Text.RegularExpressions;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using CEI.ReportImporter.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CEI.ReportImporter.Core.Services;

public sealed class HistoricalDocumentParser : IHistoricalReportParser
{
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

            var reportNumberCandidates = new List<DetectedFieldValue<int>>();
            foreach (var paragraph in allParagraphs)
            {
                foreach (Match match in ReportNumberRegex.Matches(paragraph))
                {
                    if (int.TryParse(match.Groups["number"].Value, out var number))
                    {
                        reportNumberCandidates.Add(new DetectedFieldValue<int>(number, "Document heading"));
                    }
                }
            }

            var distinctDocumentReportNumbers = reportNumberCandidates
                .Select(candidate => candidate.Value)
                .Distinct()
                .ToList();

            var documentReportNumber = distinctDocumentReportNumbers.Count == 1
                ? distinctDocumentReportNumbers[0]
                : (int?)null;
            if (distinctDocumentReportNumbers.Count > 1)
            {
                warnings.Add("Multiple report numbers were detected in the document.");
            }

            var documentDateValue = TryGetValue(tableValues, "Inspection Date");
            var documentProjectNameValue = TryGetValue(tableValues, "Project Name");
            var temperatureValue = TryGetValue(tableValues, "Temperature");
            var weatherValue = TryGetValue(tableValues, "Weather");
            var locationsValue = GetValue("Locations", sectionValues, tableValues);
            var inspectorsValue = GetValue("Inspectors", sectionValues, tableValues);
            var personnelValue = GetValue("PersonnelOnSite", sectionValues, tableValues);
            var descriptionValue = GetValue("DescriptionOfWork", sectionValues, tableValues);
            var drawingsValue = GetValue("DrawingsReviewed", sectionValues, tableValues);
            var observationsValue = GetValue("Observations", sectionValues, tableValues);
            var newDiscrepanciesValue = GetValue("NewDiscrepancies", sectionValues, tableValues);
            var previousDiscrepanciesValue = GetValue("PreviousDiscrepancies", sectionValues, tableValues);

            var documentDate = ParseDateValue(documentDateValue);
            var reportNumberExtraction = BuildReportNumberExtraction(filenameInfo.ReportNumber, documentReportNumber, reportNumberCandidates);
            var dateExtraction = BuildDateExtraction(filenameInfo.Date, documentDate, documentDateValue);
            var projectNameExtraction = BuildProjectNameExtraction(documentProjectNameValue, filenameInfo.ProjectName);

            if (string.IsNullOrWhiteSpace(weatherValue.Value))
            {
                warnings.Add("Missing weather.");
            }

            if (string.IsNullOrWhiteSpace(observationsValue.Value))
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

            var resolvedReportNumber = reportNumberExtraction.Value;
            var resolvedDate = dateExtraction.Value;
            var resolvedProjectName = !string.IsNullOrWhiteSpace(projectNameExtraction.Value)
                ? projectNameExtraction.Value
                : filenameInfo.ProjectName;

            if (resolvedReportNumber is null || resolvedDate is null)
            {
                return Failed(filePath, "The document does not contain the minimum CEI report identity fields.", warnings);
            }

            var fieldExtractions = new HistoricalFieldExtractions
            {
                ReportNumber = reportNumberExtraction,
                InspectionDate = dateExtraction,
                Temperature = BuildTextExtraction(temperatureValue, isRequired: false),
                Weather = BuildTextExtraction(weatherValue, isRequired: false),
                Locations = BuildTextExtraction(locationsValue, isRequired: true),
                Inspectors = BuildTextExtraction(inspectorsValue, isRequired: true),
                PersonnelOnSite = BuildTextExtraction(personnelValue, isRequired: true),
                DescriptionOfWork = BuildTextExtraction(descriptionValue, isRequired: true),
                DrawingsReviewed = BuildTextExtraction(drawingsValue, isRequired: true),
                Observations = BuildTextExtraction(observationsValue, isRequired: true),
                NewDiscrepancies = BuildTextExtraction(newDiscrepanciesValue, isRequired: false),
                PreviousDiscrepancies = BuildTextExtraction(previousDiscrepanciesValue, isRequired: false)
            };

            var fieldConfidence = BuildFieldConfidence(fieldExtractions, projectNameExtraction);
            var overallConfidence = BuildOverallConfidence(fieldConfidence, warnings);

            var request = new HistoricalReportImportRequest
            {
                Number = resolvedReportNumber.Value,
                Date = resolvedDate.Value,
                Temperature = temperatureValue.Value,
                Weather = weatherValue.Value,
                Locations = locationsValue.Value,
                Inspectors = inspectorsValue.Value,
                PersonnelOnSite = personnelValue.Value,
                DescriptionOfWork = descriptionValue.Value,
                DrawingsReviewed = drawingsValue.Value,
                Observations = observationsValue.Value,
                NewDiscrepancies = newDiscrepanciesValue.Value,
                PreviousDiscrepancies = previousDiscrepanciesValue.Value,
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
                FieldExtractions = fieldExtractions,
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
            FieldExtractions = new HistoricalFieldExtractions(),
            Warnings = warnings is null ? Array.Empty<string>() : warnings.ToList(),
            FailureMessage = message
        };

    private static Dictionary<string, SourcedText> ExtractTableValues(Body body)
    {
        var values = new Dictionary<string, SourcedText>(StringComparer.OrdinalIgnoreCase);

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

                values[mapped] = new SourcedText(cells[i + 1], $"Table field: {mapped}");
            }
        }

        return values;
    }

    private static Dictionary<string, SourcedText> ExtractSectionValues(Body body)
    {
        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => NormalizeWhitespace(p.InnerText))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        var sections = new Dictionary<string, SourcedText>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < paragraphs.Count; i++)
        {
            if (!TryMatchSectionHeading(paragraphs[i], out var heading, out var inlineValue))
            {
                continue;
            }

            var mappedField = SectionFieldMap[heading];
            var source = $"{heading} section";

            if (!string.IsNullOrWhiteSpace(inlineValue))
            {
                sections[mappedField] = new SourcedText(inlineValue, source);
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

            sections[mappedField] = new SourcedText(string.Join(Environment.NewLine, collected).Trim(), source);
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

    private static SourcedText GetValue(string field, IReadOnlyDictionary<string, SourcedText> sections, IReadOnlyDictionary<string, SourcedText> tables)
        => sections.TryGetValue(field, out var sectionValue) && !string.IsNullOrWhiteSpace(sectionValue.Value)
            ? sectionValue
            : tables.TryGetValue(field, out var tableValue)
                ? tableValue
                : SourcedText.Empty;

    private static SourcedText TryGetValue(IReadOnlyDictionary<string, SourcedText> tables, string field)
        => tables.TryGetValue(field, out var value) ? value : SourcedText.Empty;

    private static DateTime? ParseDateValue(SourcedText value)
    {
        if (string.IsNullOrWhiteSpace(value.Value))
        {
            return null;
        }

        var cleaned = value.Value;
        var semicolonIndex = cleaned.IndexOf(';');
        if (semicolonIndex >= 0)
        {
            cleaned = cleaned[..semicolonIndex];
        }

        return HistoricalFilenameParser.TryParseDate(cleaned.Trim(), out var parsed)
            ? parsed
            : null;
    }

    private static FieldExtraction<int?> BuildReportNumberExtraction(
        int? filenameReportNumber,
        int? documentReportNumber,
        IReadOnlyCollection<DetectedFieldValue<int>> documentCandidates)
    {
        var candidates = new List<DetectedFieldValue<int?>>();
        if (filenameReportNumber is not null)
        {
            candidates.Add(new DetectedFieldValue<int?>(filenameReportNumber.Value, "Filename"));
        }

        candidates.AddRange(documentCandidates.Select(candidate => new DetectedFieldValue<int?>(candidate.Value, candidate.Source)));

        if (documentReportNumber is not null && filenameReportNumber is not null)
        {
            if (documentReportNumber == filenameReportNumber)
            {
                return new FieldExtraction<int?>(
                    documentReportNumber,
                    ExtractionConfidence.High,
                    "Filename + document heading",
                    Array.Empty<string>(),
                    candidates);
            }

            return new FieldExtraction<int?>(
                documentReportNumber,
                ExtractionConfidence.Low,
                "Document heading",
                ["Filename and document report numbers do not match."],
                candidates);
        }

        if (documentReportNumber is not null)
        {
            return new FieldExtraction<int?>(
                documentReportNumber,
                ExtractionConfidence.High,
                "Document heading",
                Array.Empty<string>(),
                candidates);
        }

        if (filenameReportNumber is not null)
        {
            return new FieldExtraction<int?>(
                filenameReportNumber,
                ExtractionConfidence.Medium,
                "Filename",
                ["Report number was inferred from the filename."],
                candidates);
        }

        return new FieldExtraction<int?>(
            null,
            ExtractionConfidence.None,
            "Not found",
            ["Report number was not detected."],
            candidates);
    }

    private static FieldExtraction<DateTime?> BuildDateExtraction(
        DateTime? filenameDate,
        DateTime? documentDate,
        SourcedText documentDateValue)
    {
        var candidates = new List<DetectedFieldValue<DateTime?>>();
        if (filenameDate is not null)
        {
            candidates.Add(new DetectedFieldValue<DateTime?>(filenameDate.Value.Date, "Filename"));
        }

        if (documentDate is not null)
        {
            candidates.Add(new DetectedFieldValue<DateTime?>(documentDate.Value.Date, documentDateValue.Source));
        }

        if (documentDate is not null && filenameDate is not null)
        {
            if (documentDate.Value.Date == filenameDate.Value.Date)
            {
                return new FieldExtraction<DateTime?>(
                    documentDate.Value.Date,
                    ExtractionConfidence.High,
                    "Filename + inspection date field",
                    Array.Empty<string>(),
                    candidates);
            }

            return new FieldExtraction<DateTime?>(
                documentDate.Value.Date,
                ExtractionConfidence.Low,
                documentDateValue.Source,
                ["Filename and document inspection dates do not match."],
                candidates);
        }

        if (documentDate is not null)
        {
            return new FieldExtraction<DateTime?>(
                documentDate.Value.Date,
                ExtractionConfidence.High,
                documentDateValue.Source,
                Array.Empty<string>(),
                candidates);
        }

        if (filenameDate is not null)
        {
            return new FieldExtraction<DateTime?>(
                filenameDate.Value.Date,
                ExtractionConfidence.Medium,
                "Filename",
                ["Inspection date was inferred from the filename."],
                candidates);
        }

        return new FieldExtraction<DateTime?>(
            null,
            ExtractionConfidence.None,
            "Not found",
            ["Inspection date was not detected."],
            candidates);
    }

    private static FieldExtraction<string> BuildProjectNameExtraction(SourcedText documentProjectName, string filenameProjectName)
    {
        var candidates = new List<DetectedFieldValue<string>>();
        if (!string.IsNullOrWhiteSpace(filenameProjectName))
        {
            candidates.Add(new DetectedFieldValue<string>(filenameProjectName, "Filename"));
        }

        if (!string.IsNullOrWhiteSpace(documentProjectName.Value))
        {
            candidates.Add(new DetectedFieldValue<string>(documentProjectName.Value, documentProjectName.Source));
        }

        if (!string.IsNullOrWhiteSpace(documentProjectName.Value) && !string.IsNullOrWhiteSpace(filenameProjectName))
        {
            if (string.Equals(documentProjectName.Value, filenameProjectName, StringComparison.OrdinalIgnoreCase))
            {
                return new FieldExtraction<string>(
                    documentProjectName.Value,
                    ExtractionConfidence.High,
                    "Filename + project name field",
                    Array.Empty<string>(),
                    candidates);
            }

            return new FieldExtraction<string>(
                documentProjectName.Value,
                ExtractionConfidence.Medium,
                documentProjectName.Source,
                ["Filename and document project names do not match exactly."],
                candidates);
        }

        if (!string.IsNullOrWhiteSpace(documentProjectName.Value))
        {
            return new FieldExtraction<string>(
                documentProjectName.Value,
                ExtractionConfidence.High,
                documentProjectName.Source,
                Array.Empty<string>(),
                candidates);
        }

        if (!string.IsNullOrWhiteSpace(filenameProjectName))
        {
            return new FieldExtraction<string>(
                filenameProjectName,
                ExtractionConfidence.Medium,
                "Filename",
                ["Project name was inferred from the filename."],
                candidates);
        }

        return new FieldExtraction<string>(
            string.Empty,
            ExtractionConfidence.None,
            "Not found",
            ["Project name was not detected."],
            candidates);
    }

    private static FieldExtraction<string> BuildTextExtraction(SourcedText value, bool isRequired)
    {
        if (string.IsNullOrWhiteSpace(value.Value))
        {
            return new FieldExtraction<string>(
                string.Empty,
                ExtractionConfidence.None,
                "Not found",
                [isRequired ? "Required field was not detected." : "Optional field was not detected."],
                Array.Empty<DetectedFieldValue<string>>());
        }

        return new FieldExtraction<string>(
            value.Value,
            ExtractionConfidence.High,
            value.Source,
            Array.Empty<string>(),
            [new DetectedFieldValue<string>(value.Value, value.Source)]);
    }

    private static HistoricalFieldConfidence BuildFieldConfidence(
        HistoricalFieldExtractions fieldExtractions,
        FieldExtraction<string> projectNameExtraction)
        => new()
        {
            ReportNumber = ToLegacyConfidence(fieldExtractions.ReportNumber.Confidence, optionalWhenMissing: false),
            Date = ToLegacyConfidence(fieldExtractions.InspectionDate.Confidence, optionalWhenMissing: false),
            ProjectName = ToLegacyConfidence(projectNameExtraction.Confidence, optionalWhenMissing: false),
            Temperature = ToLegacyConfidence(fieldExtractions.Temperature.Confidence, optionalWhenMissing: true),
            Weather = ToLegacyConfidence(fieldExtractions.Weather.Confidence, optionalWhenMissing: true),
            Locations = ToLegacyConfidence(fieldExtractions.Locations.Confidence, optionalWhenMissing: false),
            Inspectors = ToLegacyConfidence(fieldExtractions.Inspectors.Confidence, optionalWhenMissing: false),
            PersonnelOnSite = ToLegacyConfidence(fieldExtractions.PersonnelOnSite.Confidence, optionalWhenMissing: false),
            DescriptionOfWork = ToLegacyConfidence(fieldExtractions.DescriptionOfWork.Confidence, optionalWhenMissing: false),
            DrawingsReviewed = ToLegacyConfidence(fieldExtractions.DrawingsReviewed.Confidence, optionalWhenMissing: false),
            Observations = ToLegacyConfidence(fieldExtractions.Observations.Confidence, optionalWhenMissing: false),
            NewDiscrepancies = ToLegacyConfidence(fieldExtractions.NewDiscrepancies.Confidence, optionalWhenMissing: true),
            PreviousDiscrepancies = ToLegacyConfidence(fieldExtractions.PreviousDiscrepancies.Confidence, optionalWhenMissing: true)
        };

    private static HistoricalConfidenceLevel ToLegacyConfidence(ExtractionConfidence confidence, bool optionalWhenMissing)
        => confidence switch
        {
            ExtractionConfidence.High => HistoricalConfidenceLevel.High,
            ExtractionConfidence.Medium => HistoricalConfidenceLevel.Medium,
            ExtractionConfidence.Low => HistoricalConfidenceLevel.Medium,
            _ => optionalWhenMissing ? HistoricalConfidenceLevel.Medium : HistoricalConfidenceLevel.Low
        };

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
            .Replace("Â°", string.Empty, StringComparison.Ordinal)
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

    private sealed record SourcedText(string Value, string Source)
    {
        public static SourcedText Empty { get; } = new(string.Empty, "Not found");
    }
}
