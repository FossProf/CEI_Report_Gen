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
            FolderPath = Path.GetFullPath(folderPath),
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
            var imported = SignatureStore.Import(project.FolderPath, signatureSourcePath, replaceIfExists: false);
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

        var filePath = Path.Combine(project.FolderPath, ProjectLayout.ProjectFileName);
        JsonStore.Save(filePath, ToPortableProject(project));
    }

    public static void Update(Project project, string name, string number, string owner,
        string contractManager, string generalContractor, string templateSourcePath,
        string inspectorSignaturePath, string projectManagerSignaturePath)
    {
        project.Name = name.Trim();
        project.Number = number.Trim();
        project.Owner = owner.Trim();
        project.ContractManager = contractManager.Trim();
        project.GeneralContractor = generalContractor.Trim();
        project.TemplatePath = CopyTemplate(project, templateSourcePath);
        project.InspectorSignaturePath = StoreSignature(project, inspectorSignaturePath);
        project.ProjectManagerSignaturePath = StoreSignature(project, projectManagerSignaturePath);
        Save(project);
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

    public static void RefreshNextReportNumber(Project project)
    {
        project.NextReportNumber = ReportStore.GetNextReportNumber(project);
        Save(project);
    }

    public static void AdvanceReportNumber(Project project, int finalizedReportNumber)
    {
        project.NextReportNumber = Math.Max(
            Math.Max(project.NextReportNumber, ReportStore.GetNextReportNumber(project)),
            finalizedReportNumber + 1);
        Save(project);
    }

    private static string CopyTemplate(Project project, string templateSourcePath)
    {
        if (string.IsNullOrWhiteSpace(templateSourcePath) || !File.Exists(templateSourcePath))
        {
            throw new InvalidOperationException("A valid CEI Word template is required.");
        }

        var target = Path.Combine(project.FolderPath, ProjectLayout.TemplateFileName);
        if (string.Equals(Path.GetFullPath(templateSourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        File.Copy(templateSourcePath, target, overwrite: true);
        return target;
    }

    private static Project ToPortableProject(Project project)
    {
        var folderPath = ".";
        var templatePath = ToPortablePath(project.FolderPath, project.TemplatePath);

        return new Project
        {
            Name = project.Name,
            Number = project.Number,
            Owner = project.Owner,
            ContractManager = project.ContractManager,
            GeneralContractor = project.GeneralContractor,
            FolderPath = folderPath,
            TemplatePath = templatePath,
            InspectorSignaturePath = project.InspectorSignaturePath,
            ProjectManagerSignaturePath = project.ProjectManagerSignaturePath,
            NextReportNumber = project.NextReportNumber,
            CreatedUtc = project.CreatedUtc
        };
    }

    private static string ToPortablePath(string projectFolder, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var relative = SignatureStore.RelativePath(projectFolder, path);
        return relative ?? path;
    }
}
