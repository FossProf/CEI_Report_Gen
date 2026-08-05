using System.Text.RegularExpressions;
using CEI.ReportGenerator.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace CEI.ReportGenerator.Core.Services;

public sealed record GenerationResult(string OutputPath);

public sealed class GenerationException : Exception
{
    public GenerationException(IReadOnlyList<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

public static class TemplateFiller
{
    private const double DxaToEmu = 635.0;

    private const long EmuPerPixel = 9525; // 914400 EMU per inch at 96 DPI

    private static readonly string RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static GenerationResult Generate(Project project, InspectionReport report, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(project.TemplatePath) || !File.Exists(project.TemplatePath))
        {
            throw new GenerationException(new[] { "The approved Word template could not be found." });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.Copy(project.TemplatePath, outputPath, overwrite: true);

        using var document = WordprocessingDocument.Open(outputPath, true);
        var mainPart = document.MainDocumentPart ?? throw new GenerationException(new[] { "The template is not a valid Word document." });
        var body = mainPart.Document.Body ?? throw new GenerationException(new[] { "The template has no document body." });

        var values = BuildValueDictionary(project, report);
        FillTextPlaceholders(body, values);
        FillPhotoSlots(mainPart, body, report.Photos);
        ReplaceSignatures(mainPart, project);

        mainPart.Document.Save();
        return new GenerationResult(outputPath);
    }

    private static Dictionary<string, string> BuildValueDictionary(Project project, InspectionReport report)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project.name"] = project.Name,
            ["project.num"] = project.Number,
            ["project.owner"] = project.Owner,
            ["project.contract"] = project.ContractManager,
            ["project.general"] = project.GeneralContractor,
            ["project.report.num"] = ProjectLayout.FormatReportNumber(report.Number),
            ["project.report.date"] = report.Date.ToString("MMMM d, yyyy"),
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

    private static void FillTextPlaceholders(Body body, IReadOnlyDictionary<string, string> values)
    {
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            ReplaceInParagraph(paragraph, values);
        }
    }

    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> values)
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
            var key = NormalizePlaceholder(match.Groups[1].Value);
            if (values.TryGetValue(key, out var value))
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
        var textElements = run.Elements<Text>().ToList();
        if (textElements.Count == 0)
        {
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return;
        }

