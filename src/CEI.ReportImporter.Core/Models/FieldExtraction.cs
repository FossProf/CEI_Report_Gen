namespace CEI.ReportImporter.Core.Models;

public sealed record FieldExtraction<T>
{
    public FieldExtraction()
    {
    }

    public FieldExtraction(
        T? value,
        ExtractionConfidence confidence,
        string source,
        IReadOnlyList<string> warnings,
        IReadOnlyList<DetectedFieldValue<T>> candidates)
    {
        Value = value;
        Confidence = confidence;
        Source = source;
        Warnings = warnings;
        Candidates = candidates;
    }

    public T? Value { get; init; }

    public ExtractionConfidence Confidence { get; init; } = ExtractionConfidence.None;

    public string Source { get; init; } = "Not found";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DetectedFieldValue<T>> Candidates { get; init; } = Array.Empty<DetectedFieldValue<T>>();
}
