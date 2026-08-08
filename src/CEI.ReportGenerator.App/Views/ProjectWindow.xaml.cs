using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CEI.ReportGenerator.App;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using ProjectValidation = CEI.ReportGenerator.Core.Services.Validation;

namespace CEI.ReportGenerator.App.Views;

public partial class ProjectWindow : Window
{
    private readonly Project _project;
    private bool _reportLoadWarningShown;

    public ProjectWindow(Project project)
    {
        InitializeComponent();
        _project = project;
        RefreshProjectHeader();
        RefreshReports();
        UpdateReportSelectionState();
    }

    private void RefreshProjectHeader()
    {
        ProjectTitle.Text = _project.Name;
        ProjectDetails.Text =
            $"#{_project.Number}  •  Owner: {_project.Owner}  •  Contract Manager: {_project.ContractManager}  •  General Contractor: {_project.GeneralContractor}";
        Title = $"{_project.Name} - CEI Report Generator";
    }

    private void RefreshReports()
    {
        var loadResult = ReportStore.LoadAllReports(_project);
        ReportsGrid.ItemsSource = loadResult.Reports
            .OrderByDescending(r => r.Number)
            .Select(r => new ReportListItem(r))
            .ToList();

        if (loadResult.Issues.Count > 0 && !_reportLoadWarningShown)
        {
            _reportLoadWarningShown = true;
            var lines = loadResult.Issues.Take(3)
                .Select(i => $"{i.Path}: {i.Message}");
            MessageBox.Show(
                this,
                "One or more saved reports could not be loaded:" + Environment.NewLine + string.Join(Environment.NewLine, lines),
                "Report Load Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

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
        }
    }

    private void ReportsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateReportSelectionState();
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

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            AppInfo.GetAboutText(),
            "About",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void CreateNewReport()
    {
        var nextNumber = ProjectStore.SynchronizeNextReportNumber(_project);
        var report = new InspectionReport
        {
            Number = nextNumber,
            Date = DateTime.Today
        };

        var editor = new ReportEditorWindow(_project, report, isNew: true)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true)
        {
            RefreshReports();
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
            RefreshReports();
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
            RefreshProjectHeader();
            UpdateReportSelectionState();
        }
    }

    private void ValidateProjectConfiguration()
    {
        var errors = ProjectValidation.ValidateProject(_project);
        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, errors),
                "Project Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var message = "Project configuration is valid." + Environment.NewLine + Environment.NewLine +
                      "Template: OK" + Environment.NewLine +
                      "Inspector Signature: OK" + Environment.NewLine +
                      "Project Manager Signature: OK" + Environment.NewLine +
                      "Project Folder: OK";

        MessageBox.Show(
            this,
            message,
            "Project Validation",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
    }

    private void CloseProject()
    {
        Owner?.Activate();
        Close();
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

    public sealed record ReportListItem(InspectionReport Report)
    {
        public string Number => ProjectLayout.FormatReportNumber(Report.Number);
        public string DateText => Report.Date.ToString("MMMM d, yyyy");
        public string Status => Report.Status == ReportStatus.Final ? "Final" : "Draft";
        public string FileName => string.IsNullOrWhiteSpace(Report.OutputFileName)
            ? ProjectLayout.DefaultReportFileName(Report.Number)
            : Report.OutputFileName;
    }
}
