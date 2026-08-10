using System.ComponentModel;
using System.Runtime.CompilerServices;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportImporter.Core.Services;

namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalReviewItem : INotifyPropertyChanged
{
    private HistoricalImportItemStatus _importStatus;
    private string _reason = string.Empty;
    private bool _isSelected;

    public HistoricalReviewItem(HistoricalScanResult scanResult)
    {
        ScanResult = scanResult ?? throw new ArgumentNullException(nameof(scanResult));
        OriginalRequest = scanResult.ImportRequest is null ? null : scanResult.ImportRequest with { };
        WorkingRequest = OriginalRequest is null ? null : OriginalRequest with { };
        InitialReviewState = DetermineInitialReviewState(scanResult.ParseResult);
        ReviewState = InitialReviewState;
        (_importStatus, _reason) = DetermineInitialImportState(scanResult);
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

    public bool ReadyForImport => ImportStatus == HistoricalImportItemStatus.Ready && WorkingRequest is not null;

    public HistoricalImportItemStatus ImportStatus => _importStatus;

    public string ImportStatusText => ImportStatus switch
    {
        HistoricalImportItemStatus.Ready => "Ready",
        HistoricalImportItemStatus.Duplicate => "Duplicate",
        HistoricalImportItemStatus.Conflict => "Conflict",
        HistoricalImportItemStatus.ParseError => "Parse Error",
        HistoricalImportItemStatus.MissingData => "Missing Data",
        HistoricalImportItemStatus.Imported => "Imported",
        HistoricalImportItemStatus.Skipped => "Skipped",
        _ => ImportStatus.ToString()
    };

    public string Reason => string.IsNullOrWhiteSpace(_reason) ? "-" : _reason;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var nextValue = value && CanSelect;
            if (_isSelected == nextValue)
            {
                return;
            }

            _isSelected = nextValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionStateText));
        }
    }

    public bool CanSelect => ImportStatus == HistoricalImportItemStatus.Ready;

    public string SelectionStateText => IsSelected ? "Selected" : "Not Selected";

    public string SourceFilePath => ScanResult.SourceFilePath;

    public string SourceFileName => ScanResult.SourceFileName;

    public string DisplayProject => string.IsNullOrWhiteSpace(ScanResult.ParseResult.ProjectName)
        ? "-"
        : ScanResult.ParseResult.ProjectName;

    public string DisplayInspector => string.IsNullOrWhiteSpace(WorkingRequest?.Inspectors)
        ? "-"
        : WorkingRequest.Inspectors;

    public string DisplayWeather => string.IsNullOrWhiteSpace(WorkingRequest?.Weather)
        ? "-"
        : WorkingRequest.Weather;

    public string ReadyText => ReadyForImport ? "Yes" : "No";

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
            OnPropertyChanged(nameof(DisplayInspector));
            OnPropertyChanged(nameof(DisplayWeather));
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
        }

        OnPropertyChanged(nameof(ReadyForImport));
        OnPropertyChanged(nameof(ReadyText));
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
        OnPropertyChanged(nameof(ReadyText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void ApplyImportStatus(HistoricalImportItemStatus status, string reason)
    {
        var previousStatus = _importStatus;
        var previousReason = _reason;
        var previousCanSelect = CanSelect;
        var previousReady = ReadyForImport;
        var previousDisplayWeather = DisplayWeather;
        var previousDisplayInspector = DisplayInspector;

        _importStatus = status;
        _reason = reason ?? string.Empty;
        if (!CanSelect)
        {
            _isSelected = false;
        }

        if (previousStatus != _importStatus)
        {
            OnPropertyChanged(nameof(ImportStatus));
            OnPropertyChanged(nameof(ImportStatusText));
        }

        if (!string.Equals(previousReason, _reason, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(Reason));
        }

        if (previousCanSelect != CanSelect)
        {
            OnPropertyChanged(nameof(CanSelect));
        }

        if (previousReady != ReadyForImport)
        {
            OnPropertyChanged(nameof(ReadyForImport));
            OnPropertyChanged(nameof(ReadyText));
        }

        if (previousDisplayWeather != DisplayWeather)
        {
            OnPropertyChanged(nameof(DisplayWeather));
        }

        if (previousDisplayInspector != DisplayInspector)
        {
            OnPropertyChanged(nameof(DisplayInspector));
        }

        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectionStateText));
    }

    public void MarkImported(string reason)
        => ApplyImportStatus(HistoricalImportItemStatus.Imported, reason);

    public void MarkSkipped(string reason)
        => ApplyImportStatus(HistoricalImportItemStatus.Skipped, reason);

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

    private static (HistoricalImportItemStatus Status, string Reason) DetermineInitialImportState(HistoricalScanResult scanResult)
    {
        if (!scanResult.ParseSucceeded)
        {
            return (HistoricalImportItemStatus.ParseError,
                string.IsNullOrWhiteSpace(scanResult.ParseResult.FailureMessage)
                    ? "The document could not be parsed."
                    : scanResult.ParseResult.FailureMessage);
        }

        if (scanResult.ImportRequest is null)
        {
            return (HistoricalImportItemStatus.MissingData, "The document did not produce an importable report payload.");
        }

        if (scanResult.ImportRequest.Number <= 0)
        {
            return (HistoricalImportItemStatus.MissingData, "A positive report number is required.");
        }

        if (scanResult.ImportRequest.Date == default)
        {
            return (HistoricalImportItemStatus.MissingData, "An inspection date is required.");
        }

        return (HistoricalImportItemStatus.Ready, string.Empty);
    }
}