        textElements[0].Text = text;
        textElements[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < textElements.Count; i++)
        {
            textElements[i].Remove();
        }
    }

    private static void FillPhotoSlots(MainDocumentPart mainPart, Body body, IReadOnlyList<Photo> photos)
    {
        var slots = CollectPhotoSlots(body);
        if (slots.Count == 0)
        {
            throw new GenerationException(new[] { "The template does not contain photo placeholders." });
        }

        RemoveUnusedSlots(slots, photos.Count);
        AddClonedSlots(slots, photos.Count, body);

        var photoId = 1000;
        for (var i = 0; i < photos.Count; i++)
        {
            var slot = slots[i];
            InsertPhoto(mainPart, slot.ImageParagraph, photos[i], ref photoId);
            FillCaption(slot.CaptionParagraph, i + 1, photos[i].Caption);
        }
    }

    private static List<PhotoSlot> CollectPhotoSlots(Body body)
    {
        var slots = new List<PhotoSlot>();
        var pendingImage = default(Paragraph);

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var text = string.Concat(paragraph.Elements<Run>().Select(TextOf));
            if (text.Contains(".image}"))
            {
                pendingImage = paragraph;
            }
            else if (text.Contains(".caption}") && pendingImage is not null)
            {
                slots.Add(new PhotoSlot(pendingImage, paragraph));
                pendingImage = null;
            }
        }

        return slots;
    }

    private static void RemoveUnusedSlots(IReadOnlyList<PhotoSlot> slots, int photoCount)
    {
        for (var i = slots.Count - 1; i >= photoCount; i--)
        {
            RemoveSlotRows(slots[i]);
        }
    }

    private static void RemoveSlotRows(PhotoSlot slot)
    {
        slot.ImageParagraph.Ancestors<TableRow>().FirstOrDefault()?.Remove();
        slot.CaptionParagraph.Ancestors<TableRow>().FirstOrDefault()?.Remove();
    }

    private static void AddClonedSlots(List<PhotoSlot> slots, int photoCount, Body body)
    {
        if (photoCount <= slots.Count)
        {
            return;
        }

        var sourceRow = slots[0].ImageParagraph.Ancestors<TableRow>().First();
        var sourceCaptionRow = slots[0].CaptionParagraph.Ancestors<TableRow>().First();
        var anchor = slots[^1].CaptionParagraph.Ancestors<TableRow>().First();

        while (slots.Count < photoCount)
        {
            var imageRowClone = (TableRow)sourceRow.CloneNode(true);
            var captionRowClone = (TableRow)sourceCaptionRow.CloneNode(true);
            anchor.InsertAfterSelf(captionRowClone);
            anchor.InsertAfterSelf(imageRowClone);
            anchor = captionRowClone;

            var imageParagraph = imageRowClone.Descendants<Paragraph>()
                .First(p => string.Concat(p.Elements<Run>().Select(TextOf)).Contains(".image}"));
            var captionParagraph = captionRowClone.Descendants<Paragraph>()
                .First(p => string.Concat(p.Elements<Run>().Select(TextOf)).Contains(".caption}"));
            slots.Add(new PhotoSlot(imageParagraph, captionParagraph));
        }
    }

    private static void FillCaption(Paragraph paragraph, int photoNumber, string caption)
    {
        foreach (var run in paragraph.Elements<Run>())
        {
            var text = TextOf(run);
            if (text.Contains(".caption}"))
            {
                SetRunText(run, caption);
            }
            else if (Regex.IsMatch(text, @"^Photo\s*\d*\s*:"))
            {
                SetRunText(run, $"Photo {photoNumber}: ");
            }
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
        var signatureParagraphs = mainPart.Document.Body!.Descendants<Paragraph>()
            .Where(p => p.Descendants<A.Blip>().Any())
            .ToList();

        if (signatureParagraphs.Count == 0)
        {
            throw new GenerationException(new[] { "The template does not contain signature placeholders." });
        }

        var signatures = new[] { project.InspectorSignaturePath, project.ProjectManagerSignaturePath };
        for (var i = 0; i < signatures.Length && i < signatureParagraphs.Count; i++)
        {
            ReplaceSignatureImage(mainPart, signatureParagraphs[i], signatures[i]);
        }
    }

    private static void ReplaceSignatureImage(MainDocumentPart mainPart, Paragraph paragraph, string signaturePath)
    {
        if (string.IsNullOrWhiteSpace(signaturePath) || !File.Exists(signaturePath))
        {
            throw new GenerationException(new[] { "A signature image file is missing." });
        }

        var blip = paragraph.Descendants<A.Blip>().First();
        var oldRelationshipId = blip.Embed?.Value;
        if (string.IsNullOrEmpty(oldRelationshipId))
        {
            throw new GenerationException(new[] { "A template signature image is not wired to the document." });
        }

        var oldPart = (ImagePart)mainPart.GetPartById(oldRelationshipId);

        var newRelationshipId = NewRelationshipId(mainPart);
        var newPart = mainPart.AddImagePart(ImagePartManager.GetContentType(signaturePath), newRelationshipId);
        using (var stream = File.OpenRead(signaturePath))
        {
            newPart.FeedData(stream);
        }

        blip.Embed = newRelationshipId;

        var updates = new List<(OpenXmlElement Element, string LocalName, string Value)>();
        foreach (var element in paragraph.Descendants())
        {
            foreach (var attribute in element.GetAttributes())
            {
                if (attribute.NamespaceUri == RelationshipNamespace && attribute.Value == oldRelationshipId)
                {
                    updates.Add((element, attribute.LocalName, newRelationshipId));
                }
            }
        }

        foreach (var (element, localName, value) in updates)
        {
            element.SetAttribute(new OpenXmlAttribute(localName, RelationshipNamespace, value));
        }

        mainPart.DeletePart(oldPart);
    }

    private sealed class PhotoSlot
    {
        public PhotoSlot(Paragraph imageParagraph, Paragraph captionParagraph)
        {
            ImageParagraph = imageParagraph;
            CaptionParagraph = captionParagraph;
        }

        public Paragraph ImageParagraph { get; }

        public Paragraph CaptionParagraph { get; }
    }
}
