using System.Globalization;
using System.Text.RegularExpressions;
using CEI.ReportGenerator.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace CEI.ReportGenerator.Core.Services;

public enum GenerationStage
{
    ValidateProject,
    ValidateReport,
    ValidateTemplate,
    CopyTemplate,
    PopulateText,
    ProcessPhotos,
    InsertSignatures,
    SaveDocument,
    ValidateOutput
}

public sealed record GenerationResult(string OutputPath);

public sealed class GenerationException : Exception
{
    public GenerationException(IReadOnlyList<string> errors)
        : this(null, errors)
    {
    }

    public GenerationException(GenerationStage? stage, IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Stage = stage;
        Errors = errors;
    }

    public GenerationStage? Stage { get; }

    public IReadOnlyList<string> Errors { get; }
}

public static class TemplateFiller
{
    private const double DxaToEmu = 635.0;

    private const long EmuPerPixel = 9525; // 914400 EMU per inch at 96 DPI

    private static readonly string RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly (string Tag, string Role)[] SignatureAreas =
    [
        (TemplateValidator.RequiredSignatureTags[0], "Special Inspector"),
        (TemplateValidator.RequiredSignatureTags[1], "Project Manager")
    ];

    public static GenerationResult Generate(Project project, InspectionReport report, string outputPath)
    {
        var stage = GenerationStage.CopyTemplate;
        string? tempPath = null;
        try
        {
            if (File.Exists(outputPath))
            {
                throw new GenerationException(GenerationStage.CopyTemplate,
                    new[] { $"A report file already exists at {outputPath}." });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            tempPath = CreateTempPath(outputPath);
            File.Copy(project.TemplatePath, tempPath);

            stage = GenerationStage.PopulateText;
            using (var document = WordprocessingDocument.Open(tempPath, true))
            {
                var mainPart = document.MainDocumentPart
                    ?? throw new GenerationException(GenerationStage.CopyTemplate, new[] { "The template is not a valid Word document." });
                var body = mainPart.Document.Body
                    ?? throw new GenerationException(GenerationStage.CopyTemplate, new[] { "The template has no document body." });

                var values = BuildValueDictionary(project, report);
                FillTextPlaceholders(body, values);

                stage = GenerationStage.ProcessPhotos;
                FillPhotoSlots(mainPart, body, ResolvePhotos(project, report));

                stage = GenerationStage.InsertSignatures;
                ReplaceSignatures(mainPart, project);

                RemoveInstructionText(body);

                mainPart.Document.Save();
            }

            stage = GenerationStage.SaveDocument;
            File.Move(tempPath, outputPath);
            tempPath = null;

            stage = GenerationStage.ValidateOutput;
            ValidateOutput(outputPath);

            return new GenerationResult(outputPath);
        }
        catch (GenerationException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            throw new GenerationException(stage, new[] { $"Report generation failed during {stage}: {ex.Message}" });
        }
    }

    private static string CreateTempPath(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath)!;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp.docx");
    }

