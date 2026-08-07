using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

        ReportNumberText.Text = isNew ? "New report" : $"Report {ProjectLayout.FormatReportNumber(report.Number)}";
        ReportNumberBox.Text = report.Number.ToString();
        ReportNumberBox.IsEnabled = isNew;

        DatePicker.SelectedDate = report.Date;
        TemperatureBox.Text = report.Temperature;
        WeatherCombo.ItemsSource = WeatherOptions.All;
        if (WeatherOptions.IsValid(report.Weather))
        {
            WeatherCombo.SelectedItem = report.Weather;
        }

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

        UpdatePhotoNumbers();
        UpdatePhotoButtons();
    }

    private static readonly string[] PhotoExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

    private static bool IsPhotoFile(string path)
        => PhotoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private PhotoItem? _dragSource;

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

        AddPhotoFiles(dialog.FileNames);
    }

    private void AddPhotoFiles(IEnumerable<string> paths)
    {
        var anyAdded = false;
        foreach (var path in paths.Where(IsPhotoFile))
        {
            var photo = new Photo { SourcePath = path };
            _report.Photos.Add(photo);
            PhotoList.Items.Add(new PhotoItem(photo));
            anyAdded = true;
        }

        if (!anyAdded)
        {
            return;
        }

        UpdatePhotoNumbers();
        UpdatePhotoButtons();
    }

    private void RemovePhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoList.SelectedItem is not PhotoItem item)
        {
            return;
        }

        _report.Photos.Remove(item.Model);
        PhotoList.Items.Remove(item);
        UpdatePhotoNumbers();
        UpdatePhotoButtons();
    }

    private void MoveUpPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoList.SelectedItem is PhotoItem item)
        {
            MovePhoto(item, PhotoList.Items.IndexOf(item) - 1);
        }
    }

    private void MoveDownPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoList.SelectedItem is PhotoItem item)
        {
            MovePhoto(item, PhotoList.Items.IndexOf(item) + 2);
        }
    }

    private void PhotoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePhotoButtons();
    }

    private void PhotoList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragSource = null;
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindVisualAncestor<TextBox>(source) is not null || FindVisualAncestor<ScrollBar>(source) is not null)
        {
            return;
        }

        _dragSource = (ItemsControl.ContainerFromElement(PhotoList, source) as ListBoxItem)?.Content as PhotoItem;
    }

    private void PhotoList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(PhotoList, new DataObject(typeof(PhotoItem), _dragSource), DragDropEffects.Move);
        _dragSource = null;
    }

    private void PhotoList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PhotoItem)) || e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PhotoList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PhotoItem)) is PhotoItem moved)
        {
            MovePhoto(moved, GetDropIndex(e));
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddPhotoFiles(files);
        }

        e.Handled = true;
    }

    private int GetDropIndex(DragEventArgs e)
    {
        var point = e.GetPosition(PhotoList);
        for (var i = 0; i < PhotoList.Items.Count; i++)
        {
            if (PhotoList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container)
            {
                continue;
            }

            var top = container.TranslatePoint(new Point(0, 0), PhotoList).Y;
            if (point.Y < top + (container.ActualHeight / 2))
            {
                return i;
            }
        }

        return PhotoList.Items.Count;
    }

    private void MovePhoto(PhotoItem item, int dropIndex)
    {
        var oldIndex = PhotoList.Items.IndexOf(item);
        if (oldIndex < 0)
        {
            return;
        }

        var target = dropIndex;
        if (oldIndex < dropIndex)
        {
            target--;
        }

        target = Math.Max(0, Math.Min(target, PhotoList.Items.Count - 1));
        if (target == oldIndex)
        {
            return;
        }

        PhotoList.Items.Remove(item);
        PhotoList.Items.Insert(target, item);
        PhotoList.SelectedItem = item;
        UpdatePhotoNumbers();
        UpdatePhotoButtons();
    }

    private void UpdatePhotoNumbers()
    {
        for (var i = 0; i < PhotoList.Items.Count; i++)
        {
            if (PhotoList.Items[i] is PhotoItem item)
            {
                item.Number = i + 1;
            }
        }
    }

    private void UpdatePhotoButtons()
    {
        var hasSelection = PhotoList.SelectedItem is PhotoItem;
        RemovePhotoButton.IsEnabled = hasSelection;
        MoveUpPhotoButton.IsEnabled = hasSelection && PhotoList.SelectedIndex > 0;
        MoveDownPhotoButton.IsEnabled = hasSelection
            && PhotoList.SelectedIndex >= 0
            && PhotoList.SelectedIndex < PhotoList.Items.Count - 1;
    }

    private static T? FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        var errors = CollectFromUi();
        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

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
        var errors = CollectFromUi();
        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

        ShowErrors(Array.Empty<string>());

        GenerationResult result;
        try
        {
            result = ReportGeneratorService.GenerateDraft(_project, _report);
        }
        catch (GenerationException ex)
        {
            ShowErrors(ex.Errors);
            ShowErrorDialog(ex);
            return;
        }
        catch (Exception ex)
        {
            ShowErrors(new[] { ex.Message });
            ShowErrorDialog(ex);
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

    private IReadOnlyCollection<string> CollectFromUi()
    {
        var errors = new List<string>();

        if (ReportNumberBox.IsEnabled)
        {
            if (!int.TryParse(ReportNumberBox.Text.Trim(), out var number) || number <= 0)
            {
                errors.Add("Report number must be a positive whole number.");
            }
            else if (number != _report.Number)
            {
                if (ReportStore.LoadReport(_project, number) is not null)
                {
                    errors.Add($"A report with number {number} already exists. Choose a different number.");
                }
                else
                {
                    _report.Number = number;
                }
            }
        }

        _report.Date = DatePicker.SelectedDate ?? DateTime.Today;
        _report.Temperature = TemperatureBox.Text.Trim();
        _report.Weather = WeatherCombo.SelectedItem as string ?? string.Empty;
        _report.Locations = LocationBox.Text.Trim();
        _report.Inspectors = InspectorBox.Text.Trim();
        _report.PersonnelOnSite = PersonnelBox.Text.Trim();
        _report.DescriptionOfWork = DescriptionBox.Text.Trim();
        _report.DrawingsReviewed = DrawingBox.Text.Trim();
        _report.Observations = ObservationBox.Text.Trim();
        _report.NewDiscrepancies = NewDiscrepancyBox.Text.Trim();
        _report.PreviousDiscrepancies = OldDiscrepancyBox.Text.Trim();
        _report.Photos = PhotoList.Items.OfType<PhotoItem>().Select(p => p.Model).ToList();

        return errors;
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

    private void ShowErrorDialog(GenerationException ex)
    {
        var logPath = WriteErrorLog(ex.Stage?.ToString() ?? "Unknown", ex.Errors, null);
        var dialog = new GenerationErrorDialog("Report generation failed during " + (ex.Stage?.ToString() ?? "report validation") + ".",
            ex.Errors, logPath);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void ShowErrorDialog(Exception ex)
    {
        var logPath = WriteErrorLog("Unexpected", new[] { ex.Message }, ex);
        var dialog = new GenerationErrorDialog("Report generation failed unexpectedly.",
            new[] { ex.Message }, logPath);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private string WriteErrorLog(string stage, IReadOnlyCollection<string> errors, Exception? exception)
    {
        var folder = ProjectLayout.ReportFolder(_project, _report.Number);
        Directory.CreateDirectory(folder);
        var logPath = Path.Combine(folder, "generation-error.log");
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("CEI Report Generator - generation diagnostics");
        builder.AppendLine("Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Stage: " + stage);
        builder.AppendLine("Errors:");
        foreach (var error in errors)
        {
            builder.AppendLine("  - " + error);
        }

        if (exception is not null)
        {
            builder.AppendLine("Exception: " + exception);
        }

        File.WriteAllText(logPath, builder.ToString());
        return logPath;
    }
}
