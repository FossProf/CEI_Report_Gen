namespace CEI.ReportGenerator.Core.Models;

public sealed record FinalReportFileNameInfo(
    string DatePart,
    string ProjectNamePart,
    string ReportNumberPart,
    string FileName);
