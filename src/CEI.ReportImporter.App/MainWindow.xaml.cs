using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportImporter.Core.Models;
using CEI.ReportImporter.Core.Services;
using Forms = System.Windows.Forms;

namespace CEI.ReportImporter.App;

public partial class MainWindow : Window
{
    private readonly HistoricalReportScanner _scanner = new(new HistoricalDocumentParser());
    private readonly HistoricalReviewValidator _reviewValidator = new();
    private readonly ObservableCollection<HistoricalReviewItem> _visibleResults = [];

    private HistoricalReviewSession? _currentReviewSession;
    private bool _isLoadingSelection;

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _visibleResults;
        ReviewFilterComboBox.SelectedIndex = 0;
        UpdateSelectionState();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing historical CEI SPIN reports."
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SourceFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void OpenSourceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = SourceFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            System.Windows.MessageBox.Show(
                this,
                "Select an existing source folder first.",
                "Folder Unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        TryOpenPath(path, "Unable to open the source folder.");
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentReviewSession?.HasInMemoryReviewWork == true)
        {
            var discard = System.Windows.MessageBox.Show(
                this,
                "A rescan will discard the current in-memory review changes for this session. Continue?",
                "Discard Review Changes?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (discard != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            ScanButton.IsEnabled = false;
            SummaryTextBlock.Text = "Scanning...";

            var session = await Task.Run(() => _scanner.Scan(new HistoricalReportScanOptions
            {
                SourceFolder = SourceFolderTextBox.Text.Trim(),
                IncludeSubfolders = IncludeSubfoldersCheckBox.IsChecked == true
            }));

            _currentReviewSession = new HistoricalReviewSession(session);
            ApplyReviewFilter();
            SummaryTextBlock.Text = BuildSummaryText(_currentReviewSession);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Scan Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SummaryTextBlock.Text = "Scan failed.";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private void ReviewFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyReviewFilter();
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => LoadSelectedReviewItem();

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is HistoricalReviewItem)
        {
            OpenSourceDocument();
        }
    }

    private void OpenSourceDocumentButton_Click(object sender, RoutedEventArgs e)
        => OpenSourceDocument();

