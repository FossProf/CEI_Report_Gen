using CEI.ReportGenerator.Core.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace CEI.Tools.UpdateTemplateTags;

public static class Program
{
    private static readonly string[] Tags = [TemplateValidator.RequiredSignatureTags[0], TemplateValidator.RequiredSignatureTags[1]];

    public static int Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "templates", "CEI_Base_Template_Refined.docx");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Template not found: " + path);
            return 1;
        }

        Console.WriteLine("Updating signature tags in " + path);
        using var document = WordprocessingDocument.Open(path, true);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Template has no body.");

        var missing = Tags.Where(tag => !ContainsTag(body, tag)).ToList();
        if (missing.Count == 0)
        {
            Console.WriteLine("All signature tags already present. Nothing to do.");
            return 0;
        }

        var signatureParagraphs = body.Descendants<Paragraph>()
            .Where(p => p.Descendants<A.Blip>().Any())
            .ToList();

        var paragraphsForTags = AssignParagraphsToTags(signatureParagraphs);
        if (paragraphsForTags.Count < Tags.Length)
        {
            Console.Error.WriteLine($"Expected {Tags.Length} signature drawings but found {paragraphsForTags.Count}.");
            return 1;
        }

        foreach (var tag in missing)
        {
            var index = Array.IndexOf(Tags, tag);
            var paragraph = paragraphsForTags[index];
            if (paragraph is null)
            {
                Console.Error.WriteLine("Could not locate paragraph for " + tag);
                return 1;
            }

            WrapInContentControl(paragraph, tag);
            Console.WriteLine("Wrapped signature drawing in " + tag);
        }

        document.MainDocumentPart!.Document.Save();
        Console.WriteLine("Done. Validate with the TemplateValidator to confirm.");
        return 0;
    }

    private static List<Paragraph?> AssignParagraphsToTags(List<Paragraph> signatureParagraphs)
    {
        var result = new List<Paragraph?>();
        foreach (var paragraph in signatureParagraphs)
        {
            var runs = paragraph.Elements<Run>().Where(r => r.Descendants<A.Blip>().Any()).ToList();
            foreach (var run in runs)
            {
                result.Add(paragraph);
            }
        }

        if (result.Count != Tags.Length)
        {
            return result;
        }

        for (var i = 0; i < result.Count; i++)
        {
            var paragraph = result[i];
            if (paragraph is not null && paragraph.Descendants<A.Blip>().Count() > 1)
            {
                var split = SplitSignatureParagraph(paragraph, i);
                result[i] = split.First;
                if (i + 1 < result.Count && result[i + 1] == paragraph)
                {
                    result[i + 1] = split.Next;
                }
            }
        }

        return result;
    }

    private sealed record SplitPair(Paragraph First, Paragraph Next);

    private static SplitPair SplitSignatureParagraph(Paragraph paragraph, int tagIndex)
    {
        var runs = paragraph.Elements<Run>().ToList();
        var secondDrawingIndex = runs
            .Select((run, index) => (run, index))
            .Skip(1)
            .FirstOrDefault(r => r.run.Descendants<A.Blip>().Any()).index;

        var firstRuns = runs.Take(secondDrawingIndex);
        var secondRuns = runs.Skip(secondDrawingIndex);

        var first = new Paragraph();
        var second = new Paragraph();
        var pPr = paragraph.ParagraphProperties;
        if (pPr is not null)
        {
            first.Append((ParagraphProperties)pPr.CloneNode(true));
            second.Append((ParagraphProperties)pPr.CloneNode(true));
        }

        foreach (var run in firstRuns)
        {
            first.Append((Run)run.CloneNode(true));
        }

        foreach (var run in secondRuns)
        {
            second.Append((Run)run.CloneNode(true));
        }

        paragraph.InsertBeforeSelf(first);
        first.InsertAfterSelf(second);
        paragraph.Remove();
        return new SplitPair(first, second);
    }

    private static void WrapInContentControl(Paragraph paragraph, string tag)
    {
        var properties = new SdtProperties(
            new SdtAlias { Val = tag },
            new Tag { Val = tag });
        var content = new SdtContentBlock();
        var block = new SdtBlock(properties, content);

        paragraph.InsertBeforeSelf(block);
        paragraph.Remove();
        content.Append(paragraph);
    }

    private static bool ContainsTag(Body body, string tag)
    {
        return body.Descendants<SdtBlock>().Any(block =>
            block.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value == tag);
    }
}
