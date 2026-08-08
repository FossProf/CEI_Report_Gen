using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CEI.ReportGenerator.App.Views;

public partial class ApplicationSettingsWindow : Window
{
    private readonly ApplicationSettings _workingCopy;

    public ApplicationSettingsWindow(ApplicationSettings settings)
    {
        InitializeComponent();
        _workingCopy = settings.Clone();
        PopulateForm(_workingCopy);
    }

    public ApplicationSettings? SavedSettings { get; private set; }

    private void PopulateForm(ApplicationSettings settings)
    {
        DefaultProjectsFolderBox.Text = settings.DefaultProjectsFolder;
        RecentProjectLimitBox.Text = settings.RecentProjectLimit.ToString();
        ReopenLastProjectCheckBox.IsChecked = settings.ReopenLastProjectOnStartup;
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
    }

    private void BrowseDefaultFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var initialFolder = DefaultProjectsFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(initialFolder) || File.Exists(initialFolder))
        {
            initialFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select the default projects folder",
            InitialDirectory = GetExistingParent(initialFolder)
        };
        if (dialog.ShowDialog(this) == true)
        {
            DefaultProjectsFolderBox.Text = dialog.FolderName;
        }
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        PopulateForm(ApplicationSettings.CreateDefaults());
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RecentProjectLimitBox.Text.Trim(), out var recentLimit))
        {
            ShowErrors(new[] { "Recent projects shown must be a whole number." });
            return;
        }

        _workingCopy.DefaultProjectsFolder = DefaultProjectsFolderBox.Text.Trim();
        _workingCopy.RecentProjectLimit = recentLimit;
        _workingCopy.ReopenLastProjectOnStartup = ReopenLastProjectCheckBox.IsChecked == true;

        try
        {
            var app = App.CurrentApp;
            app.SettingsStore.Save(_workingCopy);
            SavedSettings = _workingCopy.Clone();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowErrors(new[] { ex.Message });
        }
    }

    private void ShowErrors(IEnumerable<string> errors)
    {
        ErrorText.Text = string.Join(Environment.NewLine, errors.Select(e => "* " + e));
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string GetExistingParent(string path)
    {
        var current = path;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }
}
