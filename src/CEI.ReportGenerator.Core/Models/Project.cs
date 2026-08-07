using System.Text.Json.Serialization;
using CEI.ReportGenerator.Core.Services;

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

    /// <summary>
    /// Path of the Special Inspector signature stored relative to the project root,
    /// for example "Signatures/anthony-wintergerst.png".
    /// </summary>
    public string InspectorSignaturePath { get; set; } = string.Empty;

    /// <summary>
    /// Path of the Project Manager signature stored relative to the project root,
    /// for example "Signatures/ben-zinninger.png".
    /// </summary>
    public string ProjectManagerSignaturePath { get; set; } = string.Empty;

    public int NextReportNumber { get; set; } = 1;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? RelativeFolderPath { get; set; }

    [JsonIgnore]
    public string? RelativeTemplatePath { get; set; }

    [JsonIgnore]
    public string FilePath => Path.Combine(FolderPath, ProjectLayout.ProjectFileName);

    [JsonIgnore]
    public string SignatureFolderPath => Path.Combine(FolderPath, ProjectLayout.SignaturesFolderName);

    [JsonIgnore]
    public string? ResolvedInspectorSignaturePath
        => SignatureStore.Resolve(FolderPath, InspectorSignaturePath).FullPath;

    [JsonIgnore]
    public string? ResolvedProjectManagerSignaturePath
        => SignatureStore.Resolve(FolderPath, ProjectManagerSignaturePath).FullPath;
}
