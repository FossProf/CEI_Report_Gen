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

    public int TotalCount => Items.Count;

    public int UnreviewedCount => Items.Count(item => item.ReviewState == HistoricalReviewState.Unreviewed);

    public int NeedsReviewCount => Items.Count(item => item.ReviewState == HistoricalReviewState.NeedsReview);

    public int ReadyCount => Items.Count(item => item.ReviewState == HistoricalReviewState.Ready);

    public int ExcludedCount => Items.Count(item => item.ReviewState == HistoricalReviewState.Excluded);

    public int ParseFailedCount => Items.Count(item => !item.ParseSucceeded);

    public bool HasInMemoryReviewWork => Items.Any(item =>
        item.HasUserChanges
        || item.ReviewState == HistoricalReviewState.Ready
        || item.ReviewState == HistoricalReviewState.Excluded);
}
