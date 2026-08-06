using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ProjectStore
{
    public static Project Create(string folderPath, string name, string number, string owner,
        string contractManager, string generalContractor, string templateSourcePath,
        string inspectorSignaturePath, string projectManagerSignaturePath)
    {
        var project = new Project
        {
            Name = name.Trim(),
            Number = number.Trim(),
            Owner = owner.Trim(),
            ContractManager = contractManager.Trim(),
            GeneralContractor = generalContractor.Trim(),
            FolderPath = folderPath,
            CreatedUtc = DateTime.UtcNow
        };

        BuildDirectoryStructure(project);

        project.TemplatePath = CopyTemplate(project, templateSourcePath);
        project.InspectorSignaturePath = StoreSignature(project, inspectorSignaturePath) ?? string.Empty;
        project.ProjectManagerSignaturePath = StoreSignature(project, projectManagerSignaturePath) ?? string.Empty;

        Save(project);
        return project;
    }

    private static string StoreSignature(Project project, string signatureSourcePath)
    {
        if (string.IsNullOrWhiteSpace(signatureSourcePath) || !File.Exists(signatureSourcePath))
        {
            throw new InvalidOperationException("A signature image file is required.");
        }

        var relative = SignatureStore.RelativePath(project.FolderPath, signatureSourcePath);
        if (relative is not null)
        {
            return relative;
        }

        var imported = SignatureStore.Import(project.FolderPath, signatureSourcePath, replaceIfExists: true);
        return imported ?? throw new InvalidOperationException("Signature image could not be stored in the project.");
    }

    public static void Save(Project project)
    {
        Directory.CreateDirectory(project.FolderPath);
        JsonStore.Save(project.FilePath, project);
    }

    public static Project? Load(string projectJsonPathOrFolder)
    {
        var path = ResolveProjectJson(projectJsonPathOrFolder);
        return path is null ? null : JsonStore.Load<Project>(path);
    }

    public static string? ResolveProjectJson(string projectJsonPathOrFolder)
    {
        if (string.IsNullOrWhiteSpace(projectJsonPathOrFolder))
        {
            return null;
        }

        if (File.Exists(projectJsonPathOrFolder)
            && string.Equals(Path.GetFileName(projectJsonPathOrFolder), ProjectLayout.ProjectFileName, StringComparison.OrdinalIgnoreCase))
        {
            return projectJsonPathOrFolder;
        }

        if (Directory.Exists(projectJsonPathOrFolder))
        {
            var candidate = Path.Combine(projectJsonPathOrFolder, ProjectLayout.ProjectFileName);
            return File.Exists(candidate) ? candidate : null;
        }

        return null;
    }

    public static void BuildDirectoryStructure(Project project)
    {
        Directory.CreateDirectory(project.FolderPath);
        Directory.CreateDirectory(ProjectLayout.ReportsFolder(project));
        Directory.CreateDirectory(ProjectLayout.SignaturesFolder(project));
    }

    public static void IncrementReportNumber(Project project)
    {
        project.NextReportNumber++;
        Save(project);
    }

    private static string CopyTemplate(Project project, string templateSourcePath)
    {
        if (string.IsNullOrWhiteSpace(templateSourcePath) || !File.Exists(templateSourcePath))
        {
            throw new InvalidOperationException("A valid CEI Word template is required.");
        }

        var target = Path.Combine(project.FolderPath, ProjectLayout.TemplateFileName);
        File.Copy(templateSourcePath, target, overwrite: true);
        return target;
    }
}
