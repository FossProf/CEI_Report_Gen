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
    private bool _reportLoadWarningShown;
    private ReportStore.ReportLoadResult _currentLoadResult = new(Array.Empty<InspectionReport>(), Array.Empty<ReportStore.ReportLoadIssue>());
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
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void FilterControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyReportFilters();
    }

    private void DateFilter_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyReportFilters();
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
        ApplyReportFilters();
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
        ValidateProjectConfiguration();
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
        ApplyReportFilters(preserveCurrentResultsWhenInvalid: false);
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
    }

    private void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyReportFilters();
    }

    private void ApplyReportFilters(bool preserveCurrentResultsWhenInvalid = true)
    {
        var previousSelectionNumber = GetSelectedReportItem()?.Report.Number;
        var criteria = BuildCurrentSearchCriteria();
        if (!ReportSearchService.TryValidateCriteria(criteria, out var validationMessage))
        {
            FilterValidationText.Text = validationMessage;
            FilterValidationText.Visibility = Visibility.Visible;
            if (preserveCurrentResultsWhenInvalid)
            {
                return;
            }

            criteria = new ReportSearchCriteria
            {
                SearchText = SearchReportsTextBox.Text,
                Status = GetSelectedStatus(),
                Weather = GetSelectedWeather()
            };
        }
        else
        {
            FilterValidationText.Visibility = Visibility.Collapsed;
        }

        var filteredReports = ReportSearchService.Filter(
            _currentLoadResult.Reports.OrderByDescending(r => r.Number),
            criteria);
        var items = filteredReports
            .Select(r => new ReportListItem(_project, r))
            .ToList();

        ReportsGrid.ItemsSource = items;
        UpdateResultCountText(filteredReports.Count, _currentLoadResult.Reports.Count);
        UpdateEmptyState(filteredReports.Count);
        RestoreSelection(previousSelectionNumber);
        UpdateReportSelectionState();
    }

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
        FilteredResultsText.Text = filteredCount == 0
            ? "No matching reports"
            : $"Showing {filteredCount} of {totalCount} reports";
    }

    private void UpdateEmptyState(int filteredCount)
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

    private void RefreshReadinessPanel()
    {
        var readiness = _dashboardSummary.Readiness;
        ApplyReadinessText(TemplateReadinessIcon, TemplateReadinessText, "Template", readiness.TemplateReady, readiness.TemplateIssues, "Attention");
        ApplyReadinessText(InspectorReadinessIcon, InspectorReadinessText, "Inspector Signature", readiness.InspectorSignatureReady, readiness.InspectorSignatureIssues, "Missing/Invalid");
        ApplyReadinessText(ProjectManagerReadinessIcon, ProjectManagerReadinessText, "Project Manager Signature", readiness.ProjectManagerSignatureReady, readiness.ProjectManagerSignatureIssues, "Missing/Invalid");
        ApplyReadinessText(ConfigurationReadinessIcon, ConfigurationReadinessText, "Project Configuration", readiness.ProjectConfigurationReady, readiness.ProjectConfigurationIssues, "Attention");
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

    private void ApplyReadinessText(System.Windows.Shapes.Path iconTarget, TextBlock target, string label, bool isReady, IReadOnlyList<string> issues, string notReadyText)
    {
        target.Text = isReady ? $"{label}: Ready" : $"{label}: {notReadyText}";
        iconTarget.Data = (Geometry)FindResource(isReady ? "IconCheckCircleGeometry" : "IconExclamationTriangleGeometry");
        iconTarget.Fill = (Brush)FindResource(isReady ? "StatusReadyBrush" : "StatusWarningBrush");
        ToolTipService.SetToolTip(target, issues.Count == 0 ? null : string.Join(Environment.NewLine, issues));
        ToolTipService.SetToolTip(iconTarget, issues.Count == 0 ? null : string.Join(Environment.NewLine, issues));
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

    private void ValidateProjectConfiguration()
    {
        var readiness = _dashboardSummary.Readiness;
        if (readiness.IsReady)
        {
            var message = "Project configuration is valid." + Environment.NewLine + Environment.NewLine +
                          "Template: Ready" + Environment.NewLine +
                          "Inspector Signature: Ready" + Environment.NewLine +
                          "Project Manager Signature: Ready";

            MessageBox.Show(
                this,
                message,
                "Project Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var lines = new List<string>
        {
            readiness.TemplateReady ? "Template: Ready" : "Template: Attention",
            readiness.InspectorSignatureReady ? "Inspector Signature: Ready" : "Inspector Signature: Missing/Invalid",
            readiness.ProjectManagerSignatureReady ? "Project Manager Signature: Ready" : "Project Manager Signature: Missing/Invalid",
            readiness.ProjectConfigurationReady ? "Project Configuration: Ready" : "Project Configuration: Attention"
        };

        if (readiness.Issues.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(readiness.Issues);
        }

        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, lines),
            "Project Validation",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
        OpenReportButton.IsEnabled = hasSelection;
        ReportFolderButton.IsEnabled = hasSelection;
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

    public sealed record ReportListItem(Project Project, InspectionReport Report)
    {
        public string Number => ProjectLayout.FormatReportNumber(Report.Number);
        public string DateText => Report.Date.ToString("MMMM d, yyyy");
        public string Status => Report.Status == ReportStatus.Final ? "Final" : "Draft";
        public string FileName => string.IsNullOrWhiteSpace(Report.OutputFileName)
            ? ProjectLayout.DefaultReportFileName(Project, Report)
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
}
