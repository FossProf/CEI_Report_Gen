namespace CEI.ReportImporter.Core.Models;

public sealed class HistoricalReviewSession
{
    public HistoricalReviewSession(HistoricalScanSession scanSession)
    {
        ScanSession = scanSession ?? throw new ArgumentNullException(nameof(scanSession));
        Items = scanSession.Results
            .Select(result => new HistoricalReviewItem(result))
            .ToList();
    }

    public HistoricalScanSession ScanSession { get; }

    public IReadOnlyList<HistoricalReviewItem> Items { get; }

    public IReadOnlyList<HistoricalImportLogEntry> ImportLog => _importLog;

    private readonly List<HistoricalImportLogEntry> _importLog = [];

    public int TotalCount => Items.Count;

    public int UnreviewedCount => Items.Count(item => item.ReviewState == HistoricalReviewState.Unreviewed);

    public int NeedsReviewCount => Items.Count(item => item.ReviewState == HistoricalReviewState.NeedsReview);

    public int ReviewReadyCount => Items.Count(item => item.ReviewState == HistoricalReviewState.Ready);

    public int ExcludedCount => Items.Count(item => item.ReviewState == HistoricalReviewState.Excluded);

    public int ParseFailedCount => Items.Count(item => !item.ParseSucceeded);

    public int ReadyCount => Items.Count(item => item.ImportStatus == HistoricalImportItemStatus.Ready);

    public int ImportedCount => Items.Count(item => item.ImportStatus == HistoricalImportItemStatus.Imported);

    public int SkippedCount => Items.Count(item => item.ImportStatus == HistoricalImportItemStatus.Skipped);

    public int DuplicateCount => Items.Count(item => item.ImportStatus == HistoricalImportItemStatus.Duplicate);

    public int ErrorCount => Items.Count(item =>
        item.ImportStatus is HistoricalImportItemStatus.Conflict
            or HistoricalImportItemStatus.ParseError
            or HistoricalImportItemStatus.MissingData);

    public int SelectedCount => Items.Count(item => item.IsSelected);

    public bool HasInMemoryReviewWork => Items.Any(item =>
        item.HasUserChanges
        || item.ReviewState == HistoricalReviewState.Ready
        || item.ReviewState == HistoricalReviewState.Excluded
        || item.IsSelected);

    public void RecordImportLog(IReadOnlyList<HistoricalImportLogEntry> entries)
    {
        _importLog.Clear();
        _importLog.AddRange(entries);
    }

    public void RefreshSessionCounts()
    {
        ScanSession.ReadyCount = ReadyCount;
        ScanSession.ImportedCount = ImportedCount;
        ScanSession.SkippedCount = SkippedCount;
        ScanSession.DuplicateCount = DuplicateCount;
        ScanSession.ErrorCount = ErrorCount;
        ScanSession.SelectedCount = SelectedCount;
    }
}
