using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CEI.ReportGenerator.Core.Services;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportImporter.Core.Models;
using CEI.ReportImporter.Core.Services;
using Forms = System.Windows.Forms;

namespace CEI.ReportImporter.App;

public partial class MainWindow : Window
{
    private readonly HistoricalReportScanner _scanner = new(new HistoricalDocumentParser());
    private readonly HistoricalImportCommitEngine _commitEngine = new();
    private readonly HistoricalReviewValidator _reviewValidator = new();
    private readonly ObservableCollection<HistoricalReviewItem> _visibleResults = [];

    private HistoricalReviewSession? _currentReviewSession;
    private HistoricalReviewItem? _selectedReviewItem;
    private Project? _destinationProject;
    private CancellationTokenSource? _importCancellationSource;
    private bool _isLoadingSelection;
    private bool _isRefreshingCollection;
    private bool _isImporting;

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = _visibleResults;
        ReviewFilterComboBox.SelectedIndex = 0;
        DestinationProjectSummaryTextBlock.Text = "Choose an existing SPINgen project.";
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

    private void BrowseDestinationProjectButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select an existing SPINgen project folder."
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            LoadDestinationProject(dialog.SelectedPath, showErrors: true);
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_destinationProject is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Choose an existing SPINgen destination project before scanning.",
                "Destination Project Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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
                IncludeSubfolders = IncludeSubfoldersCheckBox.IsChecked == true,
                DestinationProjectFolder = _destinationProject.FolderPath,
                DestinationProjectName = _destinationProject.Name,
                DestinationProjectNumber = _destinationProject.Number
            }));

            _currentReviewSession = _commitEngine.CreateSession(session, _destinationProject);
            RefreshReviewCollection();
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

        CommitWorkingCopyEdits();
        RefreshReviewCollection(_selectedReviewItem?.SourceFilePath);
    }

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingCollection)
        {
            return;
        }

        var previousItem = _selectedReviewItem;
        var nextItem = ResultsGrid.SelectedItem as HistoricalReviewItem;
        if (previousItem is not null
            && previousItem != nextItem
            && !MatchesFilter(previousItem, GetSelectedFilter()))
        {
            RefreshReviewCollection(nextItem?.SourceFilePath);
            return;
        }

        LoadSelectedReviewItem();
    }

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is HistoricalReviewItem)
        {
            OpenSourceDocument();
        }
    }

    private void OpenSourceDocumentButton_Click(object sender, RoutedEventArgs e)
        => OpenSourceDocument();

    private void ResultsGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (_currentReviewSession is null)
        {
            return;
        }

        _currentReviewSession.RefreshSessionCounts();
        UpdateSummaryText();
        UpdateSelectionState();
    }

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
            UpdateSelectedItemPresentation(item);
            return;
        }

        RefreshSessionState(item.SourceFilePath);
    }

    private void ExcludeButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        item.MarkExcluded();
        RefreshSessionState(item.SourceFilePath);
    }

    private void ReturnToReviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        item.ReturnToReview();
        RefreshSessionState(item.SourceFilePath);
    }

    private void ResetChangesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
        {
            return;
        }

        item.ResetChanges();
        RefreshSessionState(item.SourceFilePath);
    }

    private void SelectAllReadyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentReviewSession is null)
        {
            return;
        }

        foreach (var item in _currentReviewSession.Items.Where(item => item.CanSelect))
        {
            item.IsSelected = true;
        }

        _currentReviewSession.RefreshSessionCounts();
        UpdateSummaryText();
        UpdateSelectionState();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentReviewSession is null)
        {
            return;
        }

        foreach (var item in _currentReviewSession.Items)
        {
            item.IsSelected = false;
        }

        _currentReviewSession.RefreshSessionCounts();
        UpdateSummaryText();
        UpdateSelectionState();
    }

    private async void ImportSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_destinationProject is null || _currentReviewSession is null)
        {
            return;
        }

        if (_isImporting)
        {
            _importCancellationSource?.Cancel();
            return;
        }

        CommitWorkingCopyEdits();
        RefreshSessionState(_selectedReviewItem?.SourceFilePath);
        if (_currentReviewSession.SelectedCount == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "Select one or more Ready reports before importing.",
                "Nothing Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            _importCancellationSource = new CancellationTokenSource();
            SetImportingState(true);

            var result = await Task.Run(() =>
                _commitEngine.ImportSelected(_currentReviewSession, _destinationProject, _importCancellationSource.Token));

            RefreshSessionState(_selectedReviewItem?.SourceFilePath);
            System.Windows.MessageBox.Show(
                this,
                BuildImportSummaryText(result),
                result.Cancelled ? "Import Cancelled" : "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                this,
                ex.Message,
                "Import Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _importCancellationSource?.Dispose();
            _importCancellationSource = null;
            SetImportingState(false);
        }
    }

    private void RefreshReviewCollection(string? preferredSelectionPath = null)
    {
        _visibleResults.Clear();

        if (_currentReviewSession is null)
        {
            _selectedReviewItem = null;
            ClearDetailPane();
            return;
        }

        var filter = GetSelectedFilter();
        foreach (var item in _currentReviewSession.Items.Where(item => MatchesFilter(item, filter)))
        {
            _visibleResults.Add(item);
        }

        _isRefreshingCollection = true;
        try
        {
            ResultsGrid.SelectedItem = preferredSelectionPath is null
                ? _visibleResults.FirstOrDefault()
                : _visibleResults.FirstOrDefault(result => result.SourceFilePath == preferredSelectionPath)
                    ?? _visibleResults.FirstOrDefault();
        }
        finally
        {
            _isRefreshingCollection = false;
        }

        LoadSelectedReviewItem();
    }

    private void LoadSelectedReviewItem()
    {
        _isLoadingSelection = true;
        try
        {
            if (ResultsGrid.SelectedItem is not HistoricalReviewItem item)
            {
                _selectedReviewItem = null;
                ClearDetailPane();
                return;
            }

            _selectedReviewItem = item;
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
            UpdateSummaryText();
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

        var previousReviewState = item.ReviewState;
        var previousHasUserChanges = item.HasUserChanges;
        var previousDisplayReportNumber = item.DisplayReportNumber;
        var previousDisplayDate = item.DisplayDate;

        item.UpdateWorkingRequest(updatedRequest);
        if (_currentReviewSession is not null && _destinationProject is not null)
        {
            _commitEngine.RefreshSession(_currentReviewSession, _destinationProject);
        }

        if (previousReviewState != item.ReviewState
            || previousHasUserChanges != item.HasUserChanges
            || previousDisplayReportNumber != item.DisplayReportNumber
            || previousDisplayDate != item.DisplayDate)
        {
            UpdateSelectedItemPresentation(item);
        }

        ResultsGrid.Items.Refresh();
        UpdateSelectionState();
        UpdateSummaryText();
    }

    private void UpdateSelectedItemPresentation(HistoricalReviewItem item)
    {
        if (!ReferenceEquals(_selectedReviewItem, item))
        {
            return;
        }

        SelectedReportSummaryTextBlock.Text =
            $"{item.SourceFileName} | Review: {item.ReviewStateText} | Import: {item.ImportStatusText} | Confidence: {item.OverallConfidence}";
        UpdateSelectionState();
        UpdateSummaryText();
    }

    private void UpdateSummaryText()
        => SummaryTextBlock.Text = _currentReviewSession is null
            ? "No scan has been run yet."
            : BuildSummaryText(_currentReviewSession);

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
        UpdateSummaryText();
    }

    private void UpdateSelectionState()
    {
        var item = ResultsGrid.SelectedItem as HistoricalReviewItem;
        var hasSelection = item is not null;
        var hasReadySelections = _currentReviewSession?.SelectedCount > 0;
        ScanButton.IsEnabled = !_isImporting;
        MarkReadyButton.IsEnabled = hasSelection && !_isImporting;
        ExcludeButton.IsEnabled = hasSelection && !_isImporting;
        ResetChangesButton.IsEnabled = hasSelection && !_isImporting;
        ReturnToReviewButton.IsEnabled = (item?.ReviewState is HistoricalReviewState.Ready or HistoricalReviewState.Excluded) && !_isImporting;
        OpenSourceDocumentButton.IsEnabled = hasSelection && !_isImporting;
        SelectAllReadyButton.IsEnabled = _currentReviewSession is not null && !_isImporting;
        ClearSelectionButton.IsEnabled = _currentReviewSession is not null && !_isImporting;
        ImportSelectedButton.IsEnabled = (_currentReviewSession is not null && !_isImporting && hasReadySelections)
                                         || _isImporting;
        ImportSelectedButton.Content = _isImporting ? "Cancel Import" : "Import Selected";
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
    {
        session.RefreshSessionCounts();
        return $"{session.TotalCount} scanned | {session.ReadyCount} ready | {session.SelectedCount} selected | {session.ImportedCount} imported | {session.DuplicateCount} duplicates | {session.ErrorCount} errors | {session.SkippedCount} skipped";
    }

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

    private void RefreshSessionState(string? preferredSelectionPath = null)
    {
        if (_currentReviewSession is null || _destinationProject is null)
        {
            return;
        }

        _commitEngine.RefreshSession(_currentReviewSession, _destinationProject);
        ResultsGrid.Items.Refresh();
        if (preferredSelectionPath is not null)
        {
            ResultsGrid.SelectedItem = _visibleResults.FirstOrDefault(result => result.SourceFilePath == preferredSelectionPath)
                ?? ResultsGrid.SelectedItem;
        }

        LoadSelectedReviewItem();
        UpdateSelectionState();
        UpdateSummaryText();
    }

    private void LoadDestinationProject(string folderPath, bool showErrors)
    {
        var project = ProjectStore.Load(folderPath);
        if (project is null)
        {
            _destinationProject = null;
            DestinationProjectTextBox.Text = string.Empty;
            DestinationProjectSummaryTextBlock.Text = "Choose an existing SPINgen project.";
            if (showErrors)
            {
                System.Windows.MessageBox.Show(
                    this,
                    "Select a valid existing SPINgen project folder.",
                    "Invalid Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            UpdateSelectionState();
            return;
        }

        _destinationProject = project;
        var reportCount = ReportStore.LoadAllReports(project).Reports.Count;
        DestinationProjectTextBox.Text = project.FolderPath;
        DestinationProjectSummaryTextBlock.Text =
            $"{project.Name} | Project #{project.Number} | {reportCount} current reports";

        if (_currentReviewSession is not null)
        {
            RefreshSessionState(_selectedReviewItem?.SourceFilePath);
        }

        UpdateSelectionState();
    }

    private void SetImportingState(bool isImporting)
    {
        _isImporting = isImporting;
        BrowseDestinationProjectButton.IsEnabled = !isImporting;
        OpenSourceFolderButton.IsEnabled = !isImporting;
        IncludeSubfoldersCheckBox.IsEnabled = !isImporting;
        ReviewFilterComboBox.IsEnabled = !isImporting;
        UpdateSelectionState();
    }

    private static string BuildImportSummaryText(HistoricalImportBatchResult result)
        => $"{result.ImportedCount} Imported{Environment.NewLine}"
           + $"{result.DuplicateCount} Duplicates{Environment.NewLine}"
           + $"{result.ErrorCount} Errors{Environment.NewLine}"
           + $"{result.SkippedCount} Skipped{Environment.NewLine}"
           + $"{result.Elapsed.TotalSeconds:F1}s elapsed";
}
