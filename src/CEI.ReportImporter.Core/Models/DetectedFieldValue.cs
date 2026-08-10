namespace CEI.ReportImporter.Core.Models;

public sealed record DetectedFieldValue<T>
{
    public DetectedFieldValue()
    {
    }

    public DetectedFieldValue(T? value, string source)
    {
        Value = value;
        Source = source;
    }

    public T? Value { get; init; }

    public string Source { get; init; } = string.Empty;
}
