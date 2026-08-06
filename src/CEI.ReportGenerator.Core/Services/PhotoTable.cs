using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CEI.ReportGenerator.Core.Services;

public sealed class PhotoSlot
{
    public PhotoSlot(Paragraph imageParagraph, Paragraph captionParagraph)
    {
        ImageParagraph = imageParagraph;
        CaptionParagraph = captionParagraph;
    }

    public Paragraph ImageParagraph { get; }

    public Paragraph CaptionParagraph { get; }
}

public sealed class PhotoPlace
{
    public PhotoPlace(Paragraph? headingParagraph, Table table, Paragraph? instructionParagraph, IReadOnlyList<PhotoSlot> slots)
    {
        HeadingParagraph = headingParagraph;
        Table = table;
        InstructionParagraph = instructionParagraph;
        Slots = slots;
    }

    public Paragraph? HeadingParagraph { get; }

    public Table Table { get; }

    public Paragraph? InstructionParagraph { get; }

    public IReadOnlyList<PhotoSlot> Slots { get; }

    public bool IsRemovable => HeadingParagraph is not null && InstructionParagraph is not null;

    public bool CanRepeat => Slots.Count >= 1;
}

public static class PhotoTable
{
    public static string ParagraphText(Paragraph paragraph)
        => string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));

    public static List<PhotoPlace> FindPhotoPlaces(Body body)
    {
        var places = new List<PhotoPlace>();
        foreach (var table in body.Elements<Table>().ToList())
        {
            var slots = CollectSlots(table);
            if (slots.Count == 0)
            {
                continue;
            }

            places.Add(new PhotoPlace(
                FindAdjacentParagraph(table, preceding: true),
                table,
                FindAdjacentParagraph(table, preceding: false),
                slots));
        }

        return places;
    }

    public static List<PhotoSlot> CollectSlots(Table table)
    {
        var slots = new List<PhotoSlot>();
        Paragraph? pendingImage = null;
        foreach (var paragraph in table.Descendants<Paragraph>())
        {
            var text = ParagraphText(paragraph);
            if (text.Contains(".image}", StringComparison.Ordinal))
            {
                pendingImage = paragraph;
            }
            else if (text.Contains(".caption}", StringComparison.Ordinal) && pendingImage is not null)
            {
                slots.Add(new PhotoSlot(pendingImage, paragraph));
                pendingImage = null;
            }
        }

        return slots;
    }

    private static Paragraph? FindAdjacentParagraph(Table table, bool preceding)
    {
        OpenXmlElement? sibling = preceding ? table.PreviousSibling() : table.NextSibling();
        if (sibling is not Paragraph paragraph)
        {
            return null;
        }

        var text = ParagraphText(paragraph);
        var expected = preceding ? "photo documentation" : "repeat this photo page";
        return text.Contains(expected, StringComparison.OrdinalIgnoreCase) ? paragraph : null;
    }
}
