using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using Microsoft.Win32;

namespace CEI.ReportGenerator.App.Views;

public partial class ProjectSetupWindow : Window
{
    private readonly Project? _existing;

    public ProjectSetupWindow(Project? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is not null)
        {
            Title = "Edit Project";
            SaveButton.Content = "Save Changes";
            FolderBox.Text = existing.FolderPath;
            FolderBrowseButton.IsEnabled = false;
            PopulateFrom(existing);
        }
        else
        {
            FolderBox.Text = App.CurrentApp.Settings.DefaultProjectsFolder;
            try
            {
                ApplicationSettingsValidator.ValidateAndEnsureFolder(FolderBox.Text);
            }
            catch
            {
                // Keep the configured path visible even if it cannot be created yet.
            }

            var bundledTemplate = Path.Combine(AppContext.BaseDirectory, "Templates", "CEI_Base_Template_Refined.docx");
            if (File.Exists(bundledTemplate))
            {
                TemplateBox.Text = bundledTemplate;
            }
        }
    }

    public Project? CreatedProject { get; private set; }

    private string ProjectFolderPath => _existing?.FolderPath ?? FolderBox.Text.Trim();

    private void PopulateFrom(Project project)
    {
        NameBox.Text = project.Name;
        NumberBox.Text = project.Number;
        OwnerBox.Text = project.Owner;
        ContractBox.Text = project.ContractManager;
        GeneralBox.Text = project.GeneralContractor;
        TemplateBox.Text = project.TemplatePath;
        RefreshSignatureCombo(InspectorSigCombo, project.InspectorSignaturePath);
        RefreshSignatureCombo(PMSigCombo, project.ProjectManagerSignaturePath);
    }

    private void FolderBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the folder for the new project",
            InitialDirectory = GetExistingParent(ProjectFolderPath)
        };
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
            RefreshSignatureCombo(InspectorSigCombo, string.Empty);
            RefreshSignatureCombo(PMSigCombo, string.Empty);
        }
    }

    private void TemplateBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the approved CEI Word template",
            Filter = "Word documents (*.docx)|*.docx|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            TemplateBox.Text = dialog.FileName;
        }
    }

    private void RefreshSignaturesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSignatureCombo(InspectorSigCombo, InspectorSigCombo.SelectedItem as string ?? string.Empty);
        RefreshSignatureCombo(PMSigCombo, PMSigCombo.SelectedItem as string ?? string.Empty);
    }

    private void OpenSignatureFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = ProjectFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            ShowErrors(new[] { "Select the project folder first." });
            return;
        }

        Directory.CreateDirectory(folder);
        var signaturesFolder = Path.Combine(folder, ProjectLayout.SignaturesFolderName);
        Directory.CreateDirectory(signaturesFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = signaturesFolder,
            UseShellExecute = true
        });
    }

    private void InspectorSigImportButton_Click(object sender, RoutedEventArgs e)
    {
        ImportSignature(InspectorSigCombo);
    }

    private void PMSigImportButton_Click(object sender, RoutedEventArgs e)
    {
        ImportSignature(PMSigCombo);
    }

    private void ImportSignature(ComboBox combo)
    {
        var folder = ProjectFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            ShowErrors(new[] { "Select the project folder first." });
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import a signature image into the project",
            Filter = "Signature images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            var relative = SignatureStore.Import(folder, dialog.FileName, replaceIfExists: false);
            if (relative is null)
            {
                ShowErrors(new[] { "The selected signature image could not be imported." });
                return;
            }

            RefreshSignatureCombo(combo, relative);
        }
        catch (Exception ex)
        {
            ShowErrors(new[] { ex.Message });
        }
    }

    private void RefreshSignatureCombo(ComboBox combo, string storedPath)
    {
        var folder = ProjectFolderPath;
        if (string.IsNullOrWhiteSpace(folder))
        {
            combo.ItemsSource = null;
            combo.SelectedItem = null;
            return;
        }

        var names = SignatureStore.ListSignatureFiles(folder)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();
        combo.ItemsSource = names;

        var current = string.IsNullOrWhiteSpace(storedPath) ? null : Path.GetFileName(storedPath);
        if (current is not null)
        {
            var match = names.FirstOrDefault(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                combo.SelectedItem = match;
            }
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(NameBox.Text)) errors.Add("Project name is required.");
        if (string.IsNullOrWhiteSpace(NumberBox.Text)) errors.Add("Cornerstone project number is required.");
        if (string.IsNullOrWhiteSpace(OwnerBox.Text)) errors.Add("Owner is required.");
        if (string.IsNullOrWhiteSpace(ContractBox.Text)) errors.Add("Contract manager is required.");
        if (string.IsNullOrWhiteSpace(GeneralBox.Text)) errors.Add("General contractor is required.");

        if (_existing is null && string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            errors.Add("Select the project folder.");
        }

        if (!File.Exists(TemplateBox.Text)) errors.Add("Select a valid Word template file.");
        var inspectorSig = InspectorSigCombo.SelectedItem as string;
        var pmSig = PMSigCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(inspectorSig)) errors.Add("Select the Special Inspector signature image.");
        if (string.IsNullOrWhiteSpace(pmSig)) errors.Add("Select the Project Manager signature image.");

        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

        try
        {
            if (_existing is null)
            {
                var folder = FolderBox.Text.Trim();
                var existingJson = ProjectStore.ResolveProjectJson(folder);
                if (existingJson is not null)
                {
                    ShowErrors(new[] { "The selected folder already contains a project. Choose a different folder." });
                    return;
                }

                CreatedProject = ProjectStore.Create(
                    folder,
                    NameBox.Text,
                    NumberBox.Text,
                    OwnerBox.Text,
                    ContractBox.Text,
                    GeneralBox.Text,
                    TemplateBox.Text,
                    SignatureStore.SignatureRelativePath(inspectorSig!),
                    SignatureStore.SignatureRelativePath(pmSig!));
            }
            else
            {
                ProjectStore.Update(
                    _existing,
                    NameBox.Text,
                    NumberBox.Text,
                    OwnerBox.Text,
                    ContractBox.Text,
                    GeneralBox.Text,
                    TemplateBox.Text,
                    SignatureStore.SignatureRelativePath(inspectorSig!),
                    SignatureStore.SignatureRelativePath(pmSig!));
                CreatedProject = _existing;
            }

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
