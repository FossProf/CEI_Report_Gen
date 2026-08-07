using System.IO.Compression;
using System.Security.Cryptography;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

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

    Console.WriteLine("\n== Template validation ==");
    var templateErrors = TemplateValidator.ValidateTemplate(templatePath);
    Assert(templateErrors.Count == 0, "approved template passes preflight validation");
    foreach (var e in templateErrors) Console.WriteLine("    " + e);

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

    Console.WriteLine("\n== Project creation ==");
    Console.WriteLine($"  Inspector sig (stored): {project.InspectorSignaturePath}");
    Console.WriteLine($"  PM sig (stored): {project.ProjectManagerSignaturePath}");
    Assert(project.InspectorSignaturePath.StartsWith("Signatures/", StringComparison.Ordinal), "inspector signature stored relative to project");
    Assert(project.ProjectManagerSignaturePath.StartsWith("Signatures/", StringComparison.Ordinal), "pm signature stored relative to project");
    Assert(File.Exists(project.ResolvedInspectorSignaturePath!), "inspector signature resolves to existing file");
    Assert(File.Exists(project.ResolvedProjectManagerSignaturePath!), "pm signature resolves to existing file");
    Assert(ProjectLayout.IsValidProjectFolder(projectFolder), "project folder recognized");
    Console.WriteLine($"  Initial next report number: {project.NextReportNumber}");

    Console.WriteLine("\n== Signature store ==");
    var sigNames = SignatureStore.ListSignatureFiles(project.FolderPath);
    Assert(sigNames.Count == 2, "two signature files listed");
    var imported = SignatureStore.Import(project.FolderPath, photoFiles[2], replaceIfExists: false);
    Assert(imported is not null, "signature imported");
    var collision = SignatureStore.Import(project.FolderPath, photoFiles[2], replaceIfExists: false);
    Assert(collision is not null && collision != imported, "import collision produces a unique file name");
    var sigNamesAfter = SignatureStore.ListSignatureFiles(project.FolderPath);
    Assert(sigNamesAfter.Count == 4, "four signature files after imports");
    Assert(SignatureStore.Resolve(project.FolderPath, "Signatures/missing.png").Status == SignatureResolveStatus.MissingFile, "missing signature resolved as missing");
    Assert(SignatureStore.Resolve(project.FolderPath, "../outside.png").Status == SignatureResolveStatus.OutsideProject, "traversal rejected");
    Assert(SignatureStore.Resolve(project.FolderPath, @"C:\Windows\System32\foo.png").Status == SignatureResolveStatus.OutsideProject, "absolute path outside project rejected");
    Assert(SignatureStore.Resolve(project.FolderPath, "Signatures/readme.txt").Status == SignatureResolveStatus.UnsupportedExtension, "unsupported extension rejected");

    Console.WriteLine("\n== Signature path traversal rejected by project validation ==");
    var traversalProject = ProjectStore.Load(projectFolder)!;
    traversalProject.InspectorSignaturePath = "../evil.png";
    var traversalErrors = Validation.ValidateProject(traversalProject);
    Assert(traversalErrors.Any(e => e.Contains("outside the project folder", StringComparison.OrdinalIgnoreCase)), "traversal flagged by validation");
    Console.WriteLine("    ok: " + string.Join(" | ", traversalErrors));

    Console.WriteLine("\n== Project round trip ==");
    var reloadedProject = ProjectStore.Load(projectFolder);
    Assert(reloadedProject is not null, "project reloaded");
    Assert(reloadedProject!.InspectorSignaturePath == project.InspectorSignaturePath, "relative signature path survives reload");
    Assert(reloadedProject.NextReportNumber == 1, "next report number still 1 (nothing finalized)");

    Console.WriteLine("\n== Signature UI flow (project-relative dropdown paths) ==");
    var uiProjectFolder = Path.Combine(workspace, "projects", "UI Flow Project");
    var uiImported = SignatureStore.Import(uiProjectFolder, photoFiles[3], replaceIfExists: false);
    Assert(uiImported is not null, "ui flow: signature imported into project folder");
    var uiImportedPm = SignatureStore.Import(uiProjectFolder, photoFiles[4], replaceIfExists: false);
    Assert(uiImportedPm is not null, "ui flow: pm signature imported into project folder");
    var uiStoredInspector = SignatureStore.SignatureRelativePath(Path.GetFileName(uiImported!)!);
    var uiStoredPm = SignatureStore.SignatureRelativePath(Path.GetFileName(uiImportedPm!)!);
    Project uiProject;
    try
    {
        uiProject = ProjectStore.Create(
            uiProjectFolder, "UI Flow Project", "99", "Owner", "CM", "GC",
            templatePath, uiStoredInspector, uiStoredPm);
    }
    catch (Exception ex)
    {
        throw new Exception($"UI flow project creation failed: {ex.Message}");
    }
    Assert(uiProject.InspectorSignaturePath == uiStoredInspector, "ui flow: inspector signature path stored as-is");
    Assert(SignatureStore.Resolve(uiProjectFolder, uiStoredInspector).Status == SignatureResolveStatus.Valid, "ui flow: inspector signature resolves as valid");
    Assert(SignatureStore.Resolve(uiProjectFolder, uiStoredPm).Status == SignatureResolveStatus.Valid, "ui flow: pm signature resolves as valid");
    Assert(Validation.ValidateProject(uiProject).Count == 0, "ui flow: project validates clean");
    Console.WriteLine("    ok: project created from dropdown-relative signature paths and validates clean");

    Console.WriteLine("\n== Project load with relative folder path ==");
    var relativeJsonDir = Path.Combine(workspace, "portable_project");
    Directory.CreateDirectory(relativeJsonDir);
    File.WriteAllText(Path.Combine(relativeJsonDir, "project.json"), """
    {
      "name": "Portable",
      "number": "1",
      "owner": "O",
      "contractManager": "CM",
      "generalContractor": "GC",
      "folderPath": ".",
      "templatePath": "Template.docx",
      "inspectorSignaturePath": "Signatures/missing.png",
      "projectManagerSignaturePath": "Signatures/missing.png",
      "nextReportNumber": 1
    }
    """);
    var portableProject = ProjectStore.Load(Path.Combine(relativeJsonDir, "project.json"));
    Assert(portableProject is not null, "portable project loaded");
    Assert(Path.IsPathRooted(portableProject!.FolderPath), "relative folder path normalized to absolute");
    Assert(
        portableProject.FolderPath.Equals(Path.GetFullPath(relativeJsonDir), StringComparison.OrdinalIgnoreCase),
        "relative folder resolves next to project.json");
    Assert(
        portableProject.TemplatePath.Equals(Path.Combine(portableProject.FolderPath, "Template.docx"), StringComparison.OrdinalIgnoreCase),
        "relative template path resolved against project folder");
    Console.WriteLine("    ok: relative folderPath/templatePath resolved at load");

    foreach (var w in new[] { "Sunny", "Partly Cloudy", "Overcast", "Rainy" })
    {
        Assert(WeatherOptions.IsValid(w), $"weather option '{w}' is valid");
    }

    foreach (var w in new[] { "", "   ", "Snowing", "Clear Skies" })
    {
        Assert(!WeatherOptions.IsValid(w), $"weather value '{w}' is rejected");
    }

    Console.WriteLine("\n== 0-4 photo generation ==");
    var baselineErrors = ValidateDocument(templatePath);
    Console.WriteLine($"  Original template validation errors (pre-existing): {baselineErrors.Count}");

    InspectionReport? finalizeReport = null;
    GenerationResult? finalizeResult = null;

    for (var photoCount = 0; photoCount <= 4; photoCount++)
    {
        var report = MakeReport(project, photoCount + 1, photoCount, "Sunny", photoFiles);
        var result = ReportGenerator.GenerateDraft(project, report);
        Console.WriteLine($"  {photoCount} photo(s): generated {result.OutputPath}");
        Assert(File.Exists(result.OutputPath), $"{photoCount}-photo docx output exists");
        VerifyGeneratedDocument(result.OutputPath, photoCount, baselineErrors, templatePath);

        if (photoCount == 3)
        {
            finalizeReport = report;
            finalizeResult = result;
        }
    }

    Console.WriteLine("\n== Invalid weather rejected (failure safety) ==");
    var beforeFailure = project.NextReportNumber;
    var invalidWeatherReport = MakeReport(project, 6, 1, "Snowing", photoFiles);
    ExpectGenerationFailure(project, invalidWeatherReport, GenerationStage.ValidateReport,
        e => e.Contains("approved options", StringComparison.OrdinalIgnoreCase));
    Assert(project.NextReportNumber == beforeFailure, "failed generation does not increment report number");
    var failureFolder = ProjectLayout.ReportFolder(project, invalidWeatherReport.Number);
    Assert(!Directory.Exists(failureFolder) || !Directory.EnumerateFiles(failureFolder).Any(),
        "failed generation leaves no partial output files");

    Console.WriteLine("\n== No-overwrite protection ==");
    var overwriteReport = MakeReport(project, 2, 1, "Sunny", photoFiles);
    ExpectGenerationFailure(project, overwriteReport, GenerationStage.CopyTemplate,
        e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine("\n== Template validation surfaced at generation ==");
    var badTemplate = Path.Combine(workspace, "broken_template.docx");
    File.Copy(templatePath, badTemplate);
    using (var broken = WordprocessingDocument.Open(badTemplate, true))
    {
        var firstTag = broken.MainDocumentPart!.Document.Body!
            .Descendants<SdtBlock>()
            .First(b => b.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value is not null);
        firstTag.Remove();
        broken.MainDocumentPart.Document.Save();
    }

    var brokenProject = ProjectStore.Load(projectFolder)!;
    brokenProject.TemplatePath = badTemplate;
    var brokenReport = MakeReport(brokenProject, 7, 1, "Sunny", photoFiles);
    ExpectGenerationFailure(brokenProject, brokenReport, GenerationStage.ValidateTemplate,
        e => e.Contains("signature area", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine("\n== Missing signature rejected ==");
    var missingSigProject = ProjectStore.Load(projectFolder)!;
    missingSigProject.InspectorSignaturePath = string.Empty;
    var missingSigReport = MakeReport(missingSigProject, 8, 0, "Sunny", photoFiles);
    ExpectGenerationFailure(missingSigProject, missingSigReport, GenerationStage.ValidateProject,
        e => e.Contains("Inspector signature", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine("\n== Draft save and finalize (3-photo report) ==");
    ReportGenerator.SaveDraft(project, finalizeReport!);
    Assert(File.Exists(ProjectLayout.ReportFilePath(project, finalizeReport!.Number)), "report.json draft exists");
    var photosFolder = ProjectLayout.ReportPhotosFolder(project, finalizeReport.Number);
    Assert(Directory.GetFiles(photosFolder).Length == 3, "3 photos copied to report folder");

    ReportGenerator.FinalizeReport(project, finalizeReport, finalizeResult!.OutputPath);
    Assert(finalizeReport.Status == ReportStatus.Final, "report status is Final");

    var finalProject = ProjectStore.Load(projectFolder);
    Assert(finalProject!.NextReportNumber == 2, "next report number incremented to 2 after finalize");
    var savedReport = ReportStore.LoadReport(finalProject, finalizeReport.Number);
    Assert(savedReport is not null, "report reloaded from disk");
    Assert(savedReport!.Status == ReportStatus.Final, "reloaded report is Final");
    Assert(savedReport.Photos.Count == 3, "reloaded report has 3 photos");
    Console.WriteLine($"  Report {savedReport.Number} status: {savedReport.Status}, photos: {savedReport.Photos.Count}");

    var allReports = ReportStore.LoadAllReports(finalProject);
    Assert(allReports.Count == 1, "one report.json listed in project (only the finalized report was saved)");

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

static InspectionReport MakeReport(Project project, int number, int photoCount, string weather, string[] mediaFiles)
{
    var photos = new List<Photo>();
    for (var i = 0; i < photoCount; i++)
    {
        photos.Add(new Photo { SourcePath = mediaFiles[i % mediaFiles.Length], Caption = "Inspection photo " + (i + 1) + " documentation." });
    }

    return new InspectionReport
    {
        Number = number,
        Date = new DateTime(2026, 8, 5),
        Temperature = "92",
        Weather = weather,
        Locations = "Building A - 2nd Floor Framing",
        Inspectors = "Anthony Wintergerst",
        PersonnelOnSite = "John Smith (Carpenter Foreman), Bob Jones (Ironworker)",
        DescriptionOfWork = "Review of steel beam connections and column splices at column line 3.",
        DrawingsReviewed = "S-201, S-202, D-101",
        Observations = "All welds visually inspected and found acceptable.",
        NewDiscrepancies = "None observed.",
        PreviousDiscrepancies = "N/A",
        Photos = photos
    };
}

static void ExpectGenerationFailure(Project project, InspectionReport report, GenerationStage expectedStage, Func<string, bool> messageCheck)
{
    try
    {
        ReportGenerator.GenerateDraft(project, report);
        throw new InvalidOperationException($"Expected generation to fail at {expectedStage} but it succeeded.");
    }
    catch (GenerationException ex)
    {
        if (ex.Stage != expectedStage)
        {
            throw new InvalidOperationException($"Expected stage {expectedStage} but got {ex.Stage}. Errors: {string.Join(" | ", ex.Errors)}");
        }

        if (!ex.Errors.Any(messageCheck))
        {
            throw new InvalidOperationException($"Stage {ex.Stage} errors did not match: {string.Join(" | ", ex.Errors)}");
        }

        Console.WriteLine($"  ok: failed at {ex.Stage}: {ex.Errors[0]}");
    }
}

static void VerifyGeneratedDocument(string docxPath, int photoCount, List<string> baselineErrors, string templatePath)
{
    using var doc = WordprocessingDocument.Open(docxPath, false);
    var mainPart = doc.MainDocumentPart!;
    var body = mainPart.Document.Body!;
    var bodyText = string.Concat(body.Descendants<Text>().Select(t => t.Text));

    Assert(!bodyText.Contains("{project.", StringComparison.Ordinal), "no unresolved template placeholders");

    if (photoCount == 0)
    {
        Assert(!bodyText.Contains("PHOTO DOCUMENTATION", StringComparison.OrdinalIgnoreCase), "zero-photo report removes photo section heading");
        Assert(!bodyText.Contains("Repeat this photo page", StringComparison.OrdinalIgnoreCase), "zero-photo report removes repeat instruction");
        Assert(!body.Descendants<Table>().Any(t => t.Descendants<Paragraph>().Any(p => PhotoTable.ParagraphText(p).Contains(".caption}"))),
            "zero-photo report removes the photo table");
    }
    else
    {
        Assert(bodyText.Contains("Photo " + photoCount + ": "), $"photo {photoCount} caption prefix renumbered");
        Assert(bodyText.Contains("Inspection photo " + photoCount + " documentation."), $"photo {photoCount} caption filled");
    }

    Assert(bodyText.Contains("Demo Project"), "project name filled");
    Assert(bodyText.Contains("24-1042"), "project number filled");
    Assert(bodyText.Contains("August 5, 2026"), "inspection date filled");
    Assert(bodyText.Contains("Acme Builders LLC"), "general contractor filled");
    Assert(bodyText.Contains("Anthony Wintergerst"), "inspector filled");

    var blips = body.Descendants<A.Blip>().ToList();
    Assert(blips.Count == photoCount + 2, $"expected {photoCount + 2} blips (photos + 2 signatures), found {blips.Count}");

    foreach (var blip in blips)
    {
        var part = mainPart.GetPartById(blip.Embed!);
        Assert(part is ImagePart, $"blip {blip.Embed} resolves to image part");
    }

    var sdtBlocks = body.Descendants<SdtBlock>()
        .Where(b => b.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value is not null)
        .ToList();
    Assert(sdtBlocks.Count == 2, "both signature content controls preserved in output");

    AssertLogosUnchanged(templatePath, docxPath);

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
    Console.WriteLine($"  Document verified: {blips.Count} images embedded, no unresolved placeholders.");
}

static void AssertLogosUnchanged(string templatePath, string outputPath)
{
    using var template = WordprocessingDocument.Open(templatePath, false);
    using var output = WordprocessingDocument.Open(outputPath, false);

    var templateHashes = HeaderImageHashes(template.MainDocumentPart!.HeaderParts);
    var outputHashes = HeaderImageHashes(output.MainDocumentPart!.HeaderParts);
    Assert(templateHashes.Count > 0, "template headers contain logo images");
    Assert(templateHashes.SequenceEqual(outputHashes), "CEI header logos unchanged by generation");
}

static List<string> HeaderImageHashes(IEnumerable<HeaderPart> headerParts)
{
    var hashes = new List<string>();
    foreach (var headerPart in headerParts)
    {
        foreach (var blip in headerPart.Header.Descendants<A.Blip>())
        {
            var imagePart = (ImagePart)headerPart.GetPartById(blip.Embed!);
            using var stream = imagePart.GetStream();
            hashes.Add(Convert.ToHexString(SHA256.HashData(stream)));
        }
    }

    return hashes.OrderBy(h => h).ToList();
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
