using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

var root = FindRepoRoot();
var templatePath = Environment.GetEnvironmentVariable("CEI_TEMPLATE_PATH");
if (string.IsNullOrWhiteSpace(templatePath))
{
    templatePath = Path.Combine(root, "templates", "CEI_Base_Template_Refined.docx");
}

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

    Console.WriteLine("\n== Next report number synchronization ==");
    var syncProject = ProjectStore.Load(projectFolder)!;
    syncProject.NextReportNumber = 2;
    ProjectStore.Save(syncProject);
    Directory.CreateDirectory(ProjectLayout.ReportFolder(syncProject, 2));
    File.WriteAllText(ProjectLayout.ReportFilePath(syncProject, 2), "{}");
    Directory.CreateDirectory(ProjectLayout.ReportFolder(syncProject, 3));
    File.WriteAllText(ProjectLayout.ReportFilePath(syncProject, 3), "{}");
    Assert(ProjectStore.SynchronizeNextReportNumber(syncProject) == 4, "stale next report self-corrects to 4");
    var syncReloaded = ProjectStore.Load(projectFolder)!;
    Assert(syncReloaded.NextReportNumber == 4, "synchronized next report persists");
    syncReloaded.NextReportNumber = 8;
    ProjectStore.Save(syncReloaded);
    Assert(ProjectStore.SynchronizeNextReportNumber(syncReloaded) == 8, "higher stored next report remains authoritative");
    Directory.Delete(ProjectLayout.ReportFolder(syncReloaded, 2), recursive: true);
    Directory.Delete(ProjectLayout.ReportFolder(syncReloaded, 3), recursive: true);
    syncReloaded.NextReportNumber = 1;
    ProjectStore.Save(syncReloaded);

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

    ProjectStore.Save(portableProject);
    var savedJson = File.ReadAllText(Path.Combine(relativeJsonDir, "project.json"));
    var savedDoc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(savedJson);
    Assert(
        savedDoc.GetProperty("folderPath").GetString() == ".",
        "save preserves relative folderPath");
    Assert(
        savedDoc.GetProperty("templatePath").GetString() == "Template.docx",
        "save preserves relative templatePath");
    Console.WriteLine("    ok: save keeps relative folderPath/templatePath portable");

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

    Console.WriteLine("\n== Preview can be regenerated without accepting ==");
    var previewReport = MakeReport(project, 20, 1, "Sunny", photoFiles);
    var previewResult1 = ReportGenerator.GenerateDraft(project, previewReport);
    var previewText1 = ReadBodyText(previewResult1.OutputPath);
    previewReport.Observations = "Updated observations after review.";
    var previewResult2 = ReportGenerator.GenerateDraft(project, previewReport);
    var previewText2 = ReadBodyText(previewResult2.OutputPath);
    Assert(previewResult1.OutputPath == previewResult2.OutputPath, "preview path is stable for repeated generate");
    Assert(File.Exists(previewResult2.OutputPath), "preview exists after regenerate");
    Assert(previewText1.Contains("All welds visually inspected", StringComparison.Ordinal), "initial preview contains original observations");
    Assert(previewText2.Contains("Updated observations after review.", StringComparison.Ordinal), "regenerated preview reflects edited observations");

    Console.WriteLine("\n== Empty photo captions omit colon and stay centered ==");
    var noCaptionReport = MakeReport(project, 9, 2, "Sunny", photoFiles);
    noCaptionReport.Photos[0].Caption = string.Empty;
    noCaptionReport.Photos[1].Caption = string.Empty;
    var noCaptionResult = ReportGenerator.GenerateDraft(project, noCaptionReport);
    using (var doc = WordprocessingDocument.Open(noCaptionResult.OutputPath, false))
    {
        var noCaptionBody = doc.MainDocumentPart!.Document.Body!;
        var noCaptionText = string.Concat(noCaptionBody.Descendants<Text>().Select(t => t.Text));
        Assert(!noCaptionText.Contains("Photo 1:"), "empty caption omits colon after Photo 1");
        Assert(!noCaptionText.Contains("Photo 2:"), "empty caption omits colon after Photo 2");
        Assert(noCaptionText.Contains("Photo 1") && noCaptionText.Contains("Photo 2"), "photo labels remain when captions empty");
        foreach (var captionParagraph in noCaptionBody.Descendants<Paragraph>()
            .Where(p => Regex.IsMatch(p.InnerText, @"^Photo\s+\d+$")))
        {
            var justification = captionParagraph.ParagraphProperties?.Justification?.Val?.Value;
            Assert(justification == JustificationValues.Center, $"caption '{captionParagraph.InnerText}' is centered under the photo");
        }
    }

    Console.WriteLine("\n== Invalid weather rejected (failure safety) ==");
    var beforeFailure = project.NextReportNumber;
    var invalidWeatherReport = MakeReport(project, 6, 1, "Snowing", photoFiles);
    ExpectGenerationFailure(project, invalidWeatherReport, GenerationStage.ValidateReport,
        e => e.Contains("approved options", StringComparison.OrdinalIgnoreCase));
    Assert(project.NextReportNumber == beforeFailure, "failed generation does not increment report number");
    var failureFolder = ProjectLayout.ReportFolder(project, invalidWeatherReport.Number);
    Assert(!Directory.Exists(failureFolder) || !Directory.EnumerateFileSystemEntries(failureFolder, "*", SearchOption.AllDirectories).Any(),
        "failed generation leaves no partial output files");

    Console.WriteLine("\n== Output validation failure leaves no preview doc ==");
    var unresolvedTemplate = Path.Combine(workspace, "unresolved_template.docx");
    File.Copy(templatePath, unresolvedTemplate);
    using (var invalid = WordprocessingDocument.Open(unresolvedTemplate, true))
    {
        invalid.MainDocumentPart!.Document.Body!.Append(new Paragraph(new Run(new Text("{project.unknown}"))));
        invalid.MainDocumentPart.Document.Save();
    }

    var invalidOutputProject = ProjectStore.Load(projectFolder)!;
    invalidOutputProject.TemplatePath = unresolvedTemplate;
    var invalidOutputReport = MakeReport(invalidOutputProject, 30, 0, "Sunny", photoFiles);
    ExpectGenerationFailure(invalidOutputProject, invalidOutputReport, GenerationStage.ValidateOutput,
        e => e.Contains("unresolved template placeholders", StringComparison.OrdinalIgnoreCase));
    Assert(!File.Exists(ProjectLayout.ReportPreviewPath(invalidOutputProject, invalidOutputReport.Number)),
        "failed output validation leaves no preview doc");

    Console.WriteLine("\n== Template validation surfaced at generation ==");
    var badTemplate = Path.Combine(workspace, "broken_template.docx");
    File.Copy(templatePath, badTemplate);
    using (var broken = WordprocessingDocument.Open(badTemplate, true))
    {
        var firstTag = broken.MainDocumentPart!.Document.Body!
            .Descendants<SdtElement>()
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
    finalizeReport!.Photos = new[]
    {
        photoFiles[0], // image1.png
        photoFiles[2], // image3.png (distinct content from image1)
        photoFiles[4]  // image5.jpeg
    }.Select(p => new Photo { SourcePath = p, Caption = "Site photo documentation." }).ToList();
    ReportGenerator.SaveDraft(project, finalizeReport);
    Assert(File.Exists(ProjectLayout.ReportFilePath(project, finalizeReport!.Number)), "report.json draft exists");
    var photosFolder = ProjectLayout.ReportPhotosFolder(project, finalizeReport.Number);
    Assert(Directory.GetFiles(photosFolder).Length == 3, "3 photos copied to report folder");
    var storedNames = Directory.GetFiles(photosFolder).Select(Path.GetFileName).OrderBy(n => n).ToArray();
    var sourceNames = finalizeReport.Photos.Select(p => Path.GetFileName(p.SourcePath)).OrderBy(n => n).ToArray();
    Assert(storedNames.SequenceEqual(sourceNames), "stored photos keep original file names (order-independent)");

    var finalReportPath = ProjectLayout.FinalReportPath(project, finalizeReport.Number);
    Assert(!File.Exists(finalReportPath), "final report file does not exist before accept");
    ReportGenerator.FinalizeReport(project, finalizeReport, finalizeResult!.OutputPath);
    Assert(finalizeReport.Status == ReportStatus.Final, "report status is Final");
    Assert(File.Exists(finalReportPath), "final report file exists after accept");
    Assert(!File.Exists(ProjectLayout.ReportPreviewPath(project, finalizeReport.Number)), "preview cleaned up after finalization");

    var finalProject = ProjectStore.Load(projectFolder);
    Assert(finalProject!.NextReportNumber == 5, $"next report number advances to finalized report + 1 (expected 5, got {finalProject.NextReportNumber})");
    var savedReport = ReportStore.LoadReport(finalProject, finalizeReport.Number);
    Assert(savedReport is not null, "report reloaded from disk");
    Assert(savedReport!.Status == ReportStatus.Final, "reloaded report is Final");
    Assert(savedReport.Photos.Count == 3, "reloaded report has 3 photos");
    Console.WriteLine($"  Report {savedReport.Number} status: {savedReport.Status}, photos: {savedReport.Photos.Count}");

    var allReports = ReportStore.LoadAllReports(finalProject);
    Assert(allReports.Reports.Count == 1, "one report.json listed in project (only the finalized report was saved)");

    Console.WriteLine("\n== Final DOCX collision never overwrites ==");
    var collisionProject = ProjectStore.Create(
        Path.Combine(workspace, "collision_project"), "Collision", "13", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var collisionReport = MakeReport(collisionProject, 1, 1, "Sunny", photoFiles);
    var collisionPreview = ReportGenerator.GenerateDraft(collisionProject, collisionReport);
    var collisionFinalPath = ProjectLayout.FinalReportPath(collisionProject, 1);
    Directory.CreateDirectory(Path.GetDirectoryName(collisionFinalPath)!);
    File.WriteAllText(collisionFinalPath, "existing final report");
    var beforeHash = FileHash(collisionFinalPath);
    ExpectActionFailure(
        () => ReportGenerator.FinalizeReport(collisionProject, collisionReport, collisionPreview.OutputPath),
        message => message.Contains("already exists", StringComparison.OrdinalIgnoreCase));
    Assert(FileHash(collisionFinalPath) == beforeHash, "existing final report remains unchanged after collision");
    Assert(File.Exists(collisionPreview.OutputPath), "preview still exists after failed finalization");

    Console.WriteLine("\n== Foreign report ownership blocks finalization ==");
    var foreignProject = ProjectStore.Create(
        Path.Combine(workspace, "foreign_project"), "Foreign", "14", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var foreignSaved = MakeReport(foreignProject, 1, 1, "Sunny", photoFiles);
    ReportStore.SaveReport(foreignProject, foreignSaved);
    var foreignAttempt = MakeReport(foreignProject, 1, 1, "Sunny", photoFiles);
    var foreignPreview = ReportGenerator.GenerateDraft(foreignProject, foreignAttempt);
    ExpectActionFailure(
        () => ReportGenerator.FinalizeReport(foreignProject, foreignAttempt, foreignPreview.OutputPath),
        message => message.Contains("already assigned", StringComparison.OrdinalIgnoreCase));
    Assert(File.Exists(foreignPreview.OutputPath), "preview preserved after foreign ownership failure");

    Console.WriteLine("\n== Final report cannot be finalized again ==");
    ExpectActionFailure(
        () => ReportGenerator.FinalizeReport(project, finalizeReport, finalReportPath),
        message => message.Contains("already final", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine("\n== Manual report number override advances correctly ==");
    var manualProject = ProjectStore.Create(
        Path.Combine(workspace, "manual_project"), "Manual", "42", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    manualProject.NextReportNumber = 2;
    ProjectStore.Save(manualProject);
    var manualReport5 = MakeReport(manualProject, 5, 1, "Sunny", photoFiles);
    var manualPreview5 = ReportGenerator.GenerateDraft(manualProject, manualReport5);
    ReportGenerator.FinalizeReport(manualProject, manualReport5, manualPreview5.OutputPath);
    var manualReloaded = ProjectStore.Load(manualProject.FolderPath)!;
    Assert(manualReloaded.NextReportNumber == 6, "Next = 2, finalize 5 -> Next = 6");
    Assert(ReportStore.ReportNumberExists(manualReloaded, 5), "occupied report number detected after finalization");
    var manualReport3 = MakeReport(manualReloaded, 3, 1, "Sunny", photoFiles);
    var manualPreview3 = ReportGenerator.GenerateDraft(manualReloaded, manualReport3);
    ReportGenerator.FinalizeReport(manualReloaded, manualReport3, manualPreview3.OutputPath);
    var manualReloadedAgain = ProjectStore.Load(manualProject.FolderPath)!;
    Assert(manualReloadedAgain.NextReportNumber == 6, "Next = 6, finalize 3 -> Next remains 6");

    Console.WriteLine("\n== Finalization rollback preserves preview and state ==");
    var rollbackProject = ProjectStore.Create(
        Path.Combine(workspace, "rollback_project"), "Rollback", "55", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var rollbackReport = MakeReport(rollbackProject, 1, 1, "Sunny", photoFiles);
    var rollbackPreview = ReportGenerator.GenerateDraft(rollbackProject, rollbackReport);
    var rollbackPreviewPath = rollbackPreview.OutputPath;
    ReportGenerator.SaveFailureHookForTesting = path =>
        path.EndsWith(ProjectLayout.ProjectFileName, StringComparison.OrdinalIgnoreCase)
            ? new IOException("Injected project save failure.")
            : null;
    try
    {
        ExpectActionFailure(
            () => ReportGenerator.FinalizeReport(rollbackProject, rollbackReport, rollbackPreviewPath),
            message => message.Contains("Injected project save failure.", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        ReportGenerator.SaveFailureHookForTesting = null;
    }

    Assert(File.Exists(rollbackPreviewPath), "preview still exists after rollback-triggering finalization failure");
    Assert(!File.Exists(ProjectLayout.FinalReportPath(rollbackProject, 1)), "final report not promoted after rollback failure");
    Assert(!Directory.EnumerateFiles(ProjectLayout.ReportFolder(rollbackProject, 1), "*.finalizing.docx", SearchOption.TopDirectoryOnly).Any(),
        "no finalizing artifacts remain after rollback");
    Assert(rollbackProject.NextReportNumber == 1, "in-memory next report restored after rollback");
    var rollbackLoadedReport = ReportStore.LoadReport(rollbackProject, 1);
    Assert(rollbackLoadedReport is null || rollbackLoadedReport.Status != ReportStatus.Final, "report.json not left finalized after rollback");

    Console.WriteLine("\n== Malformed report.json does not block valid reports ==");
    var malformedProject = ProjectStore.Create(
        Path.Combine(workspace, "malformed_project"), "Malformed", "19", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    ReportStore.SaveReport(malformedProject, MakeReport(malformedProject, 1, 0, "Sunny", photoFiles));
    Directory.CreateDirectory(ProjectLayout.ReportFolder(malformedProject, 2));
    File.WriteAllText(ProjectLayout.ReportFilePath(malformedProject, 2), "{ invalid json");
    ReportStore.SaveReport(malformedProject, MakeReport(malformedProject, 3, 0, "Sunny", photoFiles));
    var malformedResult = ReportStore.LoadAllReports(malformedProject);
    Assert(malformedResult.Reports.Count == 2, "valid reports still load when one report.json is malformed");
    Assert(malformedResult.Issues.Count == 1, "malformed report is surfaced as one load issue");
    Assert(malformedResult.Issues[0].Path.EndsWith(Path.Combine("0002", "report.json"), StringComparison.OrdinalIgnoreCase),
        "load issue identifies malformed report path");

    Console.WriteLine("\n== Project remains portable after directory move ==");
    var portableCreatedFolder = Path.Combine(workspace, "portable_created");
    var portableCreated = ProjectStore.Create(
        portableCreatedFolder, "Portable Created", "77", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var movedFolder = Path.Combine(workspace, "portable_moved", "Portable Created");
    CopyDirectory(portableCreatedFolder, movedFolder);
    var movedProject = ProjectStore.Load(movedFolder);
    Assert(movedProject is not null, "moved project loaded");
    Assert(movedProject!.TemplatePath.Equals(Path.Combine(movedFolder, "Template.docx"), StringComparison.OrdinalIgnoreCase),
        "moved project template resolves relative to new location");
    Assert(File.Exists(movedProject.ResolvedInspectorSignaturePath!), "moved project inspector signature resolves");
    Assert(File.Exists(movedProject.ResolvedProjectManagerSignaturePath!), "moved project pm signature resolves");

    Console.WriteLine("\n== Stored photo filename traversal is rejected ==");
    var photoSafetyProject = ProjectStore.Create(
        Path.Combine(workspace, "photo_safety"), "Photo Safety", "8", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var photoSafetyReport = MakeReport(photoSafetyProject, 1, 1, "Sunny", photoFiles);
    photoSafetyReport.Photos[0].StoredFileName = "../escape.jpg";
    ExpectActionFailure(
        () => ReportStore.SaveReport(photoSafetyProject, photoSafetyReport),
        message => message.Contains("file names only", StringComparison.OrdinalIgnoreCase));
    var readTraversalReport = MakeReport(photoSafetyProject, 2, 1, "Sunny", photoFiles);
    readTraversalReport.Photos[0].StoredFileName = @"..\outside.jpg";
    readTraversalReport.Photos[0].SourcePath = string.Empty;
    Assert(ReportStore.ResolvePhotoSourcePath(photoSafetyProject, readTraversalReport, readTraversalReport.Photos[0]) == string.Empty,
        "stored photo filename traversal is rejected on read");
    readTraversalReport.Photos[0].StoredFileName = @"C:\outside.jpg";
    Assert(ReportStore.ResolvePhotoSourcePath(photoSafetyProject, readTraversalReport, readTraversalReport.Photos[0]) == string.Empty,
        "absolute stored photo path is rejected on read");

    Console.WriteLine("\n== Failed project update leaves live and persisted state unchanged ==");
    var updateProject = ProjectStore.Create(
        Path.Combine(workspace, "update_project"), "Original", "21", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var beforeUpdateJson = File.ReadAllBytes(updateProject.FilePath);
    var beforeUpdateTemplateHash = FileHash(updateProject.TemplatePath);
    ExpectActionFailure(
        () => ProjectStore.Update(
            updateProject,
            "Changed",
            "22",
            "New Owner",
            "New CM",
            "New GC",
            templatePath,
            updateProject.InspectorSignaturePath,
            "Signatures/not-a-real-signature.txt"),
        message => message.Contains("signature image", StringComparison.OrdinalIgnoreCase) || message.Contains("supported", StringComparison.OrdinalIgnoreCase));
    Assert(updateProject.Name == "Original", "live project name unchanged after failed update");
    Assert(updateProject.Number == "21", "live project number unchanged after failed update");
    Assert(FileHash(updateProject.TemplatePath) == beforeUpdateTemplateHash, "existing template remains intact after failed update");
    Assert(File.ReadAllBytes(updateProject.FilePath).SequenceEqual(beforeUpdateJson), "project.json unchanged after failed update");

    Console.WriteLine("\n== Identical photo content stored once (dedupe) ==");
    var dedupeProject = ProjectStore.Create(
        Path.Combine(workspace, "dedupe_project"), "Dedupe", "7", "O", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var dedupeReport = MakeReport(dedupeProject, 1, 3, "Sunny", new[] { photoFiles[0], photoFiles[1], photoFiles[2] });
    ReportStore.SaveReport(dedupeProject, dedupeReport);
    var dedupeStored = Directory.GetFiles(ProjectLayout.ReportPhotosFolder(dedupeProject, 1)).Length;
    Assert(dedupeStored == 2, $"identical photos stored once (expected 2 distinct files, got {dedupeStored})");
    Assert(
        dedupeReport.Photos[0].StoredFileName == dedupeReport.Photos[1].StoredFileName,
        "identical photos share the same stored file");
    Assert(
        dedupeReport.Photos[1].StoredFileName != dedupeReport.Photos[2].StoredFileName,
        "distinct photos keep distinct stored files");
    Assert(dedupeReport.Photos[0].StoredFileName == "image1.png", "stored photo keeps original file name");
    Console.WriteLine("    ok: identical content deduped, distinct content preserved with original names");

    Console.WriteLine("\n== Repository hygiene rules present ==");
    var gitIgnore = File.ReadAllText(Path.Combine(root, ".gitignore"));
    Assert(gitIgnore.Contains("projects/*", StringComparison.Ordinal), "projects runtime data ignored");
    Assert(gitIgnore.Contains("Signatures/*", StringComparison.Ordinal), "signature library ignored");
    Assert(gitIgnore.Contains("generation-error.log", StringComparison.Ordinal), "generation logs ignored");
    Assert(gitIgnore.Contains("*.tmp.docx", StringComparison.Ordinal), "temporary docx files ignored");
    var trackedFiles = GitTrackedFiles(root);
    Assert(!trackedFiles.Any(p => p.StartsWith("projects/", StringComparison.OrdinalIgnoreCase) && p != "projects/.gitkeep"),
        "no runtime project content is tracked");
    Assert(!trackedFiles.Any(p => p.StartsWith("Signatures/", StringComparison.OrdinalIgnoreCase) && p != "Signatures/.gitkeep"),
        "no real signatures are tracked");
    Assert(!trackedFiles.Any(p => p.EndsWith("generation-error.log", StringComparison.OrdinalIgnoreCase)),
        "no generation logs are tracked");
    Assert(!trackedFiles.Any(p => p.EndsWith(".tmp.docx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".finalizing.docx", StringComparison.OrdinalIgnoreCase)),
        "no temp or finalizing docx files are tracked");

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
        Observations = "All welds visually inspected and found acceptable.\r\nNo cracks or deformation observed.\r\nAll connections torqued to spec.",
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

static void ExpectActionFailure(Action action, Func<string, bool> messageCheck)
{
    try
    {
        action();
        throw new InvalidOperationException("Expected operation to fail but it succeeded.");
    }
    catch (Exception ex) when (messageCheck(ex.Message))
    {
        Console.WriteLine("  ok: failure matched expectation: " + ex.Message);
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
    Assert(bodyText.Contains("2026-08-05"), "inspection date filled with template date format");
    Assert(bodyText.Contains("Acme Builders LLC"), "general contractor filled");
    Assert(bodyText.Contains("Anthony Wintergerst"), "inspector filled");
    Assert(bodyText.Contains("All welds visually inspected and found acceptable."), "observations filled");
    Assert(!bodyText.Contains("Repeat this photo page", StringComparison.OrdinalIgnoreCase), "template instructions removed from output");
    var observationsParagraph = body.Descendants<Paragraph>()
        .First(p => p.InnerText.Contains("All welds visually inspected", StringComparison.Ordinal));
    Assert(observationsParagraph.Descendants<Break>().Count() == 2, "carriage returns preserved as line breaks");

    var blips = body.Descendants<A.Blip>().ToList();
    Assert(blips.Count >= photoCount + 2, $"expected at least {photoCount + 2} blips (photos + signatures), found {blips.Count}");

    foreach (var blip in blips)
    {
        var part = mainPart.GetPartById(blip.Embed!);
        Assert(part is ImagePart, $"blip {blip.Embed} resolves to image part");
    }

    var sdtElements = body.Descendants<SdtElement>()
        .Where(b => b.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value is not null)
        .ToList();
    Assert(sdtElements.Count == 2, "both signature content controls preserved in output");
    Assert(sdtElements.All(b => b.Descendants<A.Blip>().Any(blip => mainPart.GetPartById(blip.Embed!) is ImagePart)),
        "each signature content control embeds an image");
    Assert(sdtElements.All(b => b.Descendants<A.Blip>().Count() == 1),
        "each signature content control contains exactly one signature drawing");

    AssertPhotoSizing(mainPart, body, photoCount);

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

static void AssertPhotoSizing(MainDocumentPart mainPart, Body body, int photoCount)
{
    const long PhotoHeightEmu = 3474720; // 3.8 inches (two photos + captions per page)
    var photos = body.Descendants<DW.Inline>()
        .Where(i => i.Extent is not null && i.Extent.Cy?.Value == PhotoHeightEmu)
        .ToList();
    Assert(photos.Count == photoCount, $"expected {photoCount} photo drawing(s) at 3.8-inch height, found {photos.Count}");

    foreach (var photo in photos)
    {
        var blip = photo.Descendants<A.Blip>().First();
        var part = (ImagePart)mainPart.GetPartById(blip.Embed!);
        var temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".img");
        try
        {
            using (var source = part.GetStream())
            using (var file = File.Create(temp))
            {
                source.CopyTo(file);
            }

            var (naturalWidth, naturalHeight) = ImageInfo.GetPixelSize(temp);
            var expectedCx = (long)(PhotoHeightEmu * (double)naturalWidth / naturalHeight);
            Assert(Math.Abs(expectedCx - photo.Extent!.Cx!.Value) <= 1,
                $"photo width {photo.Extent.Cx} EMU is aspect-locked to 3.8-inch height (expected ~{expectedCx})");
        }
        finally
        {
            File.Delete(temp);
        }
    }
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

static string ReadBodyText(string docxPath)
{
    using var doc = WordprocessingDocument.Open(docxPath, false);
    return string.Concat(doc.MainDocumentPart!.Document.Body!.Descendants<Text>().Select(t => t.Text));
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
    {
        var targetDirectory = Path.Combine(destination, Path.GetRelativePath(source, directory));
        Directory.CreateDirectory(targetDirectory);
    }

    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
        var targetFile = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.Copy(file, targetFile, overwrite: true);
    }
}

static string FileHash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

static IReadOnlyList<string> GitTrackedFiles(string workingDirectory)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "ls-files",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException("git ls-files failed: " + stderr);
    }

    return stdout
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .ToList();
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
