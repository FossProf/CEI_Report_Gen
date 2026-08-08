using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class Validation
{
    public static List<string> ValidateProject(Project project)
    {
        var errors = new List<string>();
        AddRequired(errors, "Project name", project.Name);
        AddRequired(errors, "Cornerstone project number", project.Number);
        AddRequired(errors, "Owner", project.Owner);
        AddRequired(errors, "Contract manager", project.ContractManager);
        AddRequired(errors, "General contractor", project.GeneralContractor);

        if (string.IsNullOrWhiteSpace(project.FolderPath) || !Directory.Exists(project.FolderPath))
        {
            errors.Add("Project folder does not exist.");
        }

        if (string.IsNullOrWhiteSpace(project.TemplatePath) || !File.Exists(project.TemplatePath))
        {
            errors.Add("Approved Word template is missing.");
        }

        AddSignatureError(errors, "Inspector", SignatureStore.Resolve(project.FolderPath, project.InspectorSignaturePath));
        AddSignatureError(errors, "Project Manager", SignatureStore.Resolve(project.FolderPath, project.ProjectManagerSignaturePath));

        return errors;
    }

    private static void AddSignatureError(List<string> errors, string role, SignatureResolveResult resolved)
    {
        if (resolved.Status == SignatureResolveStatus.Valid)
        {
            return;
        }

        if (resolved.Status == SignatureResolveStatus.OutsideProject)
        {
            errors.Add($"The {role} signature path resolves outside the project folder.");
        }
        else if (resolved.Status == SignatureResolveStatus.UnsupportedExtension)
        {
            errors.Add($"The {role} signature file type is not supported. Use PNG, JPG, or JPEG.");
        }
        else
        {
            errors.Add($"{role} signature file is missing.");
        }
    }

    public static List<string> ValidateReportForGeneration(InspectionReport report)
    {
        var errors = new List<string>();
        AddRequired(errors, "Inspection date", report.Date == default ? string.Empty : report.Date.ToString("d"));
        AddRequired(errors, "Weather", report.Weather);
        if (!string.IsNullOrWhiteSpace(report.Weather) && !WeatherOptions.IsValid(report.Weather))
        {
            errors.Add("Weather must be one of the approved options.");
        }

        AddRequired(errors, "Location(s)", report.Locations);
        AddRequired(errors, "Cornerstone Inspector(s)", report.Inspectors);
        AddRequired(errors, "Personnel on site", report.PersonnelOnSite);
        AddRequired(errors, "Description of work inspected", report.DescriptionOfWork);
        AddRequired(errors, "Drawing sheets and sections", report.DrawingsReviewed);

        for (var i = 0; i < report.Photos.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(report.Photos[i].SourcePath) || !File.Exists(report.Photos[i].SourcePath))
            {
                errors.Add($"Photo {i + 1} source image file is missing.");
                continue;
            }

            try
            {
                _ = ImagePartManager.GetContentType(report.Photos[i].SourcePath);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Photo {i + 1} is not a supported image. {ex.Message}");
            }
        }

        return errors;
    }

    private static void AddRequired(List<string> errors, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
        }
    }
}
