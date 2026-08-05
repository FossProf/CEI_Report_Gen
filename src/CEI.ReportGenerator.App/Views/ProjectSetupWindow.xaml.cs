using System.IO;
using System.Windows;
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
            var bundledTemplate = Path.Combine(AppContext.BaseDirectory, "Templates", "CEI_Base_Template_Refined.docx");
            if (File.Exists(bundledTemplate))
            {
                TemplateBox.Text = bundledTemplate;
            }
        }
    }

    public Project? CreatedProject { get; private set; }

    private void PopulateFrom(Project project)
    {
        NameBox.Text = project.Name;
        NumberBox.Text = project.Number;
        OwnerBox.Text = project.Owner;
        ContractBox.Text = project.ContractManager;
        GeneralBox.Text = project.GeneralContractor;
        TemplateBox.Text = project.TemplatePath;
        InspectorSigBox.Text = project.InspectorSignaturePath;
        PMSigBox.Text = project.ProjectManagerSignaturePath;
    }

    private void FolderBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the folder for the new project"
        };
        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
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

    private void InspectorSigBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        InspectorSigBox.Text = PickImage();
    }

    private void PMSigBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        PMSigBox.Text = PickImage();
    }

    private string PickImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a signature image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All files (*.*)|*.*"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : string.Empty;
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
        if (!File.Exists(InspectorSigBox.Text)) errors.Add("Select the Special Inspector signature image.");
        if (!File.Exists(PMSigBox.Text)) errors.Add("Select the Project Manager signature image.");

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
                    InspectorSigBox.Text,
                    PMSigBox.Text);
            }
            else
            {
                _existing.Name = NameBox.Text.Trim();
                _existing.Number = NumberBox.Text.Trim();
                _existing.Owner = OwnerBox.Text.Trim();
                _existing.ContractManager = ContractBox.Text.Trim();
                _existing.GeneralContractor = GeneralBox.Text.Trim();
                _existing.TemplatePath = TemplateBox.Text.Trim();
                _existing.InspectorSignaturePath = InspectorSigBox.Text.Trim();
                _existing.ProjectManagerSignaturePath = PMSigBox.Text.Trim();
                ProjectStore.Save(_existing);
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
        ErrorText.Text = string.Join(Environment.NewLine, errors.Select(e => "• " + e));
        ErrorText.Visibility = Visibility.Visible;
    }
}
