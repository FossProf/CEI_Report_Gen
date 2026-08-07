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
        if (string.IsNullOrWhiteSpace(signatureSourcePath))
        {
            throw new InvalidOperationException("A signature image file is required.");
        }

        var resolved = SignatureStore.Resolve(project.FolderPath, signatureSourcePath);
        if (resolved.Status == SignatureResolveStatus.Valid)
        {
            return SignatureStore.RelativePath(project.FolderPath, resolved.FullPath!)!;
        }

        if (resolved.Status == SignatureResolveStatus.UnsupportedExtension)
        {
            throw new InvalidOperationException("Only PNG, JPG, and JPEG signature images are supported.");
        }

        if (resolved.Status == SignatureResolveStatus.OutsideProject)
        {
            var imported = SignatureStore.Import(project.FolderPath, signatureSourcePath, replaceIfExists: true);
            if (imported is not null)
            {
                return imported;
            }
        }

        throw new InvalidOperationException("A signature image file is required.");
    }

    public static void Save(Project project)
    {
        Directory.CreateDirectory(project.FolderPath);

        var absoluteFolderPath = project.FolderPath;
        var absoluteTemplatePath = project.TemplatePath;
        var filePath = Path.Combine(absoluteFolderPath, ProjectLayout.ProjectFileName);

        try
        {
            if (!string.IsNullOrWhiteSpace(project.RelativeFolderPath))
            {
                project.FolderPath = project.RelativeFolderPath;
            }

            if (!string.IsNullOrWhiteSpace(project.RelativeTemplatePath))
            {
                project.TemplatePath = project.RelativeTemplatePath;
            }

            JsonStore.Save(filePath, project);
        }
        finally
        {
            project.FolderPath = absoluteFolderPath;
            project.TemplatePath = absoluteTemplatePath;
        }
    }

    public static Project? Load(string projectJsonPathOrFolder)
    {
        var path = ResolveProjectJson(projectJsonPathOrFolder);
        if (path is null)
        {
            return null;
        }

        var project = JsonStore.Load<Project>(path);
        if (project is null)
        {
            return null;
        }

        NormalizePaths(project, Path.GetDirectoryName(path));
        return project;
    }

    private static void NormalizePaths(Project project, string? projectJsonDirectory)
    {
        if (!string.IsNullOrWhiteSpace(project.FolderPath)
            && !Path.IsPathRooted(project.FolderPath)
            && projectJsonDirectory is not null)
        {
            project.RelativeFolderPath = project.FolderPath;
            project.FolderPath = Path.GetFullPath(Path.Combine(projectJsonDirectory, project.FolderPath));
        }

        if (!string.IsNullOrWhiteSpace(project.TemplatePath)
            && !Path.IsPathRooted(project.TemplatePath)
            && Path.IsPathRooted(project.FolderPath))
        {
            project.RelativeTemplatePath = project.TemplatePath;
            project.TemplatePath = Path.GetFullPath(Path.Combine(project.FolderPath, project.TemplatePath));
        }
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
