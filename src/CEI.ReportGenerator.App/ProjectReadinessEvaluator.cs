using System.IO;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;

namespace CEI.ReportGenerator.App;

public static class ProjectReadinessEvaluator
{
    public static ProjectReadiness Evaluate(Project project)
    {
        var templateIssues = EvaluateTemplate(project).ToList();
        var inspectorIssues = EvaluateSignature("Inspector", SignatureStore.Resolve(project.FolderPath, project.InspectorSignaturePath)).ToList();
        var projectManagerIssues = EvaluateSignature("Project Manager", SignatureStore.Resolve(project.FolderPath, project.ProjectManagerSignaturePath)).ToList();
        var configurationIssues = Validation.ValidateProject(project);

        var issues = templateIssues
            .Concat(inspectorIssues)
            .Concat(projectManagerIssues)
            .Concat(configurationIssues)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ProjectReadiness
        {
            TemplateReady = templateIssues.Count == 0,
            InspectorSignatureReady = inspectorIssues.Count == 0,
            ProjectManagerSignatureReady = projectManagerIssues.Count == 0,
            ProjectConfigurationReady = configurationIssues.Count == 0,
            TemplateIssues = templateIssues,
            InspectorSignatureIssues = inspectorIssues,
            ProjectManagerSignatureIssues = projectManagerIssues,
            ProjectConfigurationIssues = configurationIssues,
            Issues = issues
        };
    }

    private static IEnumerable<string> EvaluateTemplate(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.TemplatePath))
        {
            return new[] { "Approved template path is missing." };
        }

        if (!File.Exists(project.TemplatePath))
        {
            return new[] { $"Approved template file does not exist: {Path.GetFileName(project.TemplatePath)}" };
        }

        return TemplateValidator.ValidateTemplate(project.TemplatePath);
    }

    private static IEnumerable<string> EvaluateSignature(string role, SignatureResolveResult resolved)
    {
        return resolved.Status switch
        {
            SignatureResolveStatus.Valid => Array.Empty<string>(),
            SignatureResolveStatus.Empty => new[] { $"{role} signature is missing." },
            SignatureResolveStatus.MissingFile => new[] { $"{role} signature file is missing." },
            SignatureResolveStatus.UnsupportedExtension => new[] { $"{role} signature file type is not supported. Use PNG, JPG, or JPEG." },
            SignatureResolveStatus.OutsideProject => new[] { $"The {role} signature path resolves outside the project folder." },
            _ => new[] { $"{role} signature is invalid." }
        };
    }
}