    private void EditableFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSelection)
        {
            return;
        }

        CommitWorkingCopyEdits();
    }

    private void EditableDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSelection)
        {
            return;
        }

        CommitWorkingCopyEdits();
    }

    private void MarkReadyButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        CommitWorkingCopyEdits();
        if (!item.TryMarkReady(_reviewValidator, out var messages))
        {
            System.Windows.MessageBox.Show(
                this,
                string.Join(Environment.NewLine, messages),
                "Cannot Mark Ready",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RefreshReviewDisplay(item);
    }

    private void ExcludeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        item.MarkExcluded();
        RefreshReviewDisplay(item);
    }

    private void ReturnToReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        item.ReturnToReview();
        RefreshReviewDisplay(item);
    }

    private void ResetChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        item.ResetChanges();
        RefreshReviewDisplay(item);
    }

    private void ApplyReviewFilter()
    {
        _visibleResults.Clear();

        if (_currentReviewSession is null)
        {
            ClearDetailPane();
            return;
        }

        var filter = GetSelectedFilter();
        foreach (var item in _currentReviewSession.Items.Where(item => MatchesFilter(item, filter)))
        {
            _visibleResults.Add(item);
        }

        ResultsGrid.SelectedItem = _visibleResults.FirstOrDefault();
        SummaryTextBlock.Text = BuildSummaryText(_currentReviewSession);
        UpdateSelectionState();
    }

    private void LoadSelectedReviewItem()
    {
        _isLoadingSelection = true;
        try
        {
            if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
            {
                ClearDetailPane();
                return;
            }

            SelectedReportSummaryTextBlock.Text =
                $"{item.SourceFileName} | State: {item.ReviewStateText} | Confidence: {item.OverallConfidence}";
            WarningsTextBlock.Text = item.Warnings.Count == 0
                ? "No warnings."
                : string.Join(Environment.NewLine, item.Warnings.Select(warning => "* " + warning));

            ReportNumberTextBox.Text = item.WorkingRequest?.Number > 0 ? item.WorkingRequest.Number.ToString() : string.Empty;
            InspectionDatePicker.SelectedDate = item.WorkingRequest?.Date == default ? null : item.WorkingRequest?.Date;
            WeatherTextBox.Text = item.WorkingRequest?.Weather ?? string.Empty;
            TemperatureTextBox.Text = item.WorkingRequest?.Temperature ?? string.Empty;
            LocationsTextBox.Text = item.WorkingRequest?.Locations ?? string.Empty;
            InspectorsTextBox.Text = item.WorkingRequest?.Inspectors ?? string.Empty;
            PersonnelOnSiteTextBox.Text = item.WorkingRequest?.PersonnelOnSite ?? string.Empty;
            DescriptionOfWorkTextBox.Text = item.WorkingRequest?.DescriptionOfWork ?? string.Empty;
            DrawingsReviewedTextBox.Text = item.WorkingRequest?.DrawingsReviewed ?? string.Empty;
            ObservationsTextBox.Text = item.WorkingRequest?.Observations ?? string.Empty;
            NewDiscrepanciesTextBox.Text = item.WorkingRequest?.NewDiscrepancies ?? string.Empty;
            PreviousDiscrepanciesTextBox.Text = item.WorkingRequest?.PreviousDiscrepancies ?? string.Empty;

            ApplyExtractionDetails(item);
            UpdateSelectionState();
        }
        finally
        {
            _isLoadingSelection = false;
        }
    }

    private void ApplyExtractionDetails(HistoricalReviewItem item)
    {
        SetFieldMeta(ReportNumberMetaTextBlock, item.FieldExtractions.ReportNumber);
        ReportNumberCandidatesTextBlock.Text = BuildCandidateText(item.FieldExtractions.ReportNumber.Candidates, FormatNullableInt);

        SetFieldMeta(InspectionDateMetaTextBlock, item.FieldExtractions.InspectionDate);
        InspectionDateCandidatesTextBlock.Text = BuildCandidateText(item.FieldExtractions.InspectionDate.Candidates, FormatNullableDate);

        SetFieldMeta(WeatherMetaTextBlock, item.FieldExtractions.Weather);
        SetFieldMeta(TemperatureMetaTextBlock, item.FieldExtractions.Temperature);
        SetFieldMeta(LocationsMetaTextBlock, item.FieldExtractions.Locations);
        SetFieldMeta(InspectorsMetaTextBlock, item.FieldExtractions.Inspectors);
        SetFieldMeta(PersonnelMetaTextBlock, item.FieldExtractions.PersonnelOnSite);
        SetFieldMeta(DescriptionMetaTextBlock, item.FieldExtractions.DescriptionOfWork);
        SetFieldMeta(DrawingsMetaTextBlock, item.FieldExtractions.DrawingsReviewed);
        SetFieldMeta(ObservationsMetaTextBlock, item.FieldExtractions.Observations);
        SetFieldMeta(NewDiscrepanciesMetaTextBlock, item.FieldExtractions.NewDiscrepancies);
        SetFieldMeta(PreviousDiscrepanciesMetaTextBlock, item.FieldExtractions.PreviousDiscrepancies);
    }

    private void CommitWorkingCopyEdits()
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item || item.WorkingRequest is null)
        {
            return;
        }

        var parsedNumber = int.TryParse(ReportNumberTextBox.Text.Trim(), out var reportNumber)
            ? reportNumber
            : 0;
        var parsedDate = InspectionDatePicker.SelectedDate ?? default;

        var updatedRequest = item.WorkingRequest with
        {
            Number = parsedNumber,
            Date = parsedDate,
            Temperature = TemperatureTextBox.Text,
            Weather = WeatherTextBox.Text,
            Locations = LocationsTextBox.Text,
            Inspectors = InspectorsTextBox.Text,
            PersonnelOnSite = PersonnelOnSiteTextBox.Text,
            DescriptionOfWork = DescriptionOfWorkTextBox.Text,
            DrawingsReviewed = DrawingsReviewedTextBox.Text,
            Observations = ObservationsTextBox.Text,
            NewDiscrepancies = NewDiscrepanciesTextBox.Text,
            PreviousDiscrepancies = PreviousDiscrepanciesTextBox.Text
        };

        item.UpdateWorkingRequest(updatedRequest);
        RefreshReviewDisplay(item, preserveSelection: true);
    }

    private void RefreshReviewDisplay(HistoricalReviewItem item, bool preserveSelection = true)
    {
        var selectedPath = preserveSelection ? item.SourceFilePath : null;
        ApplyReviewFilter();
        if (selectedPath is not null)
        {
            ResultsGrid.SelectedItem = _visibleResults.FirstOrDefault(result => result.SourceFilePath == selectedPath);
        }

        LoadSelectedReviewItem();
    }

    private void ClearDetailPane()
    {
        SelectedReportSummaryTextBlock.Text = "Select a scanned report to review its extracted fields.";
        WarningsTextBlock.Text = "No warnings.";
        ReportNumberTextBox.Text = string.Empty;
        InspectionDatePicker.SelectedDate = null;
        WeatherTextBox.Text = string.Empty;
        TemperatureTextBox.Text = string.Empty;
        LocationsTextBox.Text = string.Empty;
        InspectorsTextBox.Text = string.Empty;
        PersonnelOnSiteTextBox.Text = string.Empty;
        DescriptionOfWorkTextBox.Text = string.Empty;
        DrawingsReviewedTextBox.Text = string.Empty;
        ObservationsTextBox.Text = string.Empty;
        NewDiscrepanciesTextBox.Text = string.Empty;
        PreviousDiscrepanciesTextBox.Text = string.Empty;

        foreach (var block in MetaBlocks())
        {
            block.Text = string.Empty;
        }

        ReportNumberCandidatesTextBlock.Text = string.Empty;
        InspectionDateCandidatesTextBlock.Text = string.Empty;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var item = ResultsGrid.SelectedItem as HistoricalReviewItem;
        var hasSelection = item is not null;
        MarkReadyButton.IsEnabled = hasSelection;
        ExcludeButton.IsEnabled = hasSelection;
        ResetChangesButton.IsEnabled = hasSelection;
        ReturnToReviewButton.IsEnabled = item?.ReviewState is HistoricalReviewState.Ready or HistoricalReviewState.Excluded;
        OpenSourceDocumentButton.IsEnabled = hasSelection;
    }

    private void OpenSourceDocument()
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        TryOpenPath(item.SourceFilePath, "Unable to open the source document. Confirm it still exists and is associated with an application.");
    }

    private void TryOpenPath(string path, string failureMessage)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException("Path not found.", path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                $"{failureMessage}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private string GetSelectedFilter()
        => ReviewFilterComboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? "All"
            : "All";

    private static bool MatchesFilter(HistoricalReviewItem item, string filter)
        => filter switch
        {
            "Unreviewed" => item.ReviewState == HistoricalReviewState.Unreviewed,
            "Needs Review" => item.ReviewState == HistoricalReviewState.NeedsReview,
            "Ready" => item.ReviewState == HistoricalReviewState.Ready,
            "Excluded" => item.ReviewState == HistoricalReviewState.Excluded,
            _ => true
        };

    private static string BuildSummaryText(HistoricalReviewSession session)
        => $"{session.TotalCount} scanned | {session.UnreviewedCount} unreviewed | {session.NeedsReviewCount} needs review | {session.ReadyCount} ready | {session.ExcludedCount} excluded | {session.ParseFailedCount} failed parse";

    private static void SetFieldMeta<T>(TextBlock target, FieldExtraction<T> extraction)
    {
        var warningText = extraction.Warnings.Count == 0
            ? string.Empty
            : $" | Warnings: {string.Join("; ", extraction.Warnings)}";
        target.Text = $"Confidence: {extraction.Confidence} | Source: {extraction.Source}{warningText}";
    }

    private static string BuildCandidateText<T>(IReadOnlyList<DetectedFieldValue<T>> candidates, Func<T?, string> formatter)
    {
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        return "Detected values: " + string.Join(" | ", candidates.Select(candidate => $"{formatter(candidate.Value)} ({candidate.Source})"));
    }

    private static string FormatNullableInt(int? value)
        => value?.ToString() ?? "(missing)";

    private static string FormatNullableDate(DateTime? value)
        => value?.ToString("yyyy-MM-dd") ?? "(missing)";

    private IEnumerable<TextBlock> MetaBlocks()
    {
        yield return ReportNumberMetaTextBlock;
        yield return InspectionDateMetaTextBlock;
        yield return WeatherMetaTextBlock;
        yield return TemperatureMetaTextBlock;
        yield return LocationsMetaTextBlock;
        yield return InspectorsMetaTextBlock;
        yield return PersonnelMetaTextBlock;
        yield return DescriptionMetaTextBlock;
        yield return DrawingsMetaTextBlock;
        yield return ObservationsMetaTextBlock;
        yield return NewDiscrepanciesMetaTextBlock;
        yield return PreviousDiscrepanciesMetaTextBlock;
    }
}
