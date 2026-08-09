using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.Views;

public partial class ProjectWindow : Window
{
    private const string AllFilterOption = "All";

    private readonly Project _project;
    private readonly DispatcherTimer _searchDebounceTimer;
    private ProjectWorkspace _currentWorkspace = ProjectWorkspace.Reports;
    private bool _reportLoadWarningShown;
    private ReportStore.ReportLoadResult _currentLoadResult = new(Array.Empty<InspectionReport>(), Array.Empty<ReportStore.ReportLoadIssue>());
    private IReadOnlyList<InspectionReport> _lastValidSearchResults = Array.Empty<InspectionReport>();
    private ProjectDashboardSummary _dashboardSummary = new()
    {
        ProjectName = string.Empty,
        ProjectNumber = string.Empty,
        Owner = string.Empty,
        ContractManager = string.Empty,
        GeneralContractor = string.Empty,
        NextReportNumber = 1,
        TotalReports = 0,
        FinalReports = 0,
        DraftReports = 0,
        LoadIssueCount = 0,
        Readiness = new ProjectReadiness()
    };

    public ProjectWindow(Project project)
    {
        InitializeComponent();
        _project = project;
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
        InitializeSearchControls();
        RefreshDashboard();
        UpdateReportSelectionState();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            CreateNewReport();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D1)
        {
            SetWorkspace(ProjectWorkspace.Reports);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D2)
        {
            SetWorkspace(ProjectWorkspace.Search);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            OpenProjectSettings();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.OemComma)
        {
            OpenApplicationSettings();
            e.Handled = true;
        }
    }

    private void ReportsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateReportSelectionState();
    }

    private void SearchReportsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSearchWatermark();
        MatchColumn.Visibility = ShouldShowMatchColumn() ? Visibility.Visible : Visibility.Collapsed;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchReportsTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_currentWorkspace != ProjectWorkspace.Search)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            HandleSearchEnterKey();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            _searchDebounceTimer.Stop();
            SearchReportsTextBox.Text = string.Empty;
            RefreshSearchWatermark();
            ApplyReportFilters();
            SearchReportsTextBox.Focus();
            SearchReportsTextBox.CaretIndex = 0;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            FocusGridFirstResult();
            e.Handled = true;
        }
    }

    private void FilterControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (_currentWorkspace == ProjectWorkspace.Search)
        {
            ApplyReportFilters();
        }
    }

    private void DateFilter_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (_currentWorkspace == ProjectWorkspace.Search)
        {
            ApplyReportFilters();
        }
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        SearchReportsTextBox.Text = string.Empty;
        StatusFilterComboBox.SelectedIndex = 0;
        WeatherFilterComboBox.SelectedIndex = 0;
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        _searchDebounceTimer.Stop();
        RefreshSearchWatermark();
        if (_currentWorkspace == ProjectWorkspace.Search)
        {
            ApplyReportFilters();
        }
    }

    private void ReportsWorkspaceToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SetWorkspace(ProjectWorkspace.Reports);
    }

    private void SearchWorkspaceToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SetWorkspace(ProjectWorkspace.Search);
    }

    private void NewReportButton_Click(object sender, RoutedEventArgs e)
    {
        CreateNewReport();
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReport();
    }

    private void ReportsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedReport();
    }

    private void ReportsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: ReportListItem item })
        {
            ReportsGrid.SelectedItem = item;
        }
    }

    private void ReportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReportFolder();
    }

    private void EditProjectButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectSettings();
    }

    private void ProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectFolder();
    }

    private void NewReportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CreateNewReport();
    }

    private void OpenProjectFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectFolder();
    }

    private void CloseProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CloseProject();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void ProjectSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectSettings();
    }

    private void ValidateProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectReadinessDetails();
    }

    private void OpenReportsFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenReportsFolder();
    }

    private void OpenSignaturesFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSignaturesFolder();
    }

    private void OpenSelectedReportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReport();
    }

    private void OpenSelectedReportFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReportFolder();
    }

    private void NewReportFromSelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CreateNewReportFromSelected();
    }

    private void NewReportFromThisReportContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CreateNewReportFromSelected();
    }

    private void OpenThisReportContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReport();
    }

    private void OpenThisReportFolderContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReportFolder();
    }

    private void DeleteThisReportContextMenuItem_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedReport();
    }

    private void DeleteSelectedReportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        DeleteSelectedReport();
    }

    private void ApplicationSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenApplicationSettings();
    }

    private void ReadinessIndicatorButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectReadinessDetails();
    }

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            AppInfo.GetAboutText(),
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshDashboard()
    {
        _currentLoadResult = ReportStore.LoadAllReports(_project);
        _dashboardSummary = ProjectDashboardSummaryBuilder.Build(_project, _currentLoadResult);

        RefreshProjectHeader();
        RefreshReadinessPanel();
        RefreshReportCounts();
        RefreshStatusBar();
        RefreshReportLoadWarning();
        ApplyWorkspace(preserveCurrentResultsWhenInvalid: false);
    }

    private void RefreshProjectHeader()
    {
        ProjectTitle.Text = _dashboardSummary.ProjectName;
        ProjectNumberText.Text = $"Project #{_dashboardSummary.ProjectNumber}";
        OwnerText.Text = $"Owner: {_dashboardSummary.Owner}";
        ContractManagerText.Text = $"Contract Manager: {_dashboardSummary.ContractManager}";
        GeneralContractorText.Text = $"General Contractor: {_dashboardSummary.GeneralContractor}";
        Title = $"{_dashboardSummary.ProjectName} - {AppIdentity.CurrentName}";
    }

    private void RefreshReportCounts()
    {
        NextReportText.Text = _dashboardSummary.NextReportNumberText;
        TotalReportsText.Text = _dashboardSummary.TotalReports.ToString();
        FinalReportsText.Text = _dashboardSummary.FinalReports.ToString();
        DraftReportsText.Text = _dashboardSummary.DraftReports.ToString();
        if (_dashboardSummary.HasLoadIssues)
        {
            LoadIssuesText.Text = _dashboardSummary.LoadIssueMessage;
            LoadIssuesText.Visibility = Visibility.Visible;
        }
        else
        {
            LoadIssuesText.Visibility = Visibility.Collapsed;
        }
    }

    private void InitializeSearchControls()
    {
        StatusFilterComboBox.ItemsSource = new[]
        {
            FilterOption.ForStatus(AllFilterOption, null),
            FilterOption.ForStatus("Draft", ReportStatus.Draft),
            FilterOption.ForStatus("Final", ReportStatus.Final)
        };
        StatusFilterComboBox.SelectedIndex = 0;

        WeatherFilterComboBox.ItemsSource = new[] { FilterOption.ForWeather(AllFilterOption, null) }
            .Concat(WeatherOptions.All.Select(weather => FilterOption.ForWeather(weather, weather)))
            .ToList();
        WeatherFilterComboBox.SelectedIndex = 0;

        RefreshSearchWatermark();
        ReportsWorkspaceToggleButton.IsChecked = true;
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        if (_currentWorkspace == ProjectWorkspace.Search)
        {
            ApplyReportFilters();
        }
    }

    private void ApplyReportFilters(bool preserveCurrentResultsWhenInvalid = true)
    {
        if (_currentWorkspace != ProjectWorkspace.Search)
        {
            return;
        }

        var previousSelectionNumber = GetSelectedReportItem()?.Report.Number;
        var criteria = BuildCurrentSearchCriteria();
        if (!ReportSearchService.TryValidateCriteria(criteria, out var validationMessage))
        {
            FilterValidationText.Text = validationMessage;
            FilterValidationText.Visibility = Visibility.Visible;
            if (preserveCurrentResultsWhenInvalid && _lastValidSearchResults.Count > 0)
            {
                BindSearchWorkspaceItems(_lastValidSearchResults, previousSelectionNumber);
                UpdateSearchWorkspaceEmptyState(_lastValidSearchResults.Count);
                return;
            }

            var fallbackReports = _lastValidSearchResults.Count > 0
                ? _lastValidSearchResults
                : _currentLoadResult.Reports.OrderByDescending(r => r.Number).ToList();
            BindSearchWorkspaceItems(fallbackReports, previousSelectionNumber);
            UpdateSearchWorkspaceEmptyState(fallbackReports.Count);
            return;
        }
        else
        {
            FilterValidationText.Visibility = Visibility.Collapsed;
        }

        var filteredReports = ReportSearchService.Filter(
            _currentLoadResult.Reports.OrderByDescending(r => r.Number),
            criteria);
        _lastValidSearchResults = filteredReports;
        BindSearchWorkspaceItems(filteredReports, previousSelectionNumber);
        UpdateSearchWorkspaceEmptyState(filteredReports.Count);
    }

    private void BindSearchWorkspaceItems(IEnumerable<InspectionReport> reports, int? previousSelectionNumber)
    {
        var reportList = reports.ToList();
        if (ShouldShowMatchColumn())
        {
            BindSearchResultItems(BuildSearchResults(reportList), previousSelectionNumber);
            return;
        }

        BindReportItems(reportList, previousSelectionNumber);
    }

    private IReadOnlyList<ReportSearchResult> BuildSearchResults(IEnumerable<InspectionReport> reports)
        => reports
            .Select(report => ReportMatchSnippetBuilder.Build(report, SearchReportsTextBox.Text))
            .Where(result => result is not null)
            .Cast<ReportSearchResult>()
            .ToList();

    private ReportSearchCriteria BuildCurrentSearchCriteria()
        => new()
        {
            SearchText = SearchReportsTextBox.Text,
            Status = GetSelectedStatus(),
            Weather = GetSelectedWeather(),
            FromDate = FromDatePicker.SelectedDate,
            ToDate = ToDatePicker.SelectedDate
        };

    private ReportStatus? GetSelectedStatus()
        => StatusFilterComboBox.SelectedItem is FilterOption { StatusValue: { } status }
            ? status
            : null;

    private string? GetSelectedWeather()
        => WeatherFilterComboBox.SelectedItem is FilterOption { WeatherValue: { } weather }
            ? weather
            : null;

    private void UpdateResultCountText(int filteredCount, int totalCount)
    {
        if (_currentWorkspace != ProjectWorkspace.Search)
        {
            FilteredResultsText.Text = string.Empty;
            return;
        }

        var criteria = BuildCurrentSearchCriteria();
        if (!HasAnySearchCriteria(criteria))
        {
            FilteredResultsText.Text = $"{totalCount} reports";
            return;
        }

        FilteredResultsText.Text = filteredCount == 0
            ? "No matching reports"
            : $"{filteredCount} of {totalCount} reports";
    }

    private void UpdateSearchWorkspaceEmptyState(int filteredCount)
    {
        if (_dashboardSummary.TotalReports == 0)
        {
            EmptyReportsText.Text = "No reports have been created for this project.";
            EmptyReportsText.Visibility = Visibility.Visible;
            return;
        }

        if (filteredCount == 0)
        {
            EmptyReportsText.Text = "No reports match the current search and filters.";
            EmptyReportsText.Visibility = Visibility.Visible;
            return;
        }

        EmptyReportsText.Visibility = Visibility.Collapsed;
    }

    private void UpdateReportsWorkspaceEmptyState()
    {
        if (_dashboardSummary.TotalReports == 0)
        {
            EmptyReportsText.Text = "No reports have been created for this project.";
            EmptyReportsText.Visibility = Visibility.Visible;
            return;
        }

        EmptyReportsText.Visibility = Visibility.Collapsed;
    }

    private void RestoreSelection(int? previousSelectionNumber)
    {
        if (!previousSelectionNumber.HasValue)
        {
            ReportsGrid.SelectedItem = null;
            return;
        }

        var matchingItem = (ReportsGrid.ItemsSource as IEnumerable<ReportListItem>)
            ?.FirstOrDefault(item => item.Report.Number == previousSelectionNumber.Value);
        ReportsGrid.SelectedItem = matchingItem;
    }

    private void RefreshSearchWatermark()
    {
        SearchWatermarkText.Visibility = string.IsNullOrWhiteSpace(SearchReportsTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyWorkspace(bool preserveCurrentResultsWhenInvalid = true)
    {
        ReportsWorkspaceToggleButton.IsChecked = _currentWorkspace == ProjectWorkspace.Reports;
        SearchWorkspaceToggleButton.IsChecked = _currentWorkspace == ProjectWorkspace.Search;
        ReportsWorkspaceToolbar.Visibility = _currentWorkspace == ProjectWorkspace.Reports ? Visibility.Visible : Visibility.Collapsed;
        SearchWorkspaceToolbar.Visibility = _currentWorkspace == ProjectWorkspace.Search ? Visibility.Visible : Visibility.Collapsed;

        FilteredResultsText.Visibility = _currentWorkspace == ProjectWorkspace.Search ? Visibility.Visible : Visibility.Collapsed;
        FilterValidationText.Visibility = _currentWorkspace == ProjectWorkspace.Search && FilterValidationText.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        MatchColumn.Visibility = ShouldShowMatchColumn() ? Visibility.Visible : Visibility.Collapsed;

        if (_currentWorkspace == ProjectWorkspace.Reports)
        {
            BindReportItems(_currentLoadResult.Reports.OrderByDescending(r => r.Number), GetSelectedReportItem()?.Report.Number);
            UpdateReportsWorkspaceEmptyState();
            Dispatcher.BeginInvoke(() => ReportsGrid.Focus(), DispatcherPriority.Input);
            return;
        }

        ApplyReportFilters(preserveCurrentResultsWhenInvalid);
        Dispatcher.BeginInvoke(() => SearchReportsTextBox.Focus(), DispatcherPriority.Input);
    }

    private void SetWorkspace(ProjectWorkspace workspace)
    {
        if (_currentWorkspace == workspace)
        {
            ApplyWorkspace();
            return;
        }

        _currentWorkspace = workspace;
        ApplyWorkspace();
    }

    private void BindReportItems(IEnumerable<InspectionReport> reports, int? previousSelectionNumber)
    {
        BindReportItems(
            reports.Select(r => new ReportListItem(_project, r, null)).ToList(),
            previousSelectionNumber);
    }

    private void BindSearchResultItems(IEnumerable<ReportSearchResult> searchResults, int? previousSelectionNumber)
    {
        BindReportItems(
            searchResults.Select(result => new ReportListItem(_project, result.Report, result)).ToList(),
            previousSelectionNumber);
    }

    private void BindReportItems(IReadOnlyList<ReportListItem> items, int? previousSelectionNumber)
    {
        ReportsGrid.ItemsSource = items;
        RestoreSelection(previousSelectionNumber);
        UpdateReportSelectionState();
    }

    private bool ShouldShowMatchColumn()
        => _currentWorkspace == ProjectWorkspace.Search
           && !string.IsNullOrWhiteSpace(SearchReportsTextBox.Text);

    private static bool HasAnySearchCriteria(ReportSearchCriteria criteria)
        => !string.IsNullOrWhiteSpace(criteria.SearchText)
           || criteria.Status.HasValue
           || criteria.FromDate.HasValue
           || criteria.ToDate.HasValue
           || !string.IsNullOrWhiteSpace(criteria.Weather);

    private void HandleSearchEnterKey()
    {
        var visibleItems = (ReportsGrid.ItemsSource as IEnumerable<ReportListItem>)?.ToList() ?? new List<ReportListItem>();
        if (visibleItems.Count == 1)
        {
            ReportsGrid.SelectedItem = visibleItems[0];
            OpenSelectedReport();
            return;
        }

        FocusGridFirstResult();
    }

    private void FocusGridFirstResult()
    {
        if ((ReportsGrid.ItemsSource as IEnumerable<ReportListItem>)?.FirstOrDefault() is not { } firstItem)
        {
            return;
        }

        ReportsGrid.SelectedItem = firstItem;
        ReportsGrid.Focus();
    }

    private void RefreshReadinessPanel()
    {
        var readiness = _dashboardSummary.Readiness;
        var presentation = BuildReadinessPresentation(readiness);
        ReadinessIndicatorIcon.Data = (Geometry)FindResource(presentation.IconResourceKey);
        ReadinessIndicatorIcon.Fill = (Brush)FindResource(presentation.IconBrushResourceKey);
        ReadinessIndicatorText.Text = presentation.StatusText;
        ReadinessIndicatorButton.ToolTip = presentation.TooltipText;
        ReadinessIndicatorButton.Background = (Brush)FindResource(presentation.BackgroundBrushResourceKey);
        ReadinessIndicatorButton.BorderBrush = (Brush)FindResource(presentation.BorderBrushResourceKey);
        ReadinessIndicatorButton.Foreground = (Brush)FindResource(presentation.ForegroundBrushResourceKey);
    }

    private void RefreshStatusBar()
    {
        StatusBarProjectText.Text = $"Project: {_dashboardSummary.ProjectName}";
        StatusBarNextReportText.Text = $"Next Report: {_dashboardSummary.NextReportNumberText}";
        StatusBarReadinessText.Text = $"Status: {_dashboardSummary.StatusText}";
        StatusBarReadinessIndicator.Fill = _dashboardSummary.Readiness.IsReady
            ? (Brush)FindResource("StatusReadyBrush")
            : (Brush)FindResource("StatusWarningBrush");
    }

    private void RefreshReportLoadWarning()
    {
        if (_currentLoadResult.Issues.Count == 0 || _reportLoadWarningShown)
        {
            return;
        }

        _reportLoadWarningShown = true;
        var lines = _currentLoadResult.Issues.Take(3)
            .Select(i => $"{i.Path}: {i.Message}");
        MessageBox.Show(
            this,
            "One or more saved reports could not be loaded:" + Environment.NewLine + string.Join(Environment.NewLine, lines),
            "Report Load Warning",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void CreateNewReport()
    {
        var report = ReportDraftFactory.CreateBlank(_project);

        var editor = new ReportEditorWindow(_project, report, isNew: true)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            RefreshDashboard();
        }
    }

    private void CreateNewReportFromSelected()
    {
        if (GetSelectedReportItem() is not { } item)
        {
            return;
        }

        var report = ReportDraftFactory.CreateFromExisting(_project, item.Report);
        var confirmation = new NewReportFromExistingConfirmationWindow(item.Report, report)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        var editor = new ReportEditorWindow(_project, report, isNew: true)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            RefreshDashboard();
        }
    }

    private ReportListItem? GetSelectedReportItem()
        => ReportsGrid.SelectedItem as ReportListItem;

    private void OpenSelectedReport()
    {
        if (GetSelectedReportItem() is not { } item)
        {
            return;
        }

        var editor = new ReportEditorWindow(_project, item.Report, isNew: false)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            RefreshDashboard();
        }
    }

    private void OpenSelectedReportFolder()
    {
        if (GetSelectedReportItem() is not { } item)
        {
            MessageBox.Show(
                this,
                "Select a report first.",
                "Open Report Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var folder = ProjectLayout.ReportFolder(_project, item.Report.Number);
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(
                this,
                "The selected report folder does not exist yet.",
                "Open Report Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        OpenInExplorer(folder);
    }

    private void DeleteSelectedReport()
    {
        if (GetSelectedReportItem() is not { } item)
        {
            MessageBox.Show(
                this,
                "Select a report first.",
                "Delete Report",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var reportNumber = item.Report.Number.ToString();
        var message = item.Report.Status == ReportStatus.Final
            ? $"Delete FINAL Report #{reportNumber}?{Environment.NewLine}{Environment.NewLine}This report has been finalized.{Environment.NewLine}{Environment.NewLine}Deleting it will permanently remove the finalized Word document and its saved report data.{Environment.NewLine}{Environment.NewLine}This action cannot be undone."
            : $"Delete Report #{reportNumber}?{Environment.NewLine}{Environment.NewLine}This will permanently delete:{Environment.NewLine}- report data{Environment.NewLine}- generated Word report{Environment.NewLine}- saved photos for this report{Environment.NewLine}- temporary and preview files{Environment.NewLine}{Environment.NewLine}This action cannot be undone.";
        var result = MessageBox.Show(
            this,
            message,
            "Delete Report",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var deleteStatus = ReportStore.DeleteReport(_project, item.Report.Number);
            if (deleteStatus == ReportStore.DeleteReportStatus.NotFound)
            {
                MessageBox.Show(
                    this,
                    "The selected report folder no longer exists.",
                    "Delete Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (deleteStatus == ReportStore.DeleteReportStatus.InUse)
            {
                MessageBox.Show(
                    this,
                    "Report could not be deleted because one or more files are currently in use. Close the report in Word and try again.",
                    "Delete Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            RefreshDashboard();
        }
        catch (IOException)
        {
            MessageBox.Show(
                this,
                "Report could not be deleted because one or more files are currently in use. Close the report in Word and try again.",
                "Delete Report",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "Report could not be deleted because one or more files are currently in use. Close the report in Word and try again.",
                "Delete Report",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Delete Report",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenProjectFolder()
    {
        Directory.CreateDirectory(_project.FolderPath);
        OpenInExplorer(_project.FolderPath);
    }

    private void OpenProjectSettings()
    {
        var setup = new ProjectSetupWindow(_project)
        {
            Owner = this
        };
        if (setup.ShowDialog() == true)
        {
            RefreshDashboard();
        }
    }

    private void OpenProjectReadinessDetails()
    {
        var readiness = _dashboardSummary.Readiness;
        var details = BuildReadinessDetailsText(readiness, includeTitle: false);

        MessageBox.Show(
            this,
            details,
            "Project Validation",
            MessageBoxButton.OK,
            readiness.IsReady ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void OpenReportsFolder()
    {
        var folder = ProjectLayout.ReportsFolder(_project);
        Directory.CreateDirectory(folder);
        OpenInExplorer(folder);
    }

    private void OpenSignaturesFolder()
    {
        var folder = ProjectLayout.SignaturesFolder(_project);
        Directory.CreateDirectory(folder);
        OpenInExplorer(folder);
    }

    private void UpdateReportSelectionState()
    {
        var hasSelection = GetSelectedReportItem() is not null;
        WorkspaceOpenReportButton.IsEnabled = hasSelection;
        WorkspaceOpenFolderButton.IsEnabled = hasSelection;
        WorkspaceNewFromSelectedButton.IsEnabled = hasSelection;
        WorkspaceDeleteReportButton.IsEnabled = hasSelection;
        OpenSelectedReportMenuItem.IsEnabled = hasSelection;
        OpenSelectedReportFolderMenuItem.IsEnabled = hasSelection;
        NewReportFromSelectedMenuItem.IsEnabled = hasSelection;
        OpenThisReportContextMenuItem.IsEnabled = hasSelection;
        OpenThisReportFolderContextMenuItem.IsEnabled = hasSelection;
        NewReportFromThisReportContextMenuItem.IsEnabled = hasSelection;
        DeleteSelectedReportMenuItem.IsEnabled = hasSelection;
        DeleteThisReportContextMenuItem.IsEnabled = hasSelection;
    }

    private void CloseProject()
    {
        Owner?.Activate();
        Close();
    }

    private void OpenApplicationSettings()
    {
        var app = App.CurrentApp;
        var window = new ApplicationSettingsWindow(app.Settings)
        {
            Owner = this
        };
        if (window.ShowDialog() == true && window.SavedSettings is not null)
        {
            app.ApplySettings(window.SavedSettings);
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open the folder.{Environment.NewLine}{ex.Message}",
                "Open Folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static ReadinessPresentation BuildReadinessPresentation(ProjectReadiness readiness)
    {
        var issueCategoryCount = CountFailedReadinessCategories(readiness);
        var isReady = readiness.IsReady;
        var statusText = isReady
            ? "Project Ready"
            : issueCategoryCount == 1
                ? "1 Setup Issue"
                : $"{issueCategoryCount} Setup Issues";

        return new ReadinessPresentation(
            statusText,
            BuildReadinessDetailsText(readiness, includeTitle: true),
            isReady ? "IconCheckCircleGeometry" : "IconExclamationTriangleGeometry",
            isReady ? "StatusReadyBrush" : "StatusWarningBrush",
            isReady ? "HoverBrush" : "DangerTintBrush",
            isReady ? "StatusReadyBrush" : "StatusWarningBrush",
            isReady ? "PrimaryTextBrush" : "PrimaryTextBrush");
    }

    private static string BuildReadinessDetailsText(ProjectReadiness readiness, bool includeTitle)
    {
        var lines = new List<string>();
        if (includeTitle)
        {
            lines.Add("Project Readiness");
            lines.Add(string.Empty);
        }

        lines.Add(ReadinessLine(readiness.TemplateReady, "Template", "Attention"));
        lines.Add(ReadinessLine(readiness.InspectorSignatureReady, "Inspector Signature", "Missing/Invalid"));
        lines.Add(ReadinessLine(readiness.ProjectManagerSignatureReady, "Project Manager Signature", "Missing/Invalid"));
        lines.Add(ReadinessLine(readiness.ProjectConfigurationReady, "Project Configuration", "Attention"));

        if (readiness.Issues.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(readiness.Issues);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ReadinessLine(bool isReady, string label, string notReadyText)
        => isReady ? $"✓ {label}: Ready" : $"⚠ {label}: {notReadyText}";

    private static int CountFailedReadinessCategories(ProjectReadiness readiness)
    {
        var count = 0;
        if (!readiness.TemplateReady)
        {
            count++;
        }

        if (!readiness.InspectorSignatureReady)
        {
            count++;
        }

        if (!readiness.ProjectManagerSignatureReady)
        {
            count++;
        }

        if (!readiness.ProjectConfigurationReady)
        {
            count++;
        }

        return count;
    }

    public sealed record ReportListItem(Project Project, InspectionReport Report, ReportSearchResult? SearchResult)
    {
        public string Number => ProjectLayout.FormatReportNumber(Report.Number);
        public string DateText => Report.Date.ToString("MMMM d, yyyy");
        public string Status => Report.Status == ReportStatus.Final ? "Final" : "Draft";
        public string MatchDisplay => SearchResult?.MatchDisplay ?? string.Empty;
        public string MatchTooltip => SearchResult is null
            ? string.Empty
            : $"{SearchResult.MatchField}{Environment.NewLine}{Environment.NewLine}{SearchResult.MatchFullText}";
        public string FileName => string.IsNullOrWhiteSpace(Report.OutputFileName)
            ? Report.Status == ReportStatus.Final
                ? "No local DOCX stored"
                : ProjectLayout.DefaultReportFileName(Project, Report)
            : Report.OutputFileName;
        public int PhotosCount => Report.Photos.Count;
    }

    private sealed record FilterOption(string Label, ReportStatus? StatusValue, string? WeatherValue)
    {
        public static FilterOption ForStatus(string label, ReportStatus? status)
            => new(label, status, null);

        public static FilterOption ForWeather(string label, string? weather)
            => new(label, null, weather);
    }

    private enum ProjectWorkspace
    {
        Reports,
        Search
    }

    private sealed record ReadinessPresentation(
        string StatusText,
        string TooltipText,
        string IconResourceKey,
        string IconBrushResourceKey,
        string BackgroundBrushResourceKey,
        string BorderBrushResourceKey,
        string ForegroundBrushResourceKey);
}
