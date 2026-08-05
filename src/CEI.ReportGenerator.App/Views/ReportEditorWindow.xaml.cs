using System.IO;
using System.Windows;
using CEI.ReportGenerator.App.ViewModels;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using Microsoft.Win32;
using ReportGeneratorService = CEI.ReportGenerator.Core.Services.ReportGenerator;

namespace CEI.ReportGenerator.App.Views;

public partial class ReportEditorWindow : Window
{
    private readonly Project _project;
    private readonly InspectionReport _report;

    public ReportEditorWindow(Project project, InspectionReport report, bool isNew)
    {
        InitializeComponent();
        _project = project;
        _report = report;

        ReportNumberText.Text = $"Report {ProjectLayout.FormatReportNumber(report.Number)}" +
                                (isNew ? "  (new — draft)" : "");

        DatePicker.SelectedDate = report.Date;
        TemperatureBox.Text = report.Temperature;
        WeatherBox.Text = report.Weather;
        LocationBox.Text = report.Locations;
        InspectorBox.Text = report.Inspectors;
        PersonnelBox.Text = report.PersonnelOnSite;
        DescriptionBox.Text = report.DescriptionOfWork;
        DrawingBox.Text = report.DrawingsReviewed;
        ObservationBox.Text = report.Observations;
        NewDiscrepancyBox.Text = report.NewDiscrepancies;
        OldDiscrepancyBox.Text = report.PreviousDiscrepancies;

        foreach (var photo in report.Photos)
        {
            var source = ReportStore.ResolvePhotoSourcePath(project, report, photo);
            if (!string.IsNullOrEmpty(source))
            {
                photo.SourcePath = source;
            }

            PhotoList.Items.Add(new PhotoItem(photo));
        }

        UpdateRemoveButton();
    }

    private void AddPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a photograph",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            var photo = new Photo { SourcePath = path };
            _report.Photos.Add(photo);
            PhotoList.Items.Add(new PhotoItem(photo));
        }

        UpdateRemoveButton();
    }

    private void RemovePhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoList.SelectedItem is not PhotoItem item)
        {
            return;
        }

        _report.Photos.Remove(item.Model);
        PhotoList.Items.Remove(item);
        UpdateRemoveButton();
    }

    private void UpdateRemoveButton()
    {
        RemovePhotoButton.IsEnabled = PhotoList.Items.Count > 0;
    }

    private void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        CollectFromUi();

        try
        {
            ReportGeneratorService.SaveDraft(_project, _report);
            MessageBox.Show(this,
                $"Draft saved to:\n{ProjectLayout.ReportFilePath(_project, _report.Number)}",
                "Draft Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowErrors(new[] { ex.Message });
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        CollectFromUi();
        ShowErrors(Array.Empty<string>());

        GenerationResult result;
        try
        {
            result = ReportGeneratorService.GenerateDraft(_project, _report);
        }
        catch (GenerationException ex)
        {
            ShowErrors(ex.Errors);
            return;
        }
        catch (Exception ex)
        {
            ShowErrors(new[] { ex.Message });
            return;
        }

        var dialog = new GenerationResultWindow(result.OutputPath);
        dialog.Owner = this;
        var action = dialog.ShowDialog();

        if (action == true)
        {
            try
            {
                ReportGeneratorService.FinalizeReport(_project, _report, result.OutputPath);
                MessageBox.Show(this,
                    $"Report {ProjectLayout.FormatReportNumber(_report.Number)} has been finalized and saved.",
                    "Report Final",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowErrors(new[] { ex.Message });
            }
        }
    }

    private void CollectFromUi()
    {
        _report.Date = DatePicker.SelectedDate ?? DateTime.Today;
        _report.Temperature = TemperatureBox.Text.Trim();
        _report.Weather = WeatherBox.Text.Trim();
        _report.Locations = LocationBox.Text.Trim();
        _report.Inspectors = InspectorBox.Text.Trim();
        _report.PersonnelOnSite = PersonnelBox.Text.Trim();
        _report.DescriptionOfWork = DescriptionBox.Text.Trim();
        _report.DrawingsReviewed = DrawingBox.Text.Trim();
        _report.Observations = ObservationBox.Text.Trim();
        _report.NewDiscrepancies = NewDiscrepancyBox.Text.Trim();
        _report.PreviousDiscrepancies = OldDiscrepancyBox.Text.Trim();
        _report.Photos = PhotoList.Items.OfType<PhotoItem>().Select(p => p.Model).ToList();
    }

    private void ShowErrors(IReadOnlyCollection<string> errors)
    {
        if (errors.Count == 0)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        ErrorText.Text = string.Join(Environment.NewLine, errors.Select(e => "• " + e));
        ErrorText.Visibility = Visibility.Visible;
    }
}
