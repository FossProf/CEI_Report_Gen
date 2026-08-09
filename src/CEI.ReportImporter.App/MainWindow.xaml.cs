using System.Windows;
using CEI.ReportImporter.Core.Models;
using CEI.ReportImporter.Core.Services;
using Forms = System.Windows.Forms;

namespace CEI.ReportImporter.App;

public partial class MainWindow : Window
{
    private readonly HistoricalReportScanner _scanner = new(new HistoricalDocumentParser());

    private HistoricalScanSession? _currentScanSession;

    public MainWindow()
    {
        InitializeComponent();
        ResultsGrid.ItemsSource = Array.Empty<HistoricalScanResult>();
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

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScanButton.IsEnabled = false;
            SummaryTextBlock.Text = "Scanning...";

            var session = await Task.Run(() => _scanner.Scan(new HistoricalReportScanOptions
            {
                SourceFolder = SourceFolderTextBox.Text.Trim(),
                IncludeSubfolders = IncludeSubfoldersCheckBox.IsChecked == true
            }));

            _currentScanSession = session;
            ResultsGrid.ItemsSource = _currentScanSession.Results;
            SummaryTextBlock.Text = $"{session.FilesDiscovered} found, {session.ParsedCount} parsed, {session.FailedCount} failed.";
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

    private void ResultsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is not HistoricalScanResult result)
        {
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            $"Parser preview is not implemented yet.{Environment.NewLine}{Environment.NewLine}{result.SourceFileName}",
            "Preview Pending",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
