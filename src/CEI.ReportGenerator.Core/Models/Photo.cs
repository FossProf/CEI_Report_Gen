namespace CEI.ReportGenerator.Core.Models;

public sealed class Photo
{
    public string SourcePath { get; set; } = string.Empty;

    public string StoredFileName { get; set; } = string.Empty;

    public string Caption { get; set; } = string.Empty;
}
