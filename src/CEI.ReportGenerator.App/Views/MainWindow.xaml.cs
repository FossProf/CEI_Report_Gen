using System.Windows;
using System.Windows.Input;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using Microsoft.Win32;

namespace CEI.ReportGenerator.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshRecentProjects();
    }

    private void RefreshRecentProjects()
    {
        RecentList.ItemsSource = RecentProjectStore.Load();
    }

    private void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is RecentProjectEntry entry)
        {
            OpenProjectFolder(entry.FolderPath);
        }
    }

    private void OpenSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is RecentProjectEntry entry)
        {
            OpenProjectFolder(entry.FolderPath);
        }
    }

    private void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the project folder (contains project.json)"
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenProjectFolder(dialog.FolderName);
        }
    }

    private void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var setup = new ProjectSetupWindow();
        setup.Owner = this;
        if (setup.ShowDialog() == true && setup.CreatedProject is not null)
        {
            OpenProject(setup.CreatedProject);
        }
    }

    private void OpenProjectFolder(string folderPath)
    {
        var project = ProjectStore.Load(folderPath);
        if (project is null)
        {
            MessageBox.Show(this,
                "No project.json was found in the selected folder.",
                "Open Project",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        OpenProject(project);
    }

    private void OpenProject(Project project)
    {
        RecentProjectStore.Record(project.Name, project.FolderPath);
        var window = new ProjectWindow(project);
        window.Owner = this;
        window.ShowDialog();
        RefreshRecentProjects();
    }
}
