using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CEI.ReportGenerator.App.Views;

public partial class GenerationResultWindow : Window
{
    private readonly string _outputPath;

    public GenerationResultWindow(string outputPath)
    {
        InitializeComponent();
        _outputPath = outputPath;
        PathText.Text = outputPath;
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFile(_outputPath);
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_outputPath);
        if (folder is not null)
        {
            OpenFile(folder);
        }
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private static void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("The file could not be found.", "Open",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
            MessageBox.Show($"Could not open the file.\n{ex.Message}", "Open",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
