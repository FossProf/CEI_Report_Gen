using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CEI.ReportGenerator.Core.Services;

public static class TemplateValidator
{
    public static readonly IReadOnlyList<string> RequiredTextPlaceholders =
    [
        "{project.name}",
        "{project.num}",
        "{project.owner}",
        "{project.contract}",
        "{project.general}",
        "{project.report.num}",
        "{project.report.date}",
        "{project.report.temp}",
        "{project.report.weather}",
        "{project.report.location}",
        "{project.report.inspector}",
        "{project.report.personnel}",
        "{project.report.description}",
        "{project.report.drawing}",
        "{project.report.observations}",
        "{project.report.new_discrepancies}",
        "{project.report.old_discrepancies}"
    ];

    public static readonly IReadOnlyList<string> RequiredSignatureTags =
    [
        "inspection.signature.inspector",
        "inspection.signature.projectManager"
    ];

    public static List<string> ValidateTemplate(string templatePath)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            errors.Add("No approved template selected.");
            return errors;
        }

        if (!File.Exists(templatePath))
        {
            errors.Add($"Approved template file does not exist: {Path.GetFileName(templatePath)}");
            return errors;
        }

        if (!string.Equals(Path.GetExtension(templatePath), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Approved template must be a Word .docx file.");
            return errors;
        }

        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(templatePath, false);
        }
        catch (Exception ex)
        {
            errors.Add($"Approved template could not be opened as a Word document: {ex.Message}");
            return errors;
        }

        using (document)
        {
            var mainPart = document.MainDocumentPart;
            if (mainPart is null || mainPart.Document?.Body is null)
            {
                errors.Add("Approved template has no main document body.");
                return errors;
            }

            var text = CollectBodyText(mainPart.Document.Body);
            foreach (var placeholder in RequiredTextPlaceholders)
            {
                if (!text.Contains(placeholder, StringComparison.Ordinal))
                {
                    errors.Add($"Template is missing placeholder {placeholder}.");
                }
            }

            foreach (var legacy in new[] { "{project.report.discrepancies}", "{project.report.previousDiscrepancy}" })
            {
                if (text.Contains(legacy, StringComparison.Ordinal))
                {
                    errors.Add($"Template contains misspelled legacy placeholder {legacy}.");
                }
            }

            foreach (var tag in RequiredSignatureTags)
            {
                if (!ContainsContentControlTag(mainPart.Document.Body, tag))
                {
                    errors.Add($"Template is missing signature area {tag}.");
                }
            }

            var photoPlaces = PhotoTable.FindPhotoPlaces(mainPart.Document.Body);
            if (photoPlaces.Count == 0)
            {
                errors.Add("Template is missing the photo table.");
            }
            else
            {
                foreach (var place in photoPlaces)
                {
                    if (place.Slots.Count < 1 || place.Slots.Count > 3)
                    {
                        errors.Add($"Photo table contains {place.Slots} photo slot(s); expected 1 to 3.");
                    }

                    if (!place.IsRemovable)
                    {
                        errors.Add("Photo table could not be located for removal when a report has no photos.");
                    }

                    if (!place.CanRepeat)
                    {
                        errors.Add("Photo table cannot be repeated for additional photos.");
                    }
                }
            }
        }

        return errors;
    }

    private static string CollectBodyText(Body body)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            foreach (var text in paragraph.Descendants<Text>())
            {
                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }

    private static bool ContainsContentControlTag(Body body, string tag)
    {
        return body.Descendants<SdtBlock>().Any(block =>
            block.SdtProperties?.GetFirstChild<SdtAlias>()?.Val is not null &&
            block.SdtProperties.GetFirstChild<SdtAlias>()!.Val!.Value == tag);
    }
}
