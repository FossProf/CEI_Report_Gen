using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App.Views;

public partial class ProjectWindow : Window
{
    private readonly Project _project;

    public ProjectWindow(Project project)
    {
        InitializeComponent();
        _project = project;
        ProjectTitle.Text = project.Name;
        ProjectDetails.Text =
            $"#{project.Number}  •  Owner: {project.Owner}  •  Contract Manager: {project.ContractManager}  •  General Contractor: {project.GeneralContractor}";
        RefreshReports();
    }

    private void RefreshReports()
    {
        ReportsGrid.ItemsSource = ReportStore.LoadAllReports(_project)
            .OrderByDescending(r => r.Number)
            .Select(r => new ReportListItem(r)).ToList();
    }

    private void NewReportButton_Click(object sender, RoutedEventArgs e)
    {
        var report = new InspectionReport
        {
            Number = _project.NextReportNumber,
            Date = DateTime.Today
        };
        var editor = new ReportEditorWindow(_project, report, isNew: true);
        editor.Owner = this;
        if (editor.ShowDialog() == true)
        {
            RefreshReports();
        }
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedReport();
    }

    private void ReportsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedReport();
    }

    private void OpenSelectedReport()
    {
        if (ReportsGrid.SelectedItem is not ReportListItem item)
        {
            return;
        }

        var editor = new ReportEditorWindow(_project, item.Report, isNew: false);
        editor.Owner = this;
        if (editor.ShowDialog() == true)
        {
            RefreshReports();
        }
    }

    private void ReportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ReportsGrid.SelectedItem is not ReportListItem item)
        {
            return;
        }

        var folder = ProjectLayout.ReportFolder(_project, item.Report.Number);
        Directory.CreateDirectory(folder);
        OpenInExplorer(folder);
    }

    private void ProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_project.FolderPath);
        OpenInExplorer(_project.FolderPath);
    }

    private void EditProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var setup = new ProjectSetupWindow(_project);
        setup.Owner = this;
        if (setup.ShowDialog() == true)
        {
            ProjectTitle.Text = _project.Name;
            ProjectDetails.Text =
                $"#{_project.Number}  •  Owner: {_project.Owner}  •  Contract Manager: {_project.ContractManager}  •  General Contractor: {_project.GeneralContractor}";
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
            MessageBox.Show($"Could not open the folder.\n{ex.Message}", "Open Folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
