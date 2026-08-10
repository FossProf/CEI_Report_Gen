using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class ProjectStore
{
    public static Project Create(string folderPath, string name, string number, string owner,
        string contractManager, string generalContractor, string templateSourcePath,
        string inspectorSignaturePath, string projectManagerSignaturePath,
        string? locationText = null, double? locationLatitude = null,
        double? locationLongitude = null, string? locationTimeZoneId = null)
    {
        var project = new Project
        {
            Name = name.Trim(),
            Number = number.Trim(),
            Owner = owner.Trim(),
            ContractManager = contractManager.Trim(),
            GeneralContractor = generalContractor.Trim(),
            LocationText = locationText?.Trim() ?? string.Empty,
            LocationLatitude = locationLatitude,
            LocationLongitude = locationLongitude,
            LocationTimeZoneId = locationTimeZoneId?.Trim() ?? string.Empty,
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
        string inspectorSignaturePath, string projectManagerSignaturePath,
        string? locationText = null, double? locationLatitude = null,
        double? locationLongitude = null, string? locationTimeZoneId = null)
    {
        var trimmedName = name.Trim();
        var trimmedNumber = number.Trim();
        var trimmedOwner = owner.Trim();
        var trimmedContractManager = contractManager.Trim();
        var trimmedGeneralContractor = generalContractor.Trim();
        var trimmedLocationText = locationText?.Trim() ?? string.Empty;
        var trimmedLocationTimeZoneId = locationTimeZoneId?.Trim() ?? string.Empty;

        var stagedTemplate = StageTemplateReplacement(project, templateSourcePath);
        var stagedInspector = StageSignature(project, inspectorSignaturePath);
        var stagedProjectManager = StageSignature(project, projectManagerSignaturePath);

        var previousProjectJson = File.Exists(project.FilePath) ? File.ReadAllBytes(project.FilePath) : null;
        var previousTemplateBytes = stagedTemplate.ShouldReplaceExisting && File.Exists(stagedTemplate.FinalPath)
            ? File.ReadAllBytes(stagedTemplate.FinalPath)
            : null;

        try
        {
            CommitStagedTemplate(stagedTemplate);
            CommitStagedSignature(stagedInspector);
            CommitStagedSignature(stagedProjectManager);

            var candidate = Clone(project);
            candidate.Name = trimmedName;
            candidate.Number = trimmedNumber;
            candidate.Owner = trimmedOwner;
            candidate.ContractManager = trimmedContractManager;
            candidate.GeneralContractor = trimmedGeneralContractor;
            candidate.LocationText = trimmedLocationText;
            candidate.LocationLatitude = locationLatitude;
            candidate.LocationLongitude = locationLongitude;
            candidate.LocationTimeZoneId = trimmedLocationTimeZoneId;
            candidate.TemplatePath = stagedTemplate.FinalPath;
            candidate.InspectorSignaturePath = stagedInspector.FinalRelativePath;
            candidate.ProjectManagerSignaturePath = stagedProjectManager.FinalRelativePath;

            var errors = Validation.ValidateProject(candidate);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            }

            Save(candidate);
            CopyProjectState(candidate, project);
        }
        catch
        {
            TryRestoreProjectJson(project.FilePath, previousProjectJson);
            TryRestoreTemplate(stagedTemplate, previousTemplateBytes);
            TryDeleteCommittedSignature(stagedInspector);
            TryDeleteCommittedSignature(stagedProjectManager);
            CleanupStagedArtifacts(stagedTemplate, stagedInspector, stagedProjectManager);
            throw;
        }
        finally
        {
            CleanupStagedArtifacts(stagedTemplate, stagedInspector, stagedProjectManager);
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

    public static void RefreshNextReportNumber(Project project)
    {
        project.NextReportNumber = ReportStore.GetNextReportNumber(project);
        Save(project);
    }

    public static int SynchronizeNextReportNumber(Project project)
        => ReportStore.SynchronizeNextReportNumber(project);

    public static void AdvanceReportNumber(Project project, int finalizedReportNumber)
    {
        project.NextReportNumber = ReportStore.GetNextReportNumber(project);
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

    private sealed record StagedTemplate(string FinalPath, string? TempPath, bool ShouldReplaceExisting);

    private sealed record StagedSignature(string FinalRelativePath, string FinalPath, string? TempPath, bool CreatedNewFile);

    private static StagedTemplate StageTemplateReplacement(Project project, string templateSourcePath)
    {
        if (string.IsNullOrWhiteSpace(templateSourcePath) || !File.Exists(templateSourcePath))
        {
            throw new InvalidOperationException("A valid CEI Word template is required.");
        }

        var finalPath = Path.Combine(project.FolderPath, ProjectLayout.TemplateFileName);
        if (string.Equals(Path.GetFullPath(templateSourcePath), Path.GetFullPath(finalPath), StringComparison.OrdinalIgnoreCase))
        {
            return new StagedTemplate(finalPath, null, ShouldReplaceExisting: false);
        }

        var tempPath = Path.Combine(project.FolderPath, $".{ProjectLayout.TemplateFileName}.{Guid.NewGuid():N}.tmp.docx");
        File.Copy(templateSourcePath, tempPath, overwrite: true);
        using (DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(tempPath, false))
        {
        }

        return new StagedTemplate(finalPath, tempPath, ShouldReplaceExisting: true);
    }

    private static StagedSignature StageSignature(Project project, string signatureSourcePath)
    {
        if (string.IsNullOrWhiteSpace(signatureSourcePath))
        {
            throw new InvalidOperationException("A signature image file is required.");
        }

        var resolved = SignatureStore.Resolve(project.FolderPath, signatureSourcePath);
        if (resolved.Status == SignatureResolveStatus.Valid)
        {
            var finalRelative = SignatureStore.RelativePath(project.FolderPath, resolved.FullPath!)!;
            return new StagedSignature(finalRelative, resolved.FullPath!, null, CreatedNewFile: false);
        }

        if (resolved.Status == SignatureResolveStatus.UnsupportedExtension)
        {
            throw new InvalidOperationException("Only PNG, JPG, and JPEG signature images are supported.");
        }

        if (resolved.Status != SignatureResolveStatus.OutsideProject || !File.Exists(signatureSourcePath))
        {
            throw new InvalidOperationException("A signature image file is required.");
        }

        if (!SignatureStore.IsSupportedFileName(signatureSourcePath))
        {
            throw new InvalidOperationException("Only PNG, JPG, and JPEG signature images are supported.");
        }

        var signaturesFolder = ProjectLayout.SignaturesFolder(project);
        Directory.CreateDirectory(signaturesFolder);
        var finalPath = UniquePath(Path.Combine(signaturesFolder, Path.GetFileName(signatureSourcePath)));
        var tempPath = Path.Combine(signaturesFolder, $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        File.Copy(signatureSourcePath, tempPath, overwrite: true);
        var finalRelativePath = SignatureStore.RelativePath(project.FolderPath, finalPath)
            ?? throw new InvalidOperationException("The imported signature could not be stored inside the project folder.");
        return new StagedSignature(finalRelativePath, finalPath, tempPath, CreatedNewFile: true);
    }

    private static void CommitStagedTemplate(StagedTemplate stagedTemplate)
    {
        if (stagedTemplate.TempPath is null)
        {
            return;
        }

        File.Move(stagedTemplate.TempPath, stagedTemplate.FinalPath, overwrite: true);
    }

    private static void CommitStagedSignature(StagedSignature stagedSignature)
    {
        if (stagedSignature.TempPath is null)
        {
            return;
        }

        File.Move(stagedSignature.TempPath, stagedSignature.FinalPath, overwrite: false);
    }

    private static void CleanupStagedArtifacts(StagedTemplate stagedTemplate, params StagedSignature[] stagedSignatures)
    {
        if (stagedTemplate.TempPath is not null && File.Exists(stagedTemplate.TempPath))
        {
            File.Delete(stagedTemplate.TempPath);
        }

        foreach (var stagedSignature in stagedSignatures)
        {
            if (stagedSignature.TempPath is not null && File.Exists(stagedSignature.TempPath))
            {
                File.Delete(stagedSignature.TempPath);
            }
        }
    }

    private static void TryRestoreProjectJson(string filePath, byte[]? previousBytes)
    {
        if (previousBytes is null)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return;
        }

        File.WriteAllBytes(filePath, previousBytes);
    }

    private static void TryRestoreTemplate(StagedTemplate stagedTemplate, byte[]? previousBytes)
    {
        if (!stagedTemplate.ShouldReplaceExisting)
        {
            return;
        }

        if (previousBytes is null)
        {
            if (File.Exists(stagedTemplate.FinalPath))
            {
                File.Delete(stagedTemplate.FinalPath);
            }

            return;
        }

        File.WriteAllBytes(stagedTemplate.FinalPath, previousBytes);
    }

    private static void TryDeleteCommittedSignature(StagedSignature stagedSignature)
    {
        if (stagedSignature.CreatedNewFile && File.Exists(stagedSignature.FinalPath))
        {
            File.Delete(stagedSignature.FinalPath);
        }
    }

    private static string UniquePath(string target)
    {
        if (!File.Exists(target))
        {
            return target;
        }

        var directory = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        var counter = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name} ({counter++}){extension}");
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private static Project Clone(Project project)
    {
        return new Project
        {
            Name = project.Name,
            Number = project.Number,
            Owner = project.Owner,
            ContractManager = project.ContractManager,
            GeneralContractor = project.GeneralContractor,
            LocationText = project.LocationText,
            LocationLatitude = project.LocationLatitude,
            LocationLongitude = project.LocationLongitude,
            LocationTimeZoneId = project.LocationTimeZoneId,
            FolderPath = project.FolderPath,
            TemplatePath = project.TemplatePath,
            InspectorSignaturePath = project.InspectorSignaturePath,
            ProjectManagerSignaturePath = project.ProjectManagerSignaturePath,
            NextReportNumber = project.NextReportNumber,
            CreatedUtc = project.CreatedUtc,
            RelativeFolderPath = project.RelativeFolderPath,
            RelativeTemplatePath = project.RelativeTemplatePath
        };
    }

    private static void CopyProjectState(Project source, Project destination)
    {
        destination.Name = source.Name;
        destination.Number = source.Number;
        destination.Owner = source.Owner;
        destination.ContractManager = source.ContractManager;
        destination.GeneralContractor = source.GeneralContractor;
        destination.LocationText = source.LocationText;
        destination.LocationLatitude = source.LocationLatitude;
        destination.LocationLongitude = source.LocationLongitude;
        destination.LocationTimeZoneId = source.LocationTimeZoneId;
        destination.FolderPath = source.FolderPath;
        destination.TemplatePath = source.TemplatePath;
        destination.InspectorSignaturePath = source.InspectorSignaturePath;
        destination.ProjectManagerSignaturePath = source.ProjectManagerSignaturePath;
        destination.NextReportNumber = source.NextReportNumber;
        destination.CreatedUtc = source.CreatedUtc;
        destination.RelativeFolderPath = source.RelativeFolderPath;
        destination.RelativeTemplatePath = source.RelativeTemplatePath;
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
            LocationText = project.LocationText,
            LocationLatitude = project.LocationLatitude,
            LocationLongitude = project.LocationLongitude,
            LocationTimeZoneId = project.LocationTimeZoneId,
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
