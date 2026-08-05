using System.IO.Compression;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using DocumentFormat.OpenXml.Packaging;

var root = FindRepoRoot();
var templatePath = Path.Combine(root, "templates", "CEI_Base_Template_Refined.docx");

if (!File.Exists(templatePath))
{
    Console.WriteLine($"FAIL: template not found at {templatePath}");
    return 1;
}

var workspace = Path.Combine(Path.GetTempPath(), "cei_smoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
Console.WriteLine($"Workspace: {workspace}");

var keepWorkspace = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CEI_KEEP_WORKSPACE"));

try
{
    var photoDir = Path.Combine(workspace, "sample_photos");
    Directory.CreateDirectory(photoDir);
    ExtractTemplateMedia(templatePath, photoDir);
    var photoFiles = Directory.GetFiles(photoDir).OrderBy(f => f).ToArray();
    Console.WriteLine($"Extracted {photoFiles.Length} sample images for photos.");

    var projectFolder = Path.Combine(workspace, "projects", "Demo Project");
    var project = ProjectStore.Create(
        projectFolder,
        name: "Demo Project",
        number: "24-1042",
        owner: "City of Springdale",
        contractManager: "Jane Doe",
        generalContractor: "Acme Builders LLC",
        templateSourcePath: templatePath,
        inspectorSignaturePath: photoFiles[0],
        projectManagerSignaturePath: photoFiles[1]);

    Console.WriteLine($"Project created: {project.FilePath}");
    Console.WriteLine($"  Template copied: {project.TemplatePath}");
    Console.WriteLine($"  Inspector sig: {project.InspectorSignaturePath}");
    Console.WriteLine($"  PM sig: {project.ProjectManagerSignaturePath}");
    Console.WriteLine($"  Initial next report number: {project.NextReportNumber}");
    Assert(File.Exists(project.TemplatePath), "project template copy exists");
    Assert(File.Exists(project.InspectorSignaturePath), "inspector signature copy exists");
    Assert(ProjectLayout.IsValidProjectFolder(projectFolder), "project folder recognized");

    var report = new InspectionReport
    {
        Number = project.NextReportNumber,
        Date = new DateTime(2026, 8, 5),
        Temperature = "92",
        Weather = "Sunny",
        Locations = "Building A - 2nd Floor Framing",
        Inspectors = "Anthony Wintergerst",
        PersonnelOnSite = "John Smith (Carpenter Foreman), Bob Jones (Ironworker)",
        DescriptionOfWork = "Review of steel beam connections and column splices at column line 3.",
        DrawingsReviewed = "S-201, S-202, D-101",
        Observations = "All welds visually inspected and found acceptable.",
        NewDiscrepancies = "None observed.",
        PreviousDiscrepancies = "N/A",
        Photos = new List<Photo>
        {
            new() { SourcePath = photoFiles[2], Caption = "South column splice - completed weld." },
            new() { SourcePath = photoFiles[3], Caption = "Beam to column connection at line 3." },
            new() { SourcePath = photoFiles[0], Caption = "Overall framing view from north." },
        }
    };

    Console.WriteLine("\nGenerating report draft...");
    var originalErrors = ValidateDocument(templatePath);
    Console.WriteLine($"  Original template validation errors (pre-existing): {originalErrors.Count}");
    var result = ReportGenerator.GenerateDraft(project, report);
    Console.WriteLine($"Generated: {result.OutputPath}");
    Assert(File.Exists(result.OutputPath), "docx output exists");

    VerifyGeneratedDocument(result.OutputPath, photoFiles, 3, originalErrors);

    ReportGenerator.SaveDraft(project, report);
    Assert(File.Exists(ProjectLayout.ReportFilePath(project, report.Number)), "report.json draft exists");
    var photosFolder = ProjectLayout.ReportPhotosFolder(project, report.Number);
    Assert(Directory.GetFiles(photosFolder).Length == 3, "3 photos copied to report folder");
    Console.WriteLine("Draft saved (report not yet finalized).");

    Console.WriteLine("\nFinalizing report...");
    ReportGenerator.FinalizeReport(project, report, result.OutputPath);
    Assert(report.Status == ReportStatus.Final, "report status is Final");

    var reloadedProject = ProjectStore.Load(projectFolder);
    Assert(reloadedProject is not null, "project reloaded from disk");
    Assert(reloadedProject!.NextReportNumber == 2, "next report number incremented to 2");
    Console.WriteLine($"  Next report number after finalize: {reloadedProject.NextReportNumber}");

    var savedReport = ReportStore.LoadReport(reloadedProject, 1);
    Assert(savedReport is not null, "report reloaded from disk");
    Assert(savedReport!.Status == ReportStatus.Final, "reloaded report is Final");
    Assert(savedReport.Photos.Count == 3, "reloaded report has 3 photos");
    Console.WriteLine($"  Report {savedReport.Number} status: {savedReport.Status}, photos: {savedReport.Photos.Count}");

    var allReports = ReportStore.LoadAllReports(reloadedProject);
    Assert(allReports.Count == 1, "one report listed in project");

    Console.WriteLine("\nALL SMOKE TESTS PASSED");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex}");
    return 1;
}
finally
{
    if (keepWorkspace)
    {
        Console.WriteLine($"Workspace preserved: {workspace}");
    }
    else
    {
        try
        {
            Directory.Delete(workspace, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "CEI_Report_Gen.sln")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static void ExtractTemplateMedia(string docxPath, string destination)
{
    using var zip = ZipFile.OpenRead(docxPath);
    foreach (var entry in zip.Entries)
    {
        if (entry.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(entry.FullName);
            entry.ExtractToFile(Path.Combine(destination, name), overwrite: true);
        }
    }
}

static void VerifyGeneratedDocument(string docxPath, string[] photoFiles, int photoCount, List<string> baselineErrors)
{
    using var doc = WordprocessingDocument.Open(docxPath, false);
    var mainPart = doc.MainDocumentPart!;
    var bodyText = string.Concat(mainPart.Document.Body!.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>().Select(t => t.Text));

    Assert(!bodyText.Contains('{'), "no leftover placeholders in document body");
    Assert(!bodyText.Contains('}'), "no leftover closing braces in document body");
    Assert(bodyText.Contains("Demo Project"), "project name filled");
    Assert(bodyText.Contains("24-1042"), "project number filled");
    Assert(bodyText.Contains("August 5, 2026"), "inspection date filled");
    Assert(bodyText.Contains("Acme Builders LLC"), "general contractor filled");
    Assert(bodyText.Contains("Anthony Wintergerst"), "inspector filled");
    Assert(bodyText.Contains("Photo 3: "), "third photo caption prefix renumbered");
    Assert(bodyText.Contains("Overall framing view from north."), "third photo caption filled");

    var blips = mainPart.Document.Body.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().ToList();
    Assert(blips.Count == photoCount + 2, $"expected {photoCount + 2} blips (photos + 2 signatures), found {blips.Count}");

    foreach (var blip in blips)
    {
        var part = mainPart.GetPartById(blip.Embed!);
        Assert(part is ImagePart, $"blip {blip.Embed} resolves to image part");
    }

    var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator();
    var validationErrors = validator.Validate(mainPart.Document)
        .Select(e => NormalizePath(e.Path?.XPath) + ": " + e.Description)
        .Distinct()
        .ToList();
    var newErrors = validationErrors.Where(e => !baselineErrors.Contains(e)).ToList();
    if (newErrors.Count > 0)
    {
        Console.WriteLine("  NEW OpenXml validation errors (not in template):");
        foreach (var error in newErrors.Take(20))
        {
            Console.WriteLine($"    {error}");
        }
    }

    Assert(newErrors.Count == 0, "generated document introduces no new OpenXml validation errors");

    Console.WriteLine($"  Document verified: body text filled, {blips.Count} images embedded.");
}

static List<string> ValidateDocument(string docxPath)
{
    using var doc = WordprocessingDocument.Open(docxPath, false);
    var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator();
    return validator.Validate(doc.MainDocumentPart!.Document)
        .Select(e => NormalizePath(e.Path?.XPath) + ": " + e.Description)
        .Distinct()
        .ToList();
}

static string NormalizePath(string? xpath)
{
    if (string.IsNullOrEmpty(xpath))
    {
        return string.Empty;
    }

    return System.Text.RegularExpressions.Regex.Replace(xpath, @"\[\d+\]", "[N]");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Assertion failed: {message}");
    }

    Console.WriteLine($"  ok: {message}");
}
