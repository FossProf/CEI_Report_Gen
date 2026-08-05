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

        if (string.IsNullOrWhiteSpace(project.InspectorSignaturePath) || !File.Exists(project.InspectorSignaturePath))
        {
            errors.Add("Special Inspector signature image is missing.");
        }

        if (string.IsNullOrWhiteSpace(project.ProjectManagerSignaturePath) || !File.Exists(project.ProjectManagerSignaturePath))
        {
            errors.Add("Project Manager signature image is missing.");
        }

        return errors;
    }

    public static List<string> ValidateReportForGeneration(InspectionReport report)
    {
        var errors = new List<string>();
        AddRequired(errors, "Inspection date", report.Date == default ? string.Empty : report.Date.ToString("d"));
        AddRequired(errors, "Weather", report.Weather);
        AddRequired(errors, "Location(s)", report.Locations);
        AddRequired(errors, "Cornerstone Inspector(s)", report.Inspectors);
        AddRequired(errors, "Personnel on site", report.PersonnelOnSite);
        AddRequired(errors, "Description of work inspected", report.DescriptionOfWork);
        AddRequired(errors, "Drawing sheets and sections", report.DrawingsReviewed);

        if (report.Photos.Count == 0)
        {
            errors.Add("At least one photograph is required.");
        }

        for (var i = 0; i < report.Photos.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(report.Photos[i].Caption))
            {
                errors.Add($"Photo {i + 1} requires a caption.");
            }

            if (string.IsNullOrWhiteSpace(report.Photos[i].SourcePath) || !File.Exists(report.Photos[i].SourcePath))
            {
                errors.Add($"Photo {i + 1} source image file is missing.");
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
