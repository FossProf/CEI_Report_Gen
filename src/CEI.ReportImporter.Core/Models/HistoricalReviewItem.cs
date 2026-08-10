using System.ComponentModel;
using System.Runtime.CompilerServices;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportImporter.Core.Services;

namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalReviewItem : INotifyPropertyChanged
{
    public HistoricalReviewItem(HistoricalScanResult scanResult)
    {
        ScanResult = scanResult ?? throw new ArgumentNullException(nameof(scanResult));
        OriginalRequest = scanResult.ImportRequest is null ? null : scanResult.ImportRequest with { };
        WorkingRequest = OriginalRequest is null ? null : OriginalRequest with { };
        InitialReviewState = DetermineInitialReviewState(scanResult.ParseResult);
        ReviewState = InitialReviewState;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HistoricalScanResult ScanResult { get; }

    public HistoricalReportImportRequest? OriginalRequest { get; }

    public HistoricalReportImportRequest? WorkingRequest { get; private set; }

    public HistoricalReviewState InitialReviewState { get; }

    public HistoricalReviewState ReviewState { get; private set; }

    public bool HasUserChanges { get; private set; }

    public IReadOnlyList<string> Warnings => ScanResult.ParseResult.Warnings;

    public HistoricalConfidenceLevel OverallConfidence => ScanResult.ParseResult.OverallConfidence;

    public HistoricalFieldExtractions FieldExtractions => ScanResult.ParseResult.FieldExtractions;

    public bool ParseSucceeded => ScanResult.ParseSucceeded;

    public bool ReadyForImport => ReviewState == HistoricalReviewState.Ready && WorkingRequest is not null;

    public string SourceFilePath => ScanResult.SourceFilePath;

    public string SourceFileName => ScanResult.SourceFileName;

    public string ReviewStateText => ReviewState switch
    {
        HistoricalReviewState.Unreviewed => "Unreviewed",
        HistoricalReviewState.Ready => "Ready",
        HistoricalReviewState.NeedsReview => "Needs Review",
        HistoricalReviewState.Excluded => "Excluded",
        _ => ReviewState.ToString()
    };

    public string ChangedText => HasUserChanges ? "Yes" : "No";

    public string DisplayReportNumber => WorkingRequest?.Number > 0
        ? WorkingRequest.Number.ToString()
        : ScanResult.ParseResult.ReportNumberText is { Length: > 0 } parseValue
            ? parseValue
            : "-";

    public string DisplayDate => WorkingRequest?.Date.ToString("yyyy-MM-dd")
        ?? (ScanResult.ParseResult.DateText is { Length: > 0 } parseValue ? parseValue : "-");

    public string WarningsSummary => Warnings.Count == 0
        ? "-"
        : string.Join("; ", Warnings.Take(2));

    public void UpdateWorkingRequest(HistoricalReportImportRequest updatedRequest)
    {
        ArgumentNullException.ThrowIfNull(updatedRequest);

        var previousRequest = WorkingRequest;
        var previousReviewState = ReviewState;
        var previousHasUserChanges = HasUserChanges;

        WorkingRequest = updatedRequest;
        HasUserChanges = !Equals(OriginalRequest, WorkingRequest);

        if (ReviewState == HistoricalReviewState.Ready)
        {
            ReviewState = HistoricalReviewState.NeedsReview;
        }

        NotifyForEditableStateChange(previousRequest, previousReviewState, previousHasUserChanges);
    }

    public void ResetChanges()
    {
        var previousRequest = WorkingRequest;
        var previousReviewState = ReviewState;
        var previousHasUserChanges = HasUserChanges;

        WorkingRequest = OriginalRequest is null ? null : OriginalRequest with { };
        HasUserChanges = false;
        ReviewState = InitialReviewState;

        NotifyForEditableStateChange(previousRequest, previousReviewState, previousHasUserChanges);
    }

    public void MarkExcluded()
    {
        if (ReviewState == HistoricalReviewState.Excluded)
        {
            return;
        }

        var previousReviewState = ReviewState;
        ReviewState = HistoricalReviewState.Excluded;
        NotifyForStateOnlyChange(previousReviewState);
    }

    public void ReturnToReview()
    {
        if (ReviewState == InitialReviewState)
        {
            return;
        }

        var previousReviewState = ReviewState;
        ReviewState = InitialReviewState;
        NotifyForStateOnlyChange(previousReviewState);
    }

    public bool TryMarkReady(HistoricalReviewValidator validator, out IReadOnlyList<string> validationMessages)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = validator.Validate(WorkingRequest);
        validationMessages = result.Messages;
        if (!result.CanMarkReady)
        {
            return false;
        }

        if (ReviewState == HistoricalReviewState.Ready)
        {
            return true;
        }

        var previousReviewState = ReviewState;
        ReviewState = HistoricalReviewState.Ready;
        NotifyForStateOnlyChange(previousReviewState);
        return true;
    }

    private void NotifyForEditableStateChange(
        HistoricalReportImportRequest? previousRequest,
        HistoricalReviewState previousReviewState,
        bool previousHasUserChanges)
    {
        if (!Equals(previousRequest, WorkingRequest))
        {
            OnPropertyChanged(nameof(WorkingRequest));
            OnPropertyChanged(nameof(DisplayReportNumber));
            OnPropertyChanged(nameof(DisplayDate));
        }

        if (previousHasUserChanges != HasUserChanges)
        {
            OnPropertyChanged(nameof(HasUserChanges));
            OnPropertyChanged(nameof(ChangedText));
        }

        if (previousReviewState != ReviewState)
        {
            OnPropertyChanged(nameof(ReviewState));
            OnPropertyChanged(nameof(ReviewStateText));
            OnPropertyChanged(nameof(ReadyForImport));
        }
    }

    private void NotifyForStateOnlyChange(HistoricalReviewState previousReviewState)
    {
        if (previousReviewState == ReviewState)
        {
            return;
        }

        OnPropertyChanged(nameof(ReviewState));
        OnPropertyChanged(nameof(ReviewStateText));
        OnPropertyChanged(nameof(ReadyForImport));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static HistoricalReviewState DetermineInitialReviewState(HistoricalReportParseResult parseResult)
    {
        if (!parseResult.Success)
        {
            return HistoricalReviewState.NeedsReview;
        }

        if (parseResult.OverallConfidence == HistoricalConfidenceLevel.High
            && !parseResult.Warnings.Any(w => w.Contains("mismatch", StringComparison.OrdinalIgnoreCase)))
        {
            return HistoricalReviewState.Unreviewed;
        }

        return HistoricalReviewState.NeedsReview;
    }
}
