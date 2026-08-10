using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportImporter.Core.Services;

public sealed class HistoricalReviewValidator
{
    public HistoricalReviewValidationResult Validate(HistoricalReportImportRequest? request)
    {
        var messages = new List<string>();
        if (request is null)
        {
            messages.Add("This document did not produce a recoverable historical import request.");
            return new HistoricalReviewValidationResult(false, messages);
        }

        if (request.Number <= 0)
        {
            messages.Add("Report Number must be a positive whole number.");
        }

        if (request.Date == default)
        {
            messages.Add("Inspection Date is required.");
        }

        return new HistoricalReviewValidationResult(messages.Count == 0, messages);
    }
}
