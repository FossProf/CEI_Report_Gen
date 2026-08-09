using CEI.ReportImporter.Core.Models;

namespace CEI.ReportImporter.Core.Services;

public interface IHistoricalReportParser
{
    string ProfileName { get; }

    HistoricalReportParseResult Parse(string documentPath);
}
