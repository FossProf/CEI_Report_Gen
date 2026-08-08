using System.Windows;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.App.Views;

public partial class NewReportFromExistingConfirmationWindow : Window
{
    public NewReportFromExistingConfirmationWindow(InspectionReport sourceReport, InspectionReport newReport)
    {
        InitializeComponent();
        PromptText.Text = $"Create a new report from Report {ProjectLayout.FormatReportNumber(sourceReport.Number)}?";
        NewReportNumberText.Text = $"New Report Number: {ProjectLayout.FormatReportNumber(newReport.Number)}";
        NewReportDateText.Text = $"Date: {newReport.Date:yyyy-MM-dd}";
    }

    private void CreateReportButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
