using CEI.ReportImporter.Core.Models;

namespace CEI.ReportImporter.Core.Services;

public sealed class HistoricalReportScanner
{
    private readonly IHistoricalReportParser _parser;

    public HistoricalReportScanner(IHistoricalReportParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public HistoricalScanSession Scan(HistoricalReportScanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.SourceFolder) || !Directory.Exists(options.SourceFolder))
        {
            throw new InvalidOperationException("Select an existing source folder before scanning.");
        }

        var startedUtc = DateTime.UtcNow;
        var searchOption = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(options.SourceFolder, "*.docx", searchOption)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new List<HistoricalScanResult>(files.Count);
        foreach (var file in files)
        {
            var parseResult = _parser.Parse(file);
            results.Add(new HistoricalScanResult
            {
                SourceFilePath = file,
                SourceFileName = Path.GetFileName(file),
                ParseResult = parseResult
            });
        }

        return new HistoricalScanSession
        {
            SessionId = Guid.NewGuid(),
            SourceFolder = options.SourceFolder,
            IncludeSubfolders = options.IncludeSubfolders,
            StartedUtc = startedUtc,
            CompletedUtc = DateTime.UtcNow,
            ParserProfile = _parser.ProfileName,
            Results = results
        };
    }
}