    private static void TryDelete(string? path)
    {
        if (path is not null)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private static void ValidateOutput(string outputPath)
    {
        using var document = WordprocessingDocument.Open(outputPath, false);
        var mainPart = document.MainDocumentPart
            ?? throw new GenerationException(GenerationStage.ValidateOutput, new[] { "The generated document has no main part." });
        var body = mainPart.Document.Body
            ?? throw new GenerationException(GenerationStage.ValidateOutput, new[] { "The generated document has no body." });

        var leftover = body.Descendants<Text>()
            .Select(t => t.Text)
            .FirstOrDefault(t => t.Contains("{project.", StringComparison.Ordinal));
        if (leftover is not null)
        {
            throw new GenerationException(GenerationStage.ValidateOutput,
                new[] { "The generated document still contains unresolved template placeholders." });
        }
    }

    private static IReadOnlyList<Photo> ResolvePhotos(Project project, InspectionReport report)
    {
        var photos = new List<Photo>();
        foreach (var photo in report.Photos)
        {
            photos.Add(new Photo
            {
                Caption = photo.Caption,
                StoredFileName = photo.StoredFileName,
                SourcePath = ReportStore.ResolvePhotoSourcePath(project, report, photo)
            });
        }

        return photos;
    }

    private static Dictionary<string, object> BuildValueDictionary(Project project, InspectionReport report)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["project.name"] = project.Name,
            ["project.num"] = project.Number,
            ["project.owner"] = project.Owner,
            ["project.contract"] = project.ContractManager,
            ["project.general"] = project.GeneralContractor,
            ["project.report.num"] = ProjectLayout.FormatReportNumber(report.Number),
            ["project.report.date"] = report.Date,
            ["project.report.temp"] = OrNotApplicable(report.Temperature),
            ["project.report.weather"] = report.Weather,
            ["project.report.location"] = report.Locations,
            ["project.report.inspector"] = report.Inspectors,
            ["project.report.personnel"] = report.PersonnelOnSite,
            ["project.report.description"] = report.DescriptionOfWork,
            ["project.report.drawing"] = report.DrawingsReviewed,
            ["project.report.observations"] = OrNotApplicable(report.Observations),
            ["project.report.new_discrepancies"] = OrNotApplicable(report.NewDiscrepancies),
            ["project.report.old_discrepancies"] = OrNotApplicable(report.PreviousDiscrepancies),
        };
    }

    private static string OrNotApplicable(string value)
        => string.IsNullOrWhiteSpace(value) ? "N/A" : value;

    private static void FillTextPlaceholders(Body body, IReadOnlyDictionary<string, object> values)
    {
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            ReplaceInParagraph(paragraph, values);
        }
    }

    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, object> values)
    {
        var runs = paragraph.Elements<Run>().ToList();
        if (runs.Count == 0)
        {
            return;
        }

        var texts = runs.Select(TextOf).ToList();
        var full = string.Concat(texts);
        var matches = Regex.Matches(full, @"\{([^{}\r\n]+)\}").Cast<Match>().ToList();
        if (matches.Count == 0)
        {
            return;
        }

        var plans = new List<(int Start, int End, string Value)>();
        foreach (var match in matches)
        {
            if (TryResolvePlaceholder(match.Groups[1].Value, values, out var value))
            {
                plans.Add((match.Index, match.Index + match.Length, value));
            }
        }

        if (plans.Count == 0)
        {
            return;
        }

        var offsets = new int[runs.Count + 1];
        for (var i = 0; i < runs.Count; i++)
        {
            offsets[i + 1] = offsets[i] + texts[i].Length;
        }

        var newTexts = texts.ToArray();
        var clearedRuns = new HashSet<int>();

        foreach (var (start, end, value) in plans)
        {
            var first = RunIndexOfOffset(offsets, start);
            var last = RunIndexOfOffset(offsets, end - 1);
            if (first < 0 || last < 0)
            {
                continue;
            }

            if (first == last)
            {
                var localStart = start - offsets[first];
                var localEnd = end - offsets[first];
                newTexts[first] = newTexts[first][..localStart] + value + newTexts[first][localEnd..];
            }
            else
            {
                var prefixLen = start - offsets[first];
                newTexts[first] = newTexts[first][..prefixLen] + value;
                for (var k = first + 1; k < last; k++)
                {
                    newTexts[k] = string.Empty;
                    clearedRuns.Add(k);
                }

                var suffixStart = end - offsets[last];
                newTexts[last] = newTexts[last][suffixStart..];
                if (newTexts[last].Length == 0)
                {
                    clearedRuns.Add(last);
                }
            }
        }

        for (var i = 0; i < runs.Count; i++)
        {
            if (clearedRuns.Contains(i))
            {
                if (string.IsNullOrEmpty(newTexts[i]))
                {
                    runs[i].Remove();
                    continue;
                }
            }

            if (newTexts[i] != texts[i])
            {
                SetRunText(runs[i], newTexts[i]);
            }
        }
    }

    private static bool TryResolvePlaceholder(string inner, IReadOnlyDictionary<string, object> values, out string value)
    {
        value = string.Empty;
        string? format = null;
        var semicolon = inner.IndexOf(';');
        if (semicolon >= 0)
        {
            format = inner[(semicolon + 1)..].Trim();
            inner = inner[..semicolon];
        }

        var key = NormalizePlaceholder(inner);
        if (!values.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = FormatValue(raw, format);
        return true;
    }

    private static string FormatValue(object value, string? format)
    {
        if (value is DateTime date)
        {
            if (string.IsNullOrEmpty(format))
            {
                return date.ToString("MMMM d, yyyy");
            }

            return date.ToString(NormalizeDateFormat(format));
        }

        if (string.IsNullOrEmpty(format))
        {
            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        return value is IFormattable formattable
            ? formattable.ToString(format, CultureInfo.CurrentCulture)
            : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
    }

    private static string NormalizeDateFormat(string format)
        => format.Replace("mm", "MM");

    private static int RunIndexOfOffset(IReadOnlyList<int> offsets, int offset)
    {
        for (var i = 0; i < offsets.Count - 1; i++)
        {
            if (offset >= offsets[i] && offset < offsets[i + 1])
            {
                return i;
            }
        }

        return offsets.Count - 2;
    }

    private static string NormalizePlaceholder(string raw)
        => new(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string TextOf(Run run)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var element in run.Elements())
        {
            if (element is Text text)
            {
                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }

    private static void SetRunText(Run run, string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        foreach (var element in run.Elements().ToList())
        {
            if (element is not RunProperties)
            {
                element.Remove();
            }
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.AppendChild(new Break());
            }

            run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
    }

    private static void FillPhotoSlots(MainDocumentPart mainPart, Body body, IReadOnlyList<Photo> photos)
    {
        var places = PhotoTable.FindPhotoPlaces(body);
        if (places.Count == 0)
        {
            throw new GenerationException(GenerationStage.ProcessPhotos,
                new[] { "The template does not contain a photo table." });
        }

        var place = places[0];
        if (photos.Count == 0)
        {
            if (!place.IsRemovable)
            {
                throw new GenerationException(GenerationStage.ProcessPhotos,
                    new[] { "The template photo section could not be located for removal." });
            }

            place.HeadingParagraph!.Remove();
            place.Table.Remove();
            place.InstructionParagraph!.Remove();
            return;
        }

        var slots = new List<PhotoSlot>(place.Slots);
        var photoId = 1000;

        if (photos.Count > slots.Count)
        {
            ExpandPhotoTables(place, photos.Count, slots);
        }

        for (var i = 0; i < slots.Count && i < photos.Count; i++)
        {
            InsertPhoto(mainPart, slots[i].ImageParagraph, photos[i], ref photoId);
            FillCaption(slots[i].CaptionParagraph, i + 1, photos[i].Caption);
        }

        for (var i = photos.Count; i < slots.Count; i++)
        {
            RemoveSlotRows(slots[i]);
        }
    }

    private static void ExpandPhotoTables(PhotoPlace place, int photoCount, List<PhotoSlot> slots)
    {
        var anchor = place.Table;
        while (slots.Count < photoCount)
        {
            var clone = (Table)place.Table.CloneNode(true);
            var pageBreak = new Paragraph(new Run(new Break { Type = BreakValues.Page }));
            anchor.InsertAfterSelf(pageBreak);
            pageBreak.InsertAfterSelf(clone);
            anchor = clone;

            var cloneSlots = PhotoTable.CollectSlots(clone);
            if (cloneSlots.Count == 0)
            {
                throw new GenerationException(GenerationStage.ProcessPhotos,
                    new[] { "The cloned photo table could not be populated." });
            }

            slots.AddRange(cloneSlots);
        }
    }

    private static void RemoveSlotRows(PhotoSlot slot)
    {
        slot.ImageParagraph.Ancestors<TableRow>().FirstOrDefault()?.Remove();
        slot.CaptionParagraph.Ancestors<TableRow>().FirstOrDefault()?.Remove();
    }

    private static void FillCaption(Paragraph paragraph, int photoNumber, string caption)
    {
        var runs = paragraph.Elements<Run>().ToList();
        if (runs.Count == 0)
        {
            return;
        }

        var texts = runs.Select(TextOf).ToList();
        var full = string.Concat(texts);
        var span = Regex.Match(full, @"\{[^{}\r\n]*caption\}");
        if (!span.Success)
        {
            return;
        }

        var offsets = new int[runs.Count + 1];
        for (var i = 0; i < runs.Count; i++)
        {
            offsets[i + 1] = offsets[i] + texts[i].Length;
        }

        var spanStartRun = RunIndexOfOffset(offsets, span.Index);
        var labelProperties = runs[0].RunProperties?.CloneNode(true) as RunProperties;
        var captionProperties = runs[spanStartRun].RunProperties?.CloneNode(true) as RunProperties;

        paragraph.RemoveAllChildren<Run>();

        var labelText = string.IsNullOrEmpty(caption) ? $"Photo {photoNumber}" : $"Photo {photoNumber}: ";
        if (!string.IsNullOrEmpty(labelText))
        {
            var labelRun = new Run();
            if (labelProperties is not null)
            {
                labelRun.Append(labelProperties);
            }

            labelRun.Append(new Text(labelText) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(labelRun);
        }

        if (!string.IsNullOrEmpty(caption))
        {
            var captionRun = new Run();
            if (captionProperties is not null)
            {
                captionRun.Append(captionProperties);
            }

            SetRunText(captionRun, caption);
            paragraph.Append(captionRun);
        }
    }

    private static void RemoveInstructionText(Body body)
    {
        foreach (var paragraph in body.Descendants<Paragraph>().ToList())
        {
            StripInstructionsFromParagraph(paragraph);
        }
    }

    private static void StripInstructionsFromParagraph(Paragraph paragraph)
    {
        var runs = paragraph.Elements<Run>().ToList();
        if (runs.Count == 0)
        {
            return;
        }

        var texts = runs.Select(TextOf).ToList();
        var full = string.Concat(texts);
        var matches = Regex.Matches(full, @"\{([^{}\r\n]+)\}").Cast<Match>().ToList();
        if (matches.Count == 0)
        {
            return;
        }

        var spans = new List<(int Start, int End)>();
        foreach (var match in matches)
        {
            if (NormalizePlaceholder(match.Groups[1].Value).StartsWith("project.", StringComparison.Ordinal))
            {
                continue;
            }

            spans.Add((match.Index, match.Index + match.Length));
        }

        if (spans.Count == 0)
        {
            return;
        }

        var offsets = new int[runs.Count + 1];
        for (var i = 0; i < runs.Count; i++)
        {
            offsets[i + 1] = offsets[i] + texts[i].Length;
        }

        var newTexts = texts.ToArray();
        var clearedRuns = new HashSet<int>();

        foreach (var (start, end) in spans)
        {
            var first = RunIndexOfOffset(offsets, start);
            var last = RunIndexOfOffset(offsets, end - 1);
            if (first < 0 || last < 0)
            {
                continue;
            }

            if (first == last)
            {
                var localStart = start - offsets[first];
                var localEnd = end - offsets[first];
                newTexts[first] = newTexts[first][..localStart] + newTexts[first][localEnd..];
            }
            else
            {
                var prefixLen = start - offsets[first];
                newTexts[first] = newTexts[first][..prefixLen];
                for (var k = first + 1; k < last; k++)
                {
                    newTexts[k] = string.Empty;
                    clearedRuns.Add(k);
                }

                var suffixStart = end - offsets[last];
                newTexts[last] = newTexts[last][suffixStart..];
                if (newTexts[last].Length == 0)
                {
                    clearedRuns.Add(last);
                }
            }
        }

        for (var i = 0; i < runs.Count; i++)
        {
            if (clearedRuns.Contains(i))
            {
                if (string.IsNullOrEmpty(newTexts[i]))
                {
                    runs[i].Remove();
                    continue;
                }
            }

            if (newTexts[i] != texts[i])
            {
                SetRunText(runs[i], newTexts[i]);
            }
        }

        if (string.IsNullOrWhiteSpace(PhotoTable.ParagraphText(paragraph)))
        {
            paragraph.Remove();
        }
    }

    private static void InsertPhoto(MainDocumentPart mainPart, Paragraph paragraph, Photo photo, ref int photoId)
    {
        foreach (var run in paragraph.Elements<Run>().ToList())
        {
            run.Remove();
        }

        var relationshipId = NewRelationshipId(mainPart);
        var imagePart = mainPart.AddImagePart(ImagePartManager.GetContentType(photo.SourcePath), relationshipId);
        using (var stream = File.OpenRead(photo.SourcePath))
        {
            imagePart.FeedData(stream);
        }

        var (maxWidth, maxHeight) = GetFrameSize(paragraph);
        var (naturalWidth, naturalHeight) = ImageInfo.GetPixelSize(photo.SourcePath);

        var naturalWidthEmu = naturalWidth * EmuPerPixel;
        var naturalHeightEmu = naturalHeight * EmuPerPixel;
        var scale = Math.Min((double)maxWidth / naturalWidthEmu, (double)maxHeight / naturalHeightEmu);
        scale = Math.Min(scale, 1.0);
        var cx = (long)(naturalWidthEmu * scale);
        var cy = (long)(naturalHeightEmu * scale);

        var drawing = CreateInlineDrawing(relationshipId, cx, cy, photoId);
        photoId++;
        paragraph.AppendChild(new Run(drawing));
    }

    private static string NewRelationshipId(MainDocumentPart mainPart)
    {
        var existing = new HashSet<string>(mainPart.Parts.Select(p => p.RelationshipId));
        var i = 1;
        string id;
        do
        {
            id = $"rIdGen{i++}";
        }
        while (existing.Contains(id));

        return id;
    }

    private static (long Width, long Height) GetFrameSize(Paragraph paragraph)
    {
        var cell = paragraph.Ancestors<TableCell>().FirstOrDefault();
        var row = paragraph.Ancestors<TableRow>().FirstOrDefault();

        var widthDxa = 9200.0;
        if (cell?.TableCellProperties?.TableCellWidth?.Width?.Value is string w && double.TryParse(w, out var parsedWidth))
        {
            widthDxa = parsedWidth;
        }

        double left = 80, right = 80;
        var margins = cell?.TableCellProperties?.TableCellMargin;
        if (margins?.StartMargin?.Width?.Value is string ms && double.TryParse(ms, out var parsedStart)) left = parsedStart;
        if (margins?.EndMargin?.Width?.Value is string me && double.TryParse(me, out var parsedEnd)) right = parsedEnd;
        widthDxa -= left + right;

        var heightDxa = 4824.0;
        var rowHeight = row?.TableRowProperties?.Elements<TableRowHeight>().FirstOrDefault();
        if (rowHeight?.Val?.Value is uint hv && hv > 0)
        {
            heightDxa = hv;
        }

        double top = 80, bottom = 80;
        if (margins?.TopMargin?.Width?.Value is string mt && double.TryParse(mt, out var parsedTop)) top = parsedTop;
        if (margins?.BottomMargin?.Width?.Value is string mb && double.TryParse(mb, out var parsedBottom)) bottom = parsedBottom;
        heightDxa -= top + bottom;

        return ((long)(widthDxa * DxaToEmu), (long)(heightDxa * DxaToEmu));
    }

    private static Drawing CreateInlineDrawing(string relationshipId, long cx, long cy, int id)
    {
        var docProperties = new DW.DocProperties { Id = (uint)id, Name = $"Picture {id}" };
        var graphicFrameLocks = new A.GraphicFrameLocks { NoChangeAspect = true };
        var nonVisualGraphicFrameProperties = new DW.NonVisualGraphicFrameDrawingProperties(graphicFrameLocks);

        var blipFill = new PIC.BlipFill(
            new A.Blip { Embed = relationshipId },
            new A.Stretch(new A.FillRectangle()));

        var shapeProperties = new PIC.ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = cx, Cy = cy }),
            new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle });

        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties { Id = (uint)id, Name = $"Picture {id}" },
                new PIC.NonVisualPictureDrawingProperties()),
            blipFill,
            shapeProperties);

        var graphic = new A.Graphic(
            new A.GraphicData(picture)
            {
                Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
            });

        var inline = new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy },
            new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
            docProperties,
            nonVisualGraphicFrameProperties,
            graphic);

        return new Drawing(inline);
    }

    private static void ReplaceSignatures(MainDocumentPart mainPart, Project project)
    {
        var body = mainPart.Document.Body!;
        foreach (var (tag, role) in SignatureAreas)
        {
            var block = body.Descendants<SdtBlock>()
                .FirstOrDefault(b => b.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value == tag);
            if (block is null)
            {
                throw new GenerationException(GenerationStage.InsertSignatures,
                    new[] { $"The template is missing the {tag} signature area." });
            }

            var signaturePath = tag == TemplateValidator.RequiredSignatureTags[0]
                ? project.ResolvedInspectorSignaturePath
                : project.ResolvedProjectManagerSignaturePath;

            if (string.IsNullOrWhiteSpace(signaturePath) || !File.Exists(signaturePath))
            {
                throw new GenerationException(GenerationStage.InsertSignatures,
                    new[] { $"The {role} signature image file is missing." });
            }

            ReplaceSignatureImage(mainPart, block, signaturePath);
        }
    }

    private static void ReplaceSignatureImage(MainDocumentPart mainPart, SdtBlock block, string signaturePath)
    {
        var blip = block.Descendants<A.Blip>().FirstOrDefault();
        if (blip is null)
        {
            throw new GenerationException(GenerationStage.InsertSignatures,
                new[] { "A template signature area contains no image." });
        }

        var oldImageIds = block.Descendants()
            .SelectMany(element => element.GetAttributes())
            .Where(a => a.NamespaceUri == RelationshipNamespace && !string.IsNullOrEmpty(a.Value))
            .Select(a => a.Value!)
            .Distinct()
            .Where(id => IsImagePart(mainPart, id))
            .ToList();
        if (oldImageIds.Count == 0)
        {
            throw new GenerationException(GenerationStage.InsertSignatures,
                new[] { "A template signature image is not wired to the document." });
        }

        var newRelationshipId = NewRelationshipId(mainPart);
        var newPart = mainPart.AddImagePart(ImagePartManager.GetContentType(signaturePath), newRelationshipId);
        using (var stream = File.OpenRead(signaturePath))
        {
            newPart.FeedData(stream);
        }

        blip.Embed = newRelationshipId;

        foreach (var element in block.Descendants())
        {
            foreach (var attribute in element.GetAttributes())
            {
                if (attribute.NamespaceUri == RelationshipNamespace
                    && attribute.Value is not null
                    && oldImageIds.Contains(attribute.Value))
                {
                    element.SetAttribute(new OpenXmlAttribute(attribute.LocalName, attribute.NamespaceUri, newRelationshipId));
                }
            }
        }

        foreach (var id in oldImageIds)
        {
            var oldPart = mainPart.GetPartById(id);
            mainPart.DeletePart(oldPart);
        }
    }

    private static bool IsImagePart(MainDocumentPart mainPart, string relationshipId)
    {
        try
        {
            return mainPart.GetPartById(relationshipId) is ImagePart;
        }
        catch
        {
            return false;
        }
    }
}
