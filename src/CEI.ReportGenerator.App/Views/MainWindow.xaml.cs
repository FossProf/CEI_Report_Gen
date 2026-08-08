using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using Microsoft.Win32;

namespace CEI.ReportGenerator.App.Views;

public partial class MainWindow : Window
{
    private List<RecentProjectEntry> _recentProjects = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshRecentProjects();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            CreateNewProject();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            OpenProject();
            e.Handled = true;
        }
    }

    private void RefreshRecentProjects()
    {
        _recentProjects = RecentProjectStore.Load().Take(10).ToList();
        RecentList.ItemsSource = _recentProjects;
        BuildRecentProjectsMenu();
        UpdateRecentSelectionState();
    }

    private void BuildRecentProjectsMenu()
    {
        RecentProjectsMenuItem.Items.Clear();

        if (_recentProjects.Count == 0)
        {
            RecentProjectsMenuItem.Items.Add(new MenuItem
            {
                Header = "(No Recent Projects)",
                IsEnabled = false
            });
            return;
        }

        foreach (var entry in _recentProjects)
        {
            var item = new MenuItem
            {
                Header = entry.Name,
                Tag = entry
            };
            item.Click += RecentProjectMenuItem_Click;
            RecentProjectsMenuItem.Items.Add(item);
        }
    }

    private void UpdateRecentSelectionState()
    {
        var hasSelection = RecentList.SelectedItem is RecentProjectEntry;
        OpenSelectedButton.IsEnabled = hasSelection;
        RemoveSelectedButton.IsEnabled = hasSelection;
    }

    private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateRecentSelectionState();
    }

    private void RecentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedRecentProject();
    }

    private void OpenSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedRecentProject();
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is RecentProjectEntry entry)
        {
            RemoveRecentProject(entry);
        }
    }

    private void RemoveRecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RecentProjectEntry entry })
        {
            RemoveRecentProject(entry);
        }
    }

    private void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        CreateNewProject();
    }

    private void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProject();
    }

    private void NewProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        CreateNewProject();
    }

    private void OpenProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenProject();
    }

    private void RecentProjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: RecentProjectEntry entry })
        {
            OpenProjectFolder(entry.FolderPath);
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
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

    private void CreateNewProject()
    {
        var setup = new ProjectSetupWindow
        {
            Owner = this
        };

        if (setup.ShowDialog() == true && setup.CreatedProject is not null)
        {
            OpenProject(setup.CreatedProject);
        }
    }

    private void OpenProject()
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

    private void OpenSelectedRecentProject()
    {
        if (RecentList.SelectedItem is RecentProjectEntry entry)
        {
            OpenProjectFolder(entry.FolderPath);
        }
    }

    private void RemoveRecentProject(RecentProjectEntry entry)
    {
        RecentProjectStore.Remove(entry.FolderPath);
        RefreshRecentProjects();
    }

    private void OpenProjectFolder(string folderPath)
    {
        var project = ProjectStore.Load(folderPath);
        if (project is null)
        {
            MessageBox.Show(
                this,
                "No project.json was found in the selected folder.",
                "Open Project",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            RefreshRecentProjects();
            return;
        }

        OpenProject(project);
    }

    private void OpenProject(Project project)
    {
        RecentProjectStore.Record(project.Name, project.FolderPath);
        var window = new ProjectWindow(project)
        {
            Owner = this
        };
        window.ShowDialog();
        RefreshRecentProjects();
    }
}
