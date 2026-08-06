using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace CEI.ReportGenerator.App.Views;

public partial class GenerationErrorDialog : Window
{
    private readonly string _logPath;
    private readonly string _summary;
    private readonly IReadOnlyCollection<string> _errors;

    public GenerationErrorDialog(string summary, IReadOnlyCollection<string> errors, string logPath)
    {
        InitializeComponent();
        _summary = summary;
        _errors = errors;
        _logPath = logPath;

        SummaryText.Text = summary;
        ErrorListText.Text = string.Join(Environment.NewLine, errors.Select(e => "• " + e));
        LogPathText.Text = "Diagnostic details saved to: " + logPath;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var details = string.Join(Environment.NewLine,
            _errors.Select(e => e.StartsWith("• ", StringComparison.Ordinal) ? e : "• " + e));
        var text = _summary + Environment.NewLine + Environment.NewLine + details;
        Clipboard.SetText(text);
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_logPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch
        {
            // best effort
        }
    }
}
