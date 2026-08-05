namespace CEI.ReportGenerator.Core.Models;

public sealed class Project
{
    public string Name { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string ContractManager { get; set; } = string.Empty;

    public string GeneralContractor { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public string TemplatePath { get; set; } = string.Empty;

    public string InspectorSignaturePath { get; set; } = string.Empty;

    public string ProjectManagerSignaturePath { get; set; } = string.Empty;

    public int NextReportNumber { get; set; } = 1;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string FilePath => Path.Combine(FolderPath, ProjectLayout.ProjectFileName);
}
