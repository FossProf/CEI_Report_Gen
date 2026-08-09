namespace CEI.ReportGenerator.Core.Models;

public sealed class HistoricalImportMetadata
{
    public string SourceFileName { get; set; } = string.Empty;

    public string? SourcePathAtImport { get; set; }

    public string SourceSha256 { get; set; } = string.Empty;

    public DateTime ImportedUtc { get; set; }

    public string ParserProfile { get; set; } = string.Empty;

    public string ContractVersion { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = new();
}
