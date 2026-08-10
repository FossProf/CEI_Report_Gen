using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CEI.ReportGenerator.App;
using CEI.ReportGenerator.App.Services;
using CEI.ReportGenerator.App.ViewModels;
using CEI.ReportGenerator.Core;
using CEI.ReportGenerator.Core.Models;
using CEI.ReportGenerator.Core.Services;
using CEI.ReportImporter.Core.Models;
using CEI.ReportImporter.Core.Services;
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
    Console.WriteLine("\n== Application settings ==");
    var settingsPath = Path.Combine(workspace, "settings", "settings.json");
    var settingsStore = new ApplicationSettingsStore(settingsPath);
    var defaultSettings = settingsStore.Load();
    Assert(defaultSettings.DefaultProjectsFolder == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SPINgen", "Projects"),
        "no settings file returns default projects folder");
    Assert(defaultSettings.RecentProjectLimit == 10, "no settings file returns default recent project limit");
    Assert(defaultSettings.ReopenLastProjectOnStartup == false, "no settings file returns default startup reopen value");
    Assert(defaultSettings.LastOpenedProjectPath is null, "no settings file returns null last project path");

    var roundTripSettings = defaultSettings.Clone();
    roundTripSettings.DefaultProjectsFolder = Path.Combine(workspace, "configured_projects");
    roundTripSettings.RecentProjectLimit = 7;
    roundTripSettings.ReopenLastProjectOnStartup = true;
    roundTripSettings.LastOpenedProjectPath = @"C:\Projects\Demo";
    settingsStore.Save(roundTripSettings);
    var reloadedSettings = settingsStore.Load();
    Assert(reloadedSettings.DefaultProjectsFolder == roundTripSettings.DefaultProjectsFolder, "settings round trip preserves default folder");
    Assert(reloadedSettings.RecentProjectLimit == 7, "settings round trip preserves recent project limit");
    Assert(reloadedSettings.ReopenLastProjectOnStartup, "settings round trip preserves startup reopen");
    Assert(reloadedSettings.LastOpenedProjectPath == @"C:\Projects\Demo", "last opened project path persists");
    Assert(Directory.Exists(roundTripSettings.DefaultProjectsFolder), "valid default folder persists and exists");

    File.WriteAllText(settingsPath, "{ malformed json");
    var malformedSettings = settingsStore.Load();
    Assert(malformedSettings.DefaultProjectsFolder == ApplicationSettings.CreateDefaults().DefaultProjectsFolder, "malformed JSON returns default projects folder safely");
    Assert(malformedSettings.RecentProjectLimit == 10, "malformed JSON returns default recent project limit safely");

    var invalidLimitSettings = ApplicationSettings.CreateDefaults();
    invalidLimitSettings.RecentProjectLimit = 0;
    Assert(ApplicationSettingsValidator.Validate(invalidLimitSettings).Any(e => e.Contains("between 1 and 25", StringComparison.OrdinalIgnoreCase)),
        "recent project limit validates 1-25");

    var invalidFolderSettings = ApplicationSettings.CreateDefaults();
    invalidFolderSettings.DefaultProjectsFolder = string.Empty;
    Assert(ApplicationSettingsValidator.Validate(invalidFolderSettings).Any(e => e.Contains("required", StringComparison.OrdinalIgnoreCase)),
        "invalid default folder rejected");

    var resetSettings = settingsStore.ResetToDefaults();
    Assert(resetSettings.DefaultProjectsFolder == ApplicationSettings.CreateDefaults().DefaultProjectsFolder, "reset-to-default values restore default folder");
    Assert(resetSettings.RecentProjectLimit == 10, "reset-to-default values restore recent project limit");
    Assert(resetSettings.ReopenLastProjectOnStartup == false, "reset-to-default values restore startup reopen");
    Assert(resetSettings.LastOpenedProjectPath is null, "reset-to-default values restore null last project path");

    var missingStartupSettings = ApplicationSettings.CreateDefaults();
    missingStartupSettings.ReopenLastProjectOnStartup = true;
    missingStartupSettings.LastOpenedProjectPath = Path.Combine(workspace, "missing_project");
    settingsStore.Save(missingStartupSettings);
    var startupPath = ApplicationSettingsBehavior.GetStartupReopenProjectPath(missingStartupSettings, settingsStore);
    Assert(startupPath is null, "startup reopen ignores missing path safely");
    Assert(settingsStore.Load().LastOpenedProjectPath is null, "missing startup reopen path is cleared safely");

    var currentSettingsPath = Path.Combine(workspace, "settings_migration", "SPINgen", "settings.json");
    var legacySettingsPath = Path.Combine(workspace, "settings_migration", "CEI Report Generator", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(legacySettingsPath)!);
    File.WriteAllText(
        legacySettingsPath,
        """
        {
          "defaultProjectsFolder": "C:\\LegacyProjects",
          "recentProjectLimit": 9,
          "reopenLastProjectOnStartup": true,
          "lastOpenedProjectPath": "C:\\LegacyProjects\\Demo"
        }
        """);
    var migratedSettingsStore = new ApplicationSettingsStore(currentSettingsPath, legacySettingsPath);
    var migratedSettings = migratedSettingsStore.Load();
    Assert(migratedSettings.DefaultProjectsFolder == @"C:\LegacyProjects", "legacy settings file is loaded when the SPINgen settings file does not exist");
    Assert(migratedSettings.RecentProjectLimit == 9, "legacy settings keeps recent project limit");
    Assert(migratedSettings.ReopenLastProjectOnStartup, "legacy settings keeps startup reopen preference");
    Assert(migratedSettings.LastOpenedProjectPath == @"C:\LegacyProjects\Demo", "legacy settings keeps last opened project path");

    Console.WriteLine("\n== Temperature assistance settings and session ==");
    Assert(defaultSettings.TemperatureAssistance.TemperatureLookupEnabled, "temperature lookup defaults enabled");
    Assert(defaultSettings.TemperatureAssistance.TemperatureAutoEnabledForNewReports, "auto temperature defaults enabled for new reports");
    Assert(defaultSettings.TemperatureAssistance.HistoricalDayStartHour == 7, "temperature assistance defaults to 7 AM start");
    Assert(defaultSettings.TemperatureAssistance.HistoricalDayEndHour == 17, "temperature assistance defaults to 5 PM end");

    var roundTripTemperatureSettings = defaultSettings.Clone();
    roundTripTemperatureSettings.TemperatureAssistance.TemperatureLookupEnabled = false;
    roundTripTemperatureSettings.TemperatureAssistance.TemperatureAutoEnabledForNewReports = false;
    roundTripTemperatureSettings.TemperatureAssistance.HistoricalDayStartHour = 8;
    roundTripTemperatureSettings.TemperatureAssistance.HistoricalDayEndHour = 18;
    settingsStore.Save(roundTripTemperatureSettings);
    var reloadedTemperatureSettings = settingsStore.Load();
    Assert(!reloadedTemperatureSettings.TemperatureAssistance.TemperatureLookupEnabled, "temperature lookup setting round trips");
    Assert(!reloadedTemperatureSettings.TemperatureAssistance.TemperatureAutoEnabledForNewReports, "temperature auto default setting round trips");
    Assert(reloadedTemperatureSettings.TemperatureAssistance.HistoricalDayStartHour == 8, "temperature start hour round trips");
    Assert(reloadedTemperatureSettings.TemperatureAssistance.HistoricalDayEndHour == 18, "temperature end hour round trips");

    var invalidTemperatureSettings = ApplicationSettings.CreateDefaults();
    invalidTemperatureSettings.TemperatureAssistance.HistoricalDayStartHour = 17;
    invalidTemperatureSettings.TemperatureAssistance.HistoricalDayEndHour = 17;
    Assert(ApplicationSettingsValidator.Validate(invalidTemperatureSettings).Any(e => e.Contains("earlier than end hour", StringComparison.OrdinalIgnoreCase)),
        "temperature assistance start/end hours validate");

    var averageResult = HistoricalTemperatureAverager.AverageFahrenheit([70, 72, 74, 76, 78]);
    Assert(averageResult.RoundedTemperatureFahrenheit == 74, "historical average helper returns arithmetic mean with whole-degree rounding");

    var temperatureProject = new Project
    {
        Name = "Temperature Project",
        Number = "701",
        Owner = "Owner",
        ContractManager = "CM",
        GeneralContractor = "GC",
        FolderPath = Path.Combine(workspace, "temperature_project"),
        LocationText = "Louisville, KY 40299",
        LocationLatitude = 38.2115,
        LocationLongitude = -85.5565,
        LocationTimeZoneId = "Eastern Standard Time"
    };
    var disabledService = new FakeProjectTemperatureService
    {
        CurrentResultFactory = _ => Task.FromResult(TemperatureLookupResult.Success(84.4))
    };
    var disabledSession = new TemperatureAssistanceSession(
        temperatureProject,
        string.Empty,
        new DateTime(2026, 8, 10),
        isNewReport: true,
        isFinalReport: false,
        new TemperatureAssistanceSettings
        {
            TemperatureLookupEnabled = false,
            TemperatureAutoEnabledForNewReports = true,
            HistoricalDayStartHour = 7,
            HistoricalDayEndHour = 17
        },
        disabledService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    await disabledSession.InitializeAsync();
    Assert(!disabledSession.AutoEnabled, "master temperature feature disabled keeps auto off");
    Assert(disabledService.CurrentCallCount == 0, "master temperature feature disabled performs no lookup");

    var locationWorkflowResolver = new FakeProjectLocationResolver
    {
        ResolveFunc = text => Task.FromResult<ProjectCoordinates?>(new ProjectCoordinates(38.1, -85.5, "Eastern Standard Time"))
    };
    var locationWorkflow = new ProjectLocationResolutionWorkflow(locationWorkflowResolver);
    var cachedOutcome = await locationWorkflow.ResolveAsync(temperatureProject, temperatureProject.LocationText, CancellationToken.None);
    Assert(cachedOutcome.IsResolved && cachedOutcome.UsedCachedCoordinates, "saved project coordinates skip geocoding");
    Assert(locationWorkflowResolver.CallCount == 0, "cached project coordinates avoid resolver calls");
    var resolvedOutcome = await locationWorkflow.ResolveAsync(temperatureProject, "40208", CancellationToken.None);
    Assert(resolvedOutcome.IsResolved && resolvedOutcome.Coordinates is not null, "new location text resolves into coordinates");
    Assert(locationWorkflowResolver.CallCount == 1, "new location text calls the resolver");
    locationWorkflowResolver.ResolveFunc = _ => Task.FromResult<ProjectCoordinates?>(null);
    var unresolvedOutcome = await locationWorkflow.ResolveAsync(temperatureProject, "Unknown Place", CancellationToken.None);
    Assert(!unresolvedOutcome.IsResolved, "unresolved project location remains gracefully unavailable");

    var currentTemperatureService = new FakeProjectTemperatureService
    {
        CurrentResultFactory = _ => Task.FromResult(TemperatureLookupResult.Success(84.4))
    };
    var currentTemperatureSession = new TemperatureAssistanceSession(
        temperatureProject,
        string.Empty,
        new DateTime(2026, 8, 10),
        isNewReport: true,
        isFinalReport: false,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        currentTemperatureService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    await currentTemperatureSession.InitializeAsync();
    Assert(currentTemperatureSession.AutoEnabled, "new report auto temperature follows the default setting");
    Assert(currentTemperatureSession.TemperatureText == "84", "current temperature lookup rounds to a whole Fahrenheit degree");
    Assert(currentTemperatureService.CurrentCallCount == 1, "new report current-day lookup uses the current-temperature provider");

    var finalTemperatureSession = new TemperatureAssistanceSession(
        temperatureProject,
        "86",
        new DateTime(2026, 8, 10),
        isNewReport: false,
        isFinalReport: true,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        currentTemperatureService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    await finalTemperatureSession.InitializeAsync();
    Assert(!finalTemperatureSession.AutoEnabled, "final reports open with auto temperature disabled");
    Assert(finalTemperatureSession.TemperatureText == "86", "final reports keep the existing stored temperature until the user enables auto");

    var historicalTemperatureService = new FakeProjectTemperatureService
    {
        HistoricalResultFactory = (_, _, startHour, endHour) =>
            Task.FromResult(startHour == 7 && endHour == 17
                ? HistoricalTemperatureAverager.AverageFahrenheit([74, 76, 79, 82, 84, 86, 87, 86, 84, 82, 80])
                : TemperatureLookupResult.Failure("bad hours"))
    };
    var historicalTemperatureSession = new TemperatureAssistanceSession(
        temperatureProject,
        string.Empty,
        new DateTime(2026, 8, 9),
        isNewReport: true,
        isFinalReport: false,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        historicalTemperatureService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    await historicalTemperatureSession.InitializeAsync();
    Assert(historicalTemperatureSession.TemperatureText == "82", "past-date lookup uses the configured daytime average");
    Assert(historicalTemperatureService.HistoricalCallCount == 1, "past-date lookup uses the historical temperature provider");

    var failureTemperatureService = new FakeProjectTemperatureService
    {
        CurrentResultFactory = _ => Task.FromResult(TemperatureLookupResult.Failure("Temperature lookup unavailable. Enter temperature manually."))
    };
    var failureTemperatureSession = new TemperatureAssistanceSession(
        temperatureProject,
        "90",
        new DateTime(2026, 8, 10),
        isNewReport: true,
        isFinalReport: false,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        failureTemperatureService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    await failureTemperatureSession.InitializeAsync();
    Assert(failureTemperatureSession.TemperatureText == "90", "failed lookup leaves the existing temperature unchanged");
    Assert(failureTemperatureSession.HasStatusMessage, "failed lookup surfaces a non-blocking manual-entry message");

    var futureTemperatureService = new FakeProjectTemperatureService
    {
        CurrentResultFactory = _ => Task.FromResult(TemperatureLookupResult.Success(75))
    };
    var futureTemperatureSession = new TemperatureAssistanceSession(
        temperatureProject,
        "91",
        new DateTime(2026, 8, 11),
        isNewReport: true,
        isFinalReport: false,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        futureTemperatureService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    await futureTemperatureSession.InitializeAsync();
    Assert(futureTemperatureSession.TemperatureText == "91", "future-dated reports do not auto-populate temperature");
    Assert(futureTemperatureSession.HasStatusMessage, "future-dated reports explain that auto temperature is only for today and past dates");

    var manualOverrideTaskSource = new TaskCompletionSource<TemperatureLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var manualOverrideService = new FakeProjectTemperatureService
    {
        CurrentResultFactory = _ => manualOverrideTaskSource.Task
    };
    var manualOverrideSession = new TemperatureAssistanceSession(
        temperatureProject,
        string.Empty,
        new DateTime(2026, 8, 10),
        isNewReport: true,
        isFinalReport: false,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        manualOverrideService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    var manualInitializeTask = manualOverrideSession.InitializeAsync();
    manualOverrideSession.ApplyManualTemperatureOverride("86");
    manualOverrideTaskSource.SetResult(TemperatureLookupResult.Success(84));
    await manualInitializeTask;
    Assert(manualOverrideSession.TemperatureText == "86", "manual temperature override wins over a delayed lookup");
    Assert(!manualOverrideSession.AutoEnabled, "manual temperature override disables auto for the editor session");

    var raceDateA = new DateTime(2026, 8, 8);
    var raceDateB = new DateTime(2026, 8, 9);
    var raceTemperatureService = new FakeProjectTemperatureService();
    raceTemperatureService.SetHistoricalPendingResult(raceDateA, new TaskCompletionSource<TemperatureLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously));
    raceTemperatureService.SetHistoricalPendingResult(raceDateB, new TaskCompletionSource<TemperatureLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously));
    var raceSession = new TemperatureAssistanceSession(
        temperatureProject,
        string.Empty,
        raceDateA,
        isNewReport: false,
        isFinalReport: false,
        ApplicationSettings.CreateDefaults().TemperatureAssistance,
        raceTemperatureService,
        () => new DateTimeOffset(2026, 8, 10, 16, 0, 0, TimeSpan.Zero));
    var dateATask = raceSession.SetAutoEnabledAsync(true, raceDateA);
    var dateBTask = raceSession.UpdateDateAsync(raceDateB);
    raceTemperatureService.ResolveHistorical(raceDateB, TemperatureLookupResult.Success(83));
    raceTemperatureService.ResolveHistorical(raceDateA, TemperatureLookupResult.Success(77));
    await Task.WhenAll(dateATask, dateBTask);
    Assert(raceSession.TemperatureText == "83", "only the latest date-change lookup result is applied");

    Console.WriteLine("\n== Project readiness and dashboard ==");
    var readinessTemplatePath = Path.Combine(root, "templates", "CEI_Base_Template_Refined.docx");
    var readinessPhotoDir = Path.Combine(workspace, "readiness_photos");
    Directory.CreateDirectory(readinessPhotoDir);
    ExtractTemplateMedia(readinessTemplatePath, readinessPhotoDir);
    var readinessPhotos = Directory.GetFiles(readinessPhotoDir).OrderBy(f => f).ToArray();
    var readinessProjectFolder = Path.Combine(workspace, "readiness_project");
    var readinessProject = ProjectStore.Create(
        readinessProjectFolder,
        "Dashboard Project",
        "42",
        "Owner",
        "Manager",
        "Builder",
        readinessTemplatePath,
        readinessPhotos[0],
        readinessPhotos[1]);

    var readyState = ProjectReadinessEvaluator.Evaluate(readinessProject);
    Assert(readyState.IsReady, "fully valid project => readiness true");
    Assert(readyState.Issues.Count == 0, "fully valid project => no readiness issues");

    var missingTemplateProject = ProjectStore.Load(readinessProjectFolder)!;
    missingTemplateProject.TemplatePath = Path.Combine(workspace, "missing-template.docx");
    var missingTemplateReadiness = ProjectReadinessEvaluator.Evaluate(missingTemplateProject);
    Assert(!missingTemplateReadiness.TemplateReady, "missing template => template readiness false");
    Assert(missingTemplateReadiness.Issues.Any(i => i.Contains("template", StringComparison.OrdinalIgnoreCase)), "missing template => useful readiness issue");

    var invalidTemplatePath = Path.Combine(workspace, "invalid_template.docx");
    File.Copy(readinessTemplatePath, invalidTemplatePath);
    using (var broken = WordprocessingDocument.Open(invalidTemplatePath, true))
    {
        var firstAlias = broken.MainDocumentPart!.Document.Body!
            .Descendants<SdtElement>()
            .First();
        firstAlias.Remove();
        broken.MainDocumentPart.Document.Save();
    }

    var invalidTemplateProject = ProjectStore.Load(readinessProjectFolder)!;
    invalidTemplateProject.TemplatePath = invalidTemplatePath;
    var invalidTemplateReadiness = ProjectReadinessEvaluator.Evaluate(invalidTemplateProject);
    Assert(!invalidTemplateReadiness.TemplateReady, "invalid template => template readiness false");

    var missingInspectorProject = ProjectStore.Load(readinessProjectFolder)!;
    missingInspectorProject.InspectorSignaturePath = string.Empty;
    var missingInspectorReadiness = ProjectReadinessEvaluator.Evaluate(missingInspectorProject);
    Assert(!missingInspectorReadiness.InspectorSignatureReady, "missing inspector signature => readiness false");

    var missingPmProject = ProjectStore.Load(readinessProjectFolder)!;
    missingPmProject.ProjectManagerSignaturePath = string.Empty;
    var missingPmReadiness = ProjectReadinessEvaluator.Evaluate(missingPmProject);
    Assert(!missingPmReadiness.ProjectManagerSignatureReady, "missing PM signature => readiness false");

    var invalidConfigProject = ProjectStore.Load(readinessProjectFolder)!;
    invalidConfigProject.Name = string.Empty;
    var invalidConfigReadiness = ProjectReadinessEvaluator.Evaluate(invalidConfigProject);
    Assert(!invalidConfigReadiness.ProjectConfigurationReady, "invalid project config => readiness false");
    Assert(invalidConfigReadiness.Issues.Any(i => i.Contains("required", StringComparison.OrdinalIgnoreCase)), "readiness issues contain useful descriptions");

    var emptyLoadResult = ReportStore.LoadAllReports(readinessProject);
    var emptySummary = ProjectDashboardSummaryBuilder.Build(readinessProject, emptyLoadResult);
    Assert(emptySummary.TotalReports == 0, "report counts: zero reports");
    Assert(emptySummary.FinalReports == 0, "report counts: zero final");
    Assert(emptySummary.DraftReports == 0, "report counts: zero draft");
    Assert(emptySummary.NextReportNumber == 1, "next report display uses authoritative next number for empty project");

    var draftOnlyProject = ProjectStore.Create(
        Path.Combine(workspace, "dashboard_draft_only"),
        "Draft Only",
        "51",
        "Owner",
        "CM",
        "GC",
        readinessTemplatePath,
        readinessPhotos[0],
        readinessPhotos[1]);
    var draftOnlyReport = MakeReport(draftOnlyProject, 1, 1, "Sunny", readinessPhotos);
    ReportStore.SaveReport(draftOnlyProject, draftOnlyReport);
    var draftSummary = ProjectDashboardSummaryBuilder.Build(draftOnlyProject, ReportStore.LoadAllReports(draftOnlyProject));
    Assert(draftSummary.TotalReports == 1 && draftSummary.DraftReports == 1 && draftSummary.FinalReports == 0, "report counts: draft-only");

    var finalOnlyProject = ProjectStore.Create(
        Path.Combine(workspace, "dashboard_final_only"),
        "Final Only",
        "52",
        "Owner",
        "CM",
        "GC",
        readinessTemplatePath,
        readinessPhotos[0],
        readinessPhotos[1]);
    var finalOnlyReport = MakeReport(finalOnlyProject, 1, 0, "Sunny", readinessPhotos);
    var finalOnlyPreview = ReportGenerator.GenerateDraft(finalOnlyProject, finalOnlyReport);
    ReportGenerator.FinalizeReport(finalOnlyProject, finalOnlyReport, finalOnlyPreview.OutputPath);
    var finalSummary = ProjectDashboardSummaryBuilder.Build(finalOnlyProject, ReportStore.LoadAllReports(finalOnlyProject));
    Assert(finalSummary.TotalReports == 1 && finalSummary.FinalReports == 1 && finalSummary.DraftReports == 0, "report counts: final-only");

    var mixedProject = ProjectStore.Create(
        Path.Combine(workspace, "dashboard_mixed"),
        "Mixed",
        "53",
        "Owner",
        "CM",
        "GC",
        readinessTemplatePath,
        readinessPhotos[0],
        readinessPhotos[1]);
    var mixedDraft = MakeReport(mixedProject, 1, 0, "Sunny", readinessPhotos);
    ReportStore.SaveReport(mixedProject, mixedDraft);
    var mixedFinal = MakeReport(mixedProject, 2, 0, "Sunny", readinessPhotos);
    var mixedPreview = ReportGenerator.GenerateDraft(mixedProject, mixedFinal);
    ReportGenerator.FinalizeReport(mixedProject, mixedFinal, mixedPreview.OutputPath);
    var mixedSummary = ProjectDashboardSummaryBuilder.Build(mixedProject, ReportStore.LoadAllReports(mixedProject));
    Assert(mixedSummary.TotalReports == 2 && mixedSummary.FinalReports == 1 && mixedSummary.DraftReports == 1, "report counts: mixed");

    mixedProject.NextReportNumber = 1;
    var authoritativeSummary = ProjectDashboardSummaryBuilder.Build(mixedProject, ReportStore.LoadAllReports(mixedProject));
    Assert(authoritativeSummary.NextReportNumber == ReportStore.GetNextReportNumber(mixedProject), "next report display uses authoritative next-number logic");

    Console.WriteLine("\n== Report draft factory ==");
    var draftFactoryProject = ProjectStore.Create(
        Path.Combine(workspace, "draft_factory"),
        "Draft Factory",
        "54",
        "Owner",
        "CM",
        "GC",
        readinessTemplatePath,
        readinessPhotos[0],
        readinessPhotos[1]);
    for (var i = 1; i <= 16; i++)
    {
        ReportStore.SaveReport(draftFactoryProject, MakeReport(draftFactoryProject, i, 1, "Sunny", readinessPhotos));
    }

    draftFactoryProject.NextReportNumber = 4;
    ProjectStore.Save(draftFactoryProject);

    var blankDraft = ReportDraftFactory.CreateBlank(draftFactoryProject);
    Assert(blankDraft.Number == 17, "CreateBlank uses authoritative next report number");
    Assert(blankDraft.Date == DateTime.Today, "CreateBlank uses today's date");
    Assert(blankDraft.Status == ReportStatus.Draft, "CreateBlank starts as draft");
    Assert(blankDraft.Photos.Count == 0, "CreateBlank starts with empty photos");
    Assert(string.IsNullOrWhiteSpace(blankDraft.OutputFileName), "CreateBlank clears output file name");

    var finalSource = MakeReport(draftFactoryProject, 4, 2, "Cloudy", readinessPhotos);
    finalSource.Status = ReportStatus.Final;
    finalSource.OutputFileName = "2026-08-01 Draft Factory SPIN Report #4.docx";
    finalSource.CreatedUtc = new DateTime(2026, 8, 1, 12, 30, 0, DateTimeKind.Utc);

    var duplicatedFromFinal = ReportDraftFactory.CreateFromExisting(draftFactoryProject, finalSource);
    Assert(duplicatedFromFinal.Number == 17, "CreateFromExisting uses authoritative next report number instead of source number");
    Assert(duplicatedFromFinal.Date == DateTime.Today, "CreateFromExisting uses today's date");
    Assert(duplicatedFromFinal.Status == ReportStatus.Draft, "CreateFromExisting starts as draft");
    Assert(duplicatedFromFinal.Locations == finalSource.Locations, "CreateFromExisting copies location");
    Assert(duplicatedFromFinal.Inspectors == finalSource.Inspectors, "CreateFromExisting copies inspectors");
    Assert(duplicatedFromFinal.PersonnelOnSite == finalSource.PersonnelOnSite, "CreateFromExisting copies personnel");
    Assert(duplicatedFromFinal.DescriptionOfWork == finalSource.DescriptionOfWork, "CreateFromExisting copies description of work");
    Assert(duplicatedFromFinal.DrawingsReviewed == finalSource.DrawingsReviewed, "CreateFromExisting copies drawings reviewed");
    Assert(duplicatedFromFinal.NewDiscrepancies == finalSource.NewDiscrepancies, "CreateFromExisting copies new discrepancies exactly");
    Assert(duplicatedFromFinal.PreviousDiscrepancies == finalSource.PreviousDiscrepancies, "CreateFromExisting copies previous discrepancies exactly");
    Assert(string.IsNullOrWhiteSpace(duplicatedFromFinal.Weather), "CreateFromExisting clears weather");
    Assert(string.IsNullOrWhiteSpace(duplicatedFromFinal.Temperature), "CreateFromExisting clears temperature");
    Assert(string.IsNullOrWhiteSpace(duplicatedFromFinal.Observations), "CreateFromExisting clears observations");
    Assert(duplicatedFromFinal.Photos.Count == 0, "CreateFromExisting starts with empty photos");
    Assert(string.IsNullOrWhiteSpace(duplicatedFromFinal.OutputFileName), "CreateFromExisting clears output file name");
    Assert(duplicatedFromFinal.CreatedUtc != finalSource.CreatedUtc, "CreateFromExisting assigns a new CreatedUtc");

    duplicatedFromFinal.Locations = "Changed location";
    duplicatedFromFinal.Inspectors = "Changed inspector";
    duplicatedFromFinal.Photos.Add(new Photo { SourcePath = "new-photo.jpg", Caption = "new photo" });
    Assert(finalSource.Locations != duplicatedFromFinal.Locations, "editing duplicated report does not change source location");
    Assert(finalSource.Inspectors != duplicatedFromFinal.Inspectors, "editing duplicated report does not change source inspectors");
    Assert(finalSource.Photos.Count == 2, "editing duplicated report does not change source photos");
    Assert(finalSource.OutputFileName == "2026-08-01 Draft Factory SPIN Report #4.docx", "editing duplicated report does not change source output file");
    Assert(finalSource.Status == ReportStatus.Final, "editing duplicated report does not change final source status");

    var draftSource = MakeReport(draftFactoryProject, 7, 1, "Rain", readinessPhotos);
    draftSource.Status = ReportStatus.Draft;
    var duplicatedFromDraft = ReportDraftFactory.CreateFromExisting(draftFactoryProject, draftSource);
    Assert(duplicatedFromDraft.Number == 17, "draft source also duplicates to authoritative next report number");
    Assert(duplicatedFromDraft.Status == ReportStatus.Draft, "draft source duplication still starts as draft");

    var carryForwardNumberProject = ProjectStore.Create(
        Path.Combine(workspace, "draft_factory_numbering"),
        "Draft Factory Numbering",
        "55",
        "Owner",
        "CM",
        "GC",
        readinessTemplatePath,
        readinessPhotos[0],
        readinessPhotos[1]);
    ReportStore.SaveReport(carryForwardNumberProject, MakeReport(carryForwardNumberProject, 41, 0, "Sunny", readinessPhotos));
    ReportStore.SaveReport(carryForwardNumberProject, MakeReport(carryForwardNumberProject, 42, 0, "Sunny", readinessPhotos));
    var carryForwardSource = MakeReport(carryForwardNumberProject, 40, 0, "Sunny", readinessPhotos);
    carryForwardSource.NewDiscrepancies = "Carry this discrepancy forward.";
    carryForwardSource.PreviousDiscrepancies = "Carry this prior discrepancy forward.";
    var carriedForwardDraft = ReportDraftFactory.CreateFromExisting(carryForwardNumberProject, carryForwardSource);
    Assert(carriedForwardDraft.Number == 43, "CreateFromExisting suggests the first available report number at or above source + 1");

    Console.WriteLine("\n== Report search service ==");
    var searchReports = new List<InspectionReport>
    {
        new()
        {
            Number = 216,
            Status = ReportStatus.Final,
            Date = new DateTime(2026, 7, 15),
            Temperature = "92F",
            Weather = "Cloudy",
            Locations = "Gridline 2",
            Inspectors = "Anthony Pace",
            PersonnelOnSite = "Badge 50CFAE40 present",
            DescriptionOfWork = "Bond beam reinforcing was inspected",
            DrawingsReviewed = "S-201 lintel detail",
            Observations = "CMU lintel installation reviewed prior to placement.",
            NewDiscrepancies = "Anchor bolts need recheck.",
            PreviousDiscrepancies = "Weldback repair completed.",
            OutputFileName = "2026-07-15 Sample Project SPIN Report #216.docx",
            Photos =
            [
                new Photo { Caption = "West elevation splice plate" }
            ]
        },
        new()
        {
            Number = 40,
            Status = ReportStatus.Draft,
            Date = new DateTime(2026, 6, 1),
            Temperature = "75F",
            Weather = "Sunny",
            Locations = "South stair tower",
            Inspectors = "Jane Smith",
            PersonnelOnSite = "Concrete crew",
            DescriptionOfWork = "Slab edge formwork review",
            DrawingsReviewed = "A-100",
            Observations = "Pre-pour walk completed.",
            NewDiscrepancies = "None.",
            PreviousDiscrepancies = "N/A",
            Photos =
            [
                new Photo { Caption = "South stair pan" }
            ]
        },
        new()
        {
            Number = 9,
            Status = ReportStatus.Final,
            Date = new DateTime(2026, 7, 31),
            Temperature = "81F",
            Weather = "Cloudy",
            Locations = "Roof curb",
            Inspectors = "Jordan Miles",
            PersonnelOnSite = "Steel crew",
            DescriptionOfWork = "Final roof inspection",
            DrawingsReviewed = "R-201",
            Observations = "Final sealant touch-up review.",
            NewDiscrepancies = "None.",
            PreviousDiscrepancies = "Prior curb issue closed."
        }
    };
    var searchSourceOrderBefore = searchReports.Select(report => report.Number).ToArray();

    Assert(ReportSearchService.Filter(searchReports, new ReportSearchCriteria()).Count == 3, "blank criteria returns all reports");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "Gridline" }).SequenceEqual([216]), "keyword search matches Locations");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "2026-07-15" }).SequenceEqual([216]), "keyword search matches report date text");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "pace" }).SequenceEqual([216]), "keyword search matches Inspectors case-insensitively");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "50CFAE40" }).SequenceEqual([216]), "keyword search matches PersonnelOnSite");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "bond beam" }).SequenceEqual([216]), "keyword search matches DescriptionOfWork by substring");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "S-201" }).SequenceEqual([216]), "keyword search matches DrawingsReviewed");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "placement" }).SequenceEqual([216]), "keyword search matches Observations");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "Anchor" }).SequenceEqual([216]), "keyword search matches NewDiscrepancies");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "Weldback" }).SequenceEqual([216]), "keyword search matches PreviousDiscrepancies");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "Cloudy" }).SequenceEqual([216, 9]), "keyword search matches Weather");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "92F" }).SequenceEqual([216]), "keyword search matches Temperature");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "splice plate" }).SequenceEqual([216]), "keyword search matches photo captions");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "Sample Project SPIN Report" }).SequenceEqual([216]), "keyword search matches output file name");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "216" }).SequenceEqual([216]), "keyword search matches report number");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "#216" }).SequenceEqual([216]), "keyword search matches hash-prefixed report number");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "0216" }).SequenceEqual([216]), "keyword search matches padded report number");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "lintel" }).SequenceEqual([216]), "substring matching returns partial-term matches");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "gridline reinforcing" }).SequenceEqual([216]), "multiple search terms use AND behavior across fields");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "gridline masonry" }).Count == 0, "multiple search terms require every term to match");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { Status = ReportStatus.Draft }).SequenceEqual([40]), "status filter matches Draft reports");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { Status = ReportStatus.Final }).SequenceEqual([216, 9]), "status filter matches Final reports");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { FromDate = new DateTime(2026, 7, 15) }).SequenceEqual([216, 9]), "from date filter is inclusive");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { ToDate = new DateTime(2026, 7, 15) }).SequenceEqual([216, 40]), "to date filter is inclusive");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { FromDate = new DateTime(2026, 7, 1), ToDate = new DateTime(2026, 7, 31) }).SequenceEqual([216, 9]), "date range filter is inclusive on both ends");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { Weather = "Cloudy" }).SequenceEqual([216, 9]), "weather filter matches exact approved value");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria
    {
        SearchText = "bond",
        Status = ReportStatus.Final,
        Weather = "Cloudy",
        FromDate = new DateTime(2026, 7, 1),
        ToDate = new DateTime(2026, 7, 31)
    }).SequenceEqual([216]), "combined keyword, status, date, and weather filters use AND semantics");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { SearchText = "not-present" }).Count == 0, "no matches returns an empty result");
    Assert(searchReports.Select(report => report.Number).SequenceEqual(searchSourceOrderBefore), "source report list is not mutated by filtering");
    Assert(searchReports[0].Photos[0].Caption == "West elevation splice plate", "source reports are not mutated by filtering");
    Assert(SearchReportNumbers(searchReports, new ReportSearchCriteria { Weather = "Cloudy" }).SequenceEqual([216, 9]), "result ordering preserves input order policy");
    Assert(!ReportSearchService.TryValidateCriteria(new ReportSearchCriteria
    {
        FromDate = new DateTime(2026, 8, 1),
        ToDate = new DateTime(2026, 7, 1)
    }, out _), "invalid date criteria are rejected without throwing");

    Console.WriteLine("\n== Report match snippet builder ==");
    var snippetReport = new InspectionReport
    {
        Number = 216,
        Date = new DateTime(2026, 7, 15),
        Observations = "Cornerstone reviewed the CMU lintel reinforcement before concrete placement at Gridline 2.\r\nAll work was acceptable.",
        NewDiscrepancies = "Lintel bearing at the east opening should be rechecked.",
        PreviousDiscrepancies = "Previous lintel deficiency has been corrected.",
        DescriptionOfWork = "Concrete placement for intermediate columns and lintel reinforcing review.",
        Locations = "Gridline 2 east elevation.",
        DrawingsReviewed = "S-201 lintel detail.",
        PersonnelOnSite = "Pace and crew on site.",
        Inspectors = "Anthony Pace CWI",
        Weather = "Cloudy",
        Temperature = "92F",
        OutputFileName = "2026-07-15 Sample Project SPIN Report #216.docx",
        Photos =
        [
            new Photo { Caption = "Lintel reinforcing photo marker 17" }
        ]
    };

    var observationPriorityResult = ReportMatchSnippetBuilder.Build(snippetReport, "lintel");
    Assert(observationPriorityResult is not null, "snippet builder returns a result for matching keyword");
    Assert(observationPriorityResult!.MatchField == "Observations", "Observations are preferred over Description of Work when both match");

    var discrepancyPriorityResult = ReportMatchSnippetBuilder.Build(snippetReport, "bearing");
    Assert(discrepancyPriorityResult is not null && discrepancyPriorityResult.MatchField == "New Discrepancies", "new discrepancies outrank previous discrepancies");

    var locationResult = ReportMatchSnippetBuilder.Build(snippetReport, "east elevation");
    Assert(locationResult is not null && locationResult.MatchField == "Location", "location snippet uses the user-facing Location field name");

    var photoCaptionResult = ReportMatchSnippetBuilder.Build(snippetReport, "marker 17");
    Assert(photoCaptionResult is not null && photoCaptionResult.MatchField == "Photo Caption", "photo caption can provide search-result context");

    var reportNumberResult = ReportMatchSnippetBuilder.Build(snippetReport, "0216");
    Assert(reportNumberResult is not null && reportNumberResult.MatchField == "Report Number", "report-number match returns report-number context");

    var multilineResult = ReportMatchSnippetBuilder.Build(snippetReport, "acceptable");
    Assert(multilineResult is not null && !multilineResult.MatchFullText.Contains('\n'), "multiline match text is normalized into spaces");

    var beginningResult = ReportMatchSnippetBuilder.Build(
        new InspectionReport { Observations = "Lintel repair was reviewed immediately.", Number = 1, Date = new DateTime(2026, 7, 1) },
        "lintel");
    Assert(beginningResult is not null && !beginningResult.MatchSnippet.StartsWith("...", StringComparison.Ordinal), "snippet near the beginning does not force a leading ellipsis");

    var endResult = ReportMatchSnippetBuilder.Build(
        new InspectionReport { Observations = "The final noted condition was reinforcing at the lintel", Number = 2, Date = new DateTime(2026, 7, 1) },
        "lintel");
    Assert(endResult is not null && !endResult.MatchSnippet.EndsWith("...", StringComparison.Ordinal), "snippet near the end does not force a trailing ellipsis");

    var longFieldResult = ReportMatchSnippetBuilder.Build(
        new InspectionReport
        {
            Observations = "This is a very long observation paragraph that keeps going so the snippet builder has to trim surrounding context while still preserving the lintel match near the middle of the sentence for quick review by the inspector.",
            Number = 3,
            Date = new DateTime(2026, 7, 1)
        },
        "lintel");
    Assert(longFieldResult is not null && longFieldResult.MatchSnippet.Length < longFieldResult.MatchFullText.Length, "long match text is truncated into a concise snippet");

    var shortFieldResult = ReportMatchSnippetBuilder.Build(
        new InspectionReport { Observations = "Lintel reviewed.", Number = 4, Date = new DateTime(2026, 7, 1) },
        "lintel");
    Assert(shortFieldResult is not null && shortFieldResult.MatchSnippet == "Lintel reviewed.", "short match text remains intact");

    var caseInsensitiveResult = ReportMatchSnippetBuilder.Build(snippetReport, "cwi");
    Assert(caseInsensitiveResult is not null && caseInsensitiveResult.MatchField == "Inspector", "snippet matching is case-insensitive");

    var multiWordPriorityResult = ReportMatchSnippetBuilder.Build(snippetReport, "Gridline reinforcement placement");
    Assert(multiWordPriorityResult is not null && multiWordPriorityResult.MatchField == "Observations", "field containing the most query terms is preferred for multi-word context");

    Assert(snippetReport.Observations.Contains("\r\n", StringComparison.Ordinal), "snippet builder does not mutate the source report");
    Assert(ReportMatchSnippetBuilder.Build(snippetReport, string.Empty) is null, "filter-only search does not require snippet generation");
    Assert(ReportMatchSnippetBuilder.Build(new InspectionReport { Number = 5, Date = new DateTime(2026, 7, 1) }, "lintel") is null, "no matching text is handled safely");

    // Baseline Regression Tests
    // These checks define the protected release baseline contract and should not be removed.
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

    Console.WriteLine("\n== New project folders nest under the selected root ==");
    var nestedProjectsRoot = Path.Combine(workspace, "nested_projects_root");
    var suggestedNestedFolder = ProjectLayout.DefaultNewProjectFolderPath(nestedProjectsRoot, "CMF Structural Repairs");
    Assert(
        suggestedNestedFolder.Equals(Path.Combine(nestedProjectsRoot, "CMF Structural Repairs"), StringComparison.OrdinalIgnoreCase),
        "default root + project name creates nested path");
    Assert(
        ProjectLayout.DefaultNewProjectFolderPath(@"D:\Inspection Projects", "Clarifier 6 Repairs")
            .Equals(Path.Combine(@"D:\Inspection Projects", "Clarifier 6 Repairs"), StringComparison.OrdinalIgnoreCase),
        "alternate parent root creates nested path");
    Assert(ProjectLayout.SanitizeProjectFolderName("CMF: Structural / Repairs?") == "CMF_ Structural _ Repairs_", "invalid filename characters sanitized for project folders");
    Assert(ProjectLayout.SanitizeProjectFolderName("CON") == "CON_", "reserved Windows names are made safe for project folders");
    Assert(ProjectLayout.SanitizeProjectFolderName("   ") == "New Project", "blank project folder suggestions fall back safely");
    Assert(ProjectLayout.CanInitializeNewProjectFolder(Path.Combine(workspace, "safe_missing_folder")), "nonexistent folder is safe to initialize");

    var safeEmptyFolder = Path.Combine(workspace, "safe_empty_folder");
    Directory.CreateDirectory(safeEmptyFolder);
    Assert(ProjectLayout.CanInitializeNewProjectFolder(safeEmptyFolder), "empty folder is safe to initialize");

    var safeSignaturesOnlyFolder = Path.Combine(workspace, "safe_signatures_only");
    Directory.CreateDirectory(Path.Combine(safeSignaturesOnlyFolder, ProjectLayout.SignaturesFolderName));
    Assert(ProjectLayout.CanInitializeNewProjectFolder(safeSignaturesOnlyFolder), "folder containing only Signatures is safe to initialize");

    var safeImportedSignaturesFolder = Path.Combine(workspace, "safe_imported_signatures");
    Directory.CreateDirectory(Path.Combine(safeImportedSignaturesFolder, ProjectLayout.SignaturesFolderName));
    File.Copy(photoFiles[0], Path.Combine(safeImportedSignaturesFolder, ProjectLayout.SignaturesFolderName, Path.GetFileName(photoFiles[0])), overwrite: true);
    File.Copy(photoFiles[1], Path.Combine(safeImportedSignaturesFolder, ProjectLayout.SignaturesFolderName, Path.GetFileName(photoFiles[1])), overwrite: true);
    Assert(ProjectLayout.CanInitializeNewProjectFolder(safeImportedSignaturesFolder), "Signatures containing PNG/JPG files is safe to initialize");

    var unsafeProjectJsonFolder = Path.Combine(workspace, "unsafe_project_json");
    Directory.CreateDirectory(unsafeProjectJsonFolder);
    File.WriteAllText(Path.Combine(unsafeProjectJsonFolder, ProjectLayout.ProjectFileName), "{}");
    Assert(!ProjectLayout.CanInitializeNewProjectFolder(unsafeProjectJsonFolder), "project.json makes folder unsafe");

    var unsafeReportsFolder = Path.Combine(workspace, "unsafe_reports");
    Directory.CreateDirectory(Path.Combine(unsafeReportsFolder, ProjectLayout.ReportsFolderName));
    Assert(!ProjectLayout.CanInitializeNewProjectFolder(unsafeReportsFolder), "Reports directory makes folder unsafe");

    var unsafeRootFileFolder = Path.Combine(workspace, "unsafe_root_file");
    Directory.CreateDirectory(unsafeRootFileFolder);
    File.WriteAllText(Path.Combine(unsafeRootFileFolder, "random.txt"), "random");
    Assert(!ProjectLayout.CanInitializeNewProjectFolder(unsafeRootFileFolder), "arbitrary root file makes folder unsafe");

    var unsafeRootDirectoryFolder = Path.Combine(workspace, "unsafe_root_directory");
    Directory.CreateDirectory(Path.Combine(unsafeRootDirectoryFolder, "Old Documents"));
    Assert(!ProjectLayout.CanInitializeNewProjectFolder(unsafeRootDirectoryFolder), "arbitrary root directory makes folder unsafe");

    var nestedProject = ProjectStore.Create(
        suggestedNestedFolder,
        "CMF Structural Repairs",
        "24-9999",
        "Owner",
        "CM",
        "GC",
        templatePath,
        photoFiles[0],
        photoFiles[1]);
    Assert(
        nestedProject.FolderPath.Equals(Path.GetFullPath(suggestedNestedFolder), StringComparison.OrdinalIgnoreCase),
        "created project uses the nested project folder");
    Assert(
        Path.GetDirectoryName(nestedProject.FolderPath)!.Equals(Path.GetFullPath(nestedProjectsRoot), StringComparison.OrdinalIgnoreCase),
        "nested project folder lives directly beneath the selected root");
    Assert(File.Exists(nestedProject.FilePath), "project.json lives inside the nested project folder");
    Assert(Directory.Exists(ProjectLayout.ReportsFolder(nestedProject)), "Reports folder lives inside the nested project folder");
    Assert(Directory.Exists(ProjectLayout.SignaturesFolder(nestedProject)), "Signatures folder lives inside the nested project folder");

    Console.WriteLine("\n== New project creation accepts pre-created Signatures folder ==");
    var preCreatedFolder = Path.Combine(workspace, "precreated_root", "Imported Signatures Project");
    Directory.CreateDirectory(preCreatedFolder);
    var importedInspector = SignatureStore.Import(preCreatedFolder, photoFiles[2], replaceIfExists: false);
    var importedPm = SignatureStore.Import(preCreatedFolder, photoFiles[3], replaceIfExists: false);
    Assert(importedInspector is not null && importedPm is not null, "signature import created the target folder before project creation");
    Assert(ProjectLayout.CanInitializeNewProjectFolder(preCreatedFolder), "pre-created folder containing only Signatures remains safe");
    var preCreatedProject = ProjectStore.Create(
        preCreatedFolder,
        "Imported Signatures Project",
        "24-3001",
        "Owner",
        "CM",
        "GC",
        templatePath,
        SignatureStore.SignatureRelativePath(Path.GetFileName(importedInspector!)!),
        SignatureStore.SignatureRelativePath(Path.GetFileName(importedPm!)!));
    Assert(preCreatedProject.InspectorSignaturePath == SignatureStore.SignatureRelativePath(Path.GetFileName(importedInspector!)!), "imported inspector signature remains selected after project creation");
    Assert(preCreatedProject.ProjectManagerSignaturePath == SignatureStore.SignatureRelativePath(Path.GetFileName(importedPm!)!), "imported PM signature remains selected after project creation");
    Assert(File.Exists(preCreatedProject.ResolvedInspectorSignaturePath!), "imported inspector signature still resolves after project creation");
    Assert(File.Exists(preCreatedProject.ResolvedProjectManagerSignaturePath!), "imported PM signature still resolves after project creation");
    var preCreatedReport = MakeReport(preCreatedProject, 1, 1, "Sunny", photoFiles);
    var preCreatedPreview = ReportGenerator.GenerateDraft(preCreatedProject, preCreatedReport);
    ReportGenerator.FinalizeReport(preCreatedProject, preCreatedReport, preCreatedPreview.OutputPath);
    Assert(File.Exists(ProjectLayout.FinalReportPath(preCreatedProject, preCreatedReport)), "project creation after signature import still supports report finalize");

    var existingProjectCollisionFolder = Path.Combine(workspace, "existing_project_collision");
    var existingProjectCollision = ProjectStore.Create(
        existingProjectCollisionFolder,
        "Existing Collision",
        "24-3002",
        "Owner",
        "CM",
        "GC",
        templatePath,
        photoFiles[0],
        photoFiles[1]);
    Assert(ProjectStore.ResolveProjectJson(existingProjectCollisionFolder) is not null, "existing project collision is still detected authoritatively");
    Assert(!ProjectLayout.CanInitializeNewProjectFolder(existingProjectCollisionFolder), "existing project collision remains unsafe for new project initialization");

    Console.WriteLine("\n== Project rename does not move an existing folder ==");
    var renameProjectFolder = Path.Combine(workspace, "rename_root", "Project A");
    var renameProject = ProjectStore.Create(
        renameProjectFolder,
        "Project A",
        "24-2001",
        "Owner",
        "CM",
        "GC",
        templatePath,
        photoFiles[0],
        photoFiles[1]);
    var renameOriginalFolder = renameProject.FolderPath;
    ProjectStore.Update(
        renameProject,
        "Project B",
        renameProject.Number,
        renameProject.Owner,
        renameProject.ContractManager,
        renameProject.GeneralContractor,
        renameProject.TemplatePath,
        renameProject.InspectorSignaturePath,
        renameProject.ProjectManagerSignaturePath);
    Assert(renameProject.Name == "Project B", "project name can change without recreating the project");
    Assert(renameProject.FolderPath == renameOriginalFolder, "renamed project does not relocate folder");
    Assert(Directory.Exists(renameOriginalFolder), "renamed project folder remains in place");
    Assert(ProjectStore.Load(renameOriginalFolder)!.Name == "Project B", "renamed project persists without moving folder");

    Console.WriteLine("\n== Recent and startup reopen use final nested project folders ==");
    var nestedStartupSettingsPath = Path.Combine(workspace, "nested_settings", "settings.json");
    var nestedSettingsStore = new ApplicationSettingsStore(nestedStartupSettingsPath);
    var nestedStartupSettings = nestedSettingsStore.Load();
    nestedStartupSettings.ReopenLastProjectOnStartup = true;
    nestedStartupSettings.LastOpenedProjectPath = nestedProject.FolderPath;
    nestedSettingsStore.Save(nestedStartupSettings);
    Assert(
        ApplicationSettingsBehavior.GetStartupReopenProjectPath(nestedStartupSettings, nestedSettingsStore) == nestedProject.FolderPath,
        "reopen-last-project uses the final nested folder");

    var recentProjectsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CEIReportGenerator",
        "recent-projects.json");
    Directory.CreateDirectory(Path.GetDirectoryName(recentProjectsFile)!);
    var hadRecentProjectsBackup = File.Exists(recentProjectsFile);
    var recentProjectsBackup = hadRecentProjectsBackup ? File.ReadAllText(recentProjectsFile) : null;
    try
    {
        try
        {
            if (hadRecentProjectsBackup)
            {
                File.Delete(recentProjectsFile);
            }

            RecentProjectStore.Record(nestedProject.Name, nestedProject.FolderPath, 10);
            var recentEntries = RecentProjectStore.Load(10);
            Assert(
                recentEntries.Any(entry => string.Equals(entry.FolderPath, nestedProject.FolderPath, StringComparison.OrdinalIgnoreCase)),
                "recent-project path uses the nested project folder");
            RecentProjectStore.Remove(nestedProject.FolderPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"  note: skipping recent-project store assertion in restricted environment ({ex.Message})");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  note: skipping recent-project store assertion in restricted environment ({ex.Message})");
        }
    }
    finally
    {
        try
        {
            if (recentProjectsBackup is null)
            {
                if (File.Exists(recentProjectsFile))
                {
                    File.Delete(recentProjectsFile);
                }
            }
            else
            {
                File.WriteAllText(recentProjectsFile, recentProjectsBackup);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"  note: skipping recent-project cleanup restore in restricted environment ({ex.Message})");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  note: skipping recent-project cleanup restore in restricted environment ({ex.Message})");
        }
    }

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
    var syncProject = ProjectStore.Create(
        Path.Combine(workspace, "sync_project"),
        "Sync Project",
        "400",
        "Owner",
        "CM",
        "GC",
        templatePath,
        photoFiles[0],
        photoFiles[1]);
    syncProject.NextReportNumber = 8;
    ProjectStore.Save(syncProject);
    var syncDraft2 = MakeReport(syncProject, 2, 0, "Sunny", photoFiles);
    var syncDraft3 = MakeReport(syncProject, 3, 0, "Sunny", photoFiles);
    ReportStore.SaveReport(syncProject, syncDraft2);
    ReportStore.SaveReport(syncProject, syncDraft3);
    Assert(ProjectStore.SynchronizeNextReportNumber(syncProject) == 1, "draft-only reports do not permanently increase next report number");

    var syncPreview2 = ReportGenerator.GenerateDraft(syncProject, syncDraft2);
    ReportGenerator.FinalizeReport(syncProject, syncDraft2, syncPreview2.OutputPath);
    var syncPreview3 = ReportGenerator.GenerateDraft(syncProject, syncDraft3);
    ReportGenerator.FinalizeReport(syncProject, syncDraft3, syncPreview3.OutputPath);
    Assert(ProjectStore.SynchronizeNextReportNumber(syncProject) == 4, "highest finalized report drives next report number");
    var syncReloaded = ProjectStore.Load(syncProject.FolderPath)!;
    Assert(syncReloaded.NextReportNumber == 4, "synchronized next report persists from finalized reports");

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

    var finalReportPath = ProjectLayout.FinalReportPath(project, finalizeReport);
    Assert(!File.Exists(finalReportPath), "final report file does not exist before accept");
    ReportGenerator.FinalizeReport(project, finalizeReport, finalizeResult!.OutputPath);
    Assert(finalizeReport.Status == ReportStatus.Final, "report status is Final");
    Assert(File.Exists(finalReportPath), "final report file exists after accept");
    Assert(Path.GetFileName(finalReportPath) == "2026-08-05 Demo Project SPIN Report #4.docx", "final report file uses the SPIN naming pattern");
    Assert(finalizeReport.OutputFileName == "2026-08-05 Demo Project SPIN Report #4.docx", "finalized report stores the SPIN output file name");
    Assert(!File.Exists(ProjectLayout.ReportPreviewPath(project, finalizeReport.Number)), "preview cleaned up after finalization");

    var finalProject = ProjectStore.Load(projectFolder);
    Assert(finalProject!.NextReportNumber == 5, $"next report number advances to finalized report + 1 (expected 5, got {finalProject.NextReportNumber})");
    var savedReport = ReportStore.LoadReport(finalProject, finalizeReport.Number);
    Assert(savedReport is not null, "report reloaded from disk");
    Assert(savedReport!.Status == ReportStatus.Final, "reloaded report is Final");
    Assert(savedReport.Photos.Count == 3, "reloaded report has 3 photos");
    Assert(savedReport.Photos.Select(photo => photo.StoredFileName).SequenceEqual([
        "image1_Photo 1 - Site photo documentation..png",
        "image3_Photo 2 - Site photo documentation..png",
        "image5_Photo 3 - Site photo documentation..jpeg"
    ]), "finalized report renames stored project photo copies after successful finalization");
    Console.WriteLine($"  Report {savedReport.Number} status: {savedReport.Status}, photos: {savedReport.Photos.Count}");

    var allReports = ReportStore.LoadAllReports(finalProject);
    Assert(allReports.Reports.Count == 1, "one report.json listed in project (only the finalized report was saved)");

    var finalizedDocHashBeforeMetadataEdit = FileHash(finalReportPath);
    finalizeReport.Observations = "Corrected searchable metadata only.";
    finalizeReport.NewDiscrepancies = "Metadata correction only.";
    ReportGenerator.SaveDraft(finalProject, finalizeReport);
    var metadataEditedReport = ReportStore.LoadReport(finalProject, finalizeReport.Number);
    Assert(metadataEditedReport is not null, "final report metadata edits reload from report.json");
    Assert(metadataEditedReport!.Status == ReportStatus.Final, "Save Changes preserves final status");
    Assert(metadataEditedReport.OutputFileName == finalizeReport.OutputFileName, "Save Changes preserves finalized output file name");
    Assert(metadataEditedReport.CreatedUtc == finalizeReport.CreatedUtc, "Save Changes preserves created timestamp");
    Assert(metadataEditedReport.Observations == "Corrected searchable metadata only.", "Save Changes updates searchable JSON metadata");
    Assert(FileHash(finalReportPath) == finalizedDocHashBeforeMetadataEdit, "Save Changes does not modify the finalized Word report");
    ExpectActionFailure(
        () => ReportGenerator.GenerateDraft(finalProject, finalizeReport),
        message => message.Contains("Generate Report is disabled", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine("\n== Finalized photo copies are renamed safely ==");
    var renamedPhotoProject = ProjectStore.Create(
        Path.Combine(workspace, "renamed_photo_project"), "Renamed Photos", "73", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var renamedPhotoReport = MakeReport(renamedPhotoProject, 216, 0, "Sunny", photoFiles);
    renamedPhotoReport.Photos =
    [
        new Photo { SourcePath = photoFiles[0], Caption = "North wall reinforcing" },
        new Photo { SourcePath = photoFiles[2], Caption = string.Empty },
        new Photo
        {
            SourcePath = photoFiles[4],
            Caption = "CMU: lintel / east wall? with <unsafe> characters | and a very long caption that should be trimmed before the file name becomes unreasonable for field use."
        }
    ];
    ReportGenerator.SaveDraft(renamedPhotoProject, renamedPhotoReport);
    var renamedPhotosFolder = ProjectLayout.ReportPhotosFolder(renamedPhotoProject, renamedPhotoReport.Number);
    File.WriteAllText(Path.Combine(renamedPhotosFolder, "image1_Photo 1 - North wall reinforcing.png"), "existing collision");
    var renamedPhotoPreview = ReportGenerator.GenerateDraft(renamedPhotoProject, renamedPhotoReport);
    ReportGenerator.FinalizeReport(renamedPhotoProject, renamedPhotoReport, renamedPhotoPreview.OutputPath);
    var renamedPhotoReloaded = ReportStore.LoadReport(renamedPhotoProject, renamedPhotoReport.Number)!;
    Assert(renamedPhotoReloaded.Photos[0].StoredFileName == "image1_Photo 1 - North wall reinforcing_2.png", "photo rename appends a deterministic suffix when the target name already exists");
    Assert(renamedPhotoReloaded.Photos[1].StoredFileName == "image3_Photo 2.png", "blank caption photo rename omits the caption suffix");
    Assert(renamedPhotoReloaded.Photos[2].StoredFileName.StartsWith("image5_Photo 3 - ", StringComparison.Ordinal), "photo rename preserves original file stem and photo number");
    Assert(!renamedPhotoReloaded.Photos[2].StoredFileName.Any(ch => "\\/:*?\"<>|".Contains(ch)), "photo caption sanitization removes invalid file-name characters");
    Assert(Path.GetFileNameWithoutExtension(renamedPhotoReloaded.Photos[2].StoredFileName).Length <= "image5_Photo 3 - ".Length + 72, "photo caption truncation limits the appended caption length");
    Assert(File.Exists(ReportStore.StoredPhotoPath(renamedPhotoProject, renamedPhotoReloaded, renamedPhotoReloaded.Photos[0])), "renamed stored photo file exists after finalization");
    Assert(renamedPhotoReloaded.Photos[0].SourcePath == photoFiles[0], "photo rename does not modify the source photo path stored in report.json");
    Assert(renamedPhotoReloaded.Photos[1].SourcePath == photoFiles[2], "blank-caption photo rename leaves source path unchanged");
    var renamedPhotoFinalText = ReadBodyText(ProjectLayout.FinalReportPath(renamedPhotoProject, renamedPhotoReport));
    Assert(renamedPhotoFinalText.Contains("216"), "generated report header displays the unpadded report number");
    Assert(!renamedPhotoFinalText.Contains("0216", StringComparison.Ordinal), "generated report header no longer displays a padded report number");

    Console.WriteLine("\n== Final DOCX collision never overwrites ==");
    var collisionProject = ProjectStore.Create(
        Path.Combine(workspace, "collision_project"), "Collision", "13", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var collisionReport = MakeReport(collisionProject, 1, 1, "Sunny", photoFiles);
    var collisionPreview = ReportGenerator.GenerateDraft(collisionProject, collisionReport);
    var collisionFinalPath = ProjectLayout.FinalReportPath(collisionProject, collisionReport);
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

    Console.WriteLine("\n== Final report naming avoids duplicate SPIN and trims leading zeros ==");
    var namingProject = ProjectStore.Create(
        Path.Combine(workspace, "naming_project"), "CMF Structural Repairs", "88", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    AssertFinalFileName(namingProject, 1, "2026-08-05 CMF Structural Repairs SPIN Report #1.docx", photoFiles);
    AssertFinalFileName(namingProject, 9, "2026-08-05 CMF Structural Repairs SPIN Report #9.docx", photoFiles);
    AssertFinalFileName(namingProject, 21, "2026-08-05 CMF Structural Repairs SPIN Report #21.docx", photoFiles);
    AssertFinalFileName(namingProject, 216, "2026-08-05 CMF Structural Repairs SPIN Report #216.docx", photoFiles);

    var spinNamedProject = ProjectStore.Create(
        Path.Combine(workspace, "spin_named_project"), "CMF Structural Repairs SPIN", "90", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var spinFileNameInfo = ProjectLayout.BuildFinalReportFileNameInfo(spinNamedProject, MakeReport(spinNamedProject, 216, 1, "Sunny", photoFiles));
    Assert(spinFileNameInfo.FileName == "2026-08-05 CMF Structural Repairs SPIN Report #216.docx", "final file name avoids duplicate SPIN when the project name already ends with SPIN");
    Assert(!spinFileNameInfo.FileName.Contains("SPIN SPIN", StringComparison.OrdinalIgnoreCase), "final file name never duplicates SPIN");
    Assert(!spinFileNameInfo.FileName.Contains("#0216", StringComparison.Ordinal), "final file name never pads the visible report number");

    var crispinProject = ProjectStore.Create(
        Path.Combine(workspace, "crispin_naming_project"), "Crispin Structural Repairs", "92", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var crispinFileNameInfo = ProjectLayout.BuildFinalReportFileNameInfo(crispinProject, MakeReport(crispinProject, 216, 1, "Sunny", photoFiles));
    Assert(crispinFileNameInfo.FileName == "2026-08-05 Crispin Structural Repairs SPIN Report #216.docx", "project names ending with Crispin still keep the inserted SPIN word");

    var sanitizedNamingProject = ProjectStore.Create(
        Path.Combine(workspace, "sanitized_naming_project"), "CMF: Structural/Repairs?", "91", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var sanitizedFileNameInfo = ProjectLayout.BuildFinalReportFileNameInfo(sanitizedNamingProject, MakeReport(sanitizedNamingProject, 12, 1, "Sunny", photoFiles));
    Assert(sanitizedFileNameInfo.FileName == "2026-08-05 CMF_ Structural_Repairs_ SPIN Report #12.docx", "final file name still sanitizes invalid file-name characters");

    Console.WriteLine("\n== Report deletion is atomic and removes only the selected report data ==");
    var deleteProject = ProjectStore.Create(
        Path.Combine(workspace, "delete_project"), "Delete Project", "89", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    deleteProject.NextReportNumber = 217;
    ProjectStore.Save(deleteProject);

    var draftReport214 = MakeReport(deleteProject, 214, 2, "Sunny", photoFiles);
    ReportGenerator.SaveDraft(deleteProject, draftReport214);

    var finalReport215 = MakeReport(deleteProject, 215, 1, "Sunny", photoFiles);
    var finalPreview215 = ReportGenerator.GenerateDraft(deleteProject, finalReport215);
    ReportGenerator.FinalizeReport(deleteProject, finalReport215, finalPreview215.OutputPath);

    var finalReport216 = MakeReport(deleteProject, 216, 2, "Sunny", photoFiles);
    var finalPreview216 = ReportGenerator.GenerateDraft(deleteProject, finalReport216);
    ReportGenerator.FinalizeReport(deleteProject, finalReport216, finalPreview216.OutputPath);

    var report216Folder = ProjectLayout.ReportFolder(deleteProject, 216);
    var report216Json = ProjectLayout.ReportFilePath(deleteProject, 216);
    var report216Photos = ProjectLayout.ReportPhotosFolder(deleteProject, 216);
    var report216Working = ProjectLayout.ReportWorkingFolder(deleteProject, 216);
    var report216Final = ProjectLayout.FinalReportPath(deleteProject, finalReport216);
    var projectJsonBeforeDelete = File.ReadAllBytes(deleteProject.FilePath);
    var signaturesBeforeDelete = Directory.GetFiles(ProjectLayout.SignaturesFolder(deleteProject), "*", SearchOption.TopDirectoryOnly)
        .Select(path => Path.GetFileName(path))
        .OrderBy(name => name)
        .ToArray();

    Assert(Directory.Exists(report216Folder), "selected report folder exists before deletion");
    Assert(File.Exists(report216Json), "selected report.json exists before deletion");
    Assert(Directory.Exists(report216Photos), "selected report photos folder exists before deletion");
    Assert(File.Exists(report216Final), "selected finalized report file exists before deletion");
    Assert(!Directory.Exists(report216Working), "selected working folder is already cleaned after finalization");
    Assert(Directory.Exists(ProjectLayout.ReportFolder(deleteProject, 214)), "other draft report folder exists before deletion");
    Assert(Directory.Exists(ProjectLayout.ReportFolder(deleteProject, 215)), "other final report folder exists before deletion");

    string? renamedSource = null;
    string? renamedDestination = null;
    ReportStore.RenameObserverForTesting = (source, destination) =>
    {
        renamedSource = source;
        renamedDestination = destination;
        Assert(Directory.Exists(source), "canonical report folder still exists immediately before rename");
    };
    try
    {
        Assert(ReportStore.DeleteReport(deleteProject, 216) == ReportStore.DeleteReportStatus.Deleted, "delete report returns deleted when the report folder exists");
    }
    finally
    {
        ReportStore.RenameObserverForTesting = null;
    }

    Assert(renamedSource == report216Folder, "delete report renames the canonical report folder before deletion");
    Assert(!string.IsNullOrWhiteSpace(renamedDestination), "delete report uses a temporary deleting folder");
    Assert(Path.GetFileName(renamedDestination!).StartsWith(".0216.deleting.", StringComparison.OrdinalIgnoreCase), "temporary deleting folder stays inside Reports with the expected naming pattern");
    Assert(!Directory.Exists(renamedDestination), "temporary deleting folder is removed after successful recursive deletion");
    Assert(!Directory.Exists(report216Folder), "delete report removes the selected report folder");
    Assert(!File.Exists(report216Json), "delete report removes the selected report.json");
    Assert(!Directory.Exists(report216Photos), "delete report removes the selected report photos folder");
    Assert(!File.Exists(report216Final), "delete report removes the selected finalized report file");
    Assert(!Directory.Exists(report216Working), "delete report leaves no working folder behind");
    Assert(Directory.Exists(ProjectLayout.ReportFolder(deleteProject, 214)), "delete report does not remove other draft report folders");
    Assert(Directory.Exists(ProjectLayout.ReportFolder(deleteProject, 215)), "delete report does not remove other final report folders");
    Assert(
        Directory.GetFiles(ProjectLayout.SignaturesFolder(deleteProject), "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name)
            .SequenceEqual(signaturesBeforeDelete),
        "delete report does not modify stored signatures");

    var deleteReloadedProject = ProjectStore.Load(deleteProject.FolderPath)!;
    Assert(!File.ReadAllBytes(deleteProject.FilePath).SequenceEqual(projectJsonBeforeDelete), "deleting the highest finalized report updates project.json next report number");
    Assert(deleteReloadedProject.NextReportNumber == 216, "deleting the highest finalized report immediately recomputes the next report number");
    var deleteLoadResult = ReportStore.LoadAllReports(deleteReloadedProject);
    Assert(deleteLoadResult.Reports.Count == 2, "deleted report is no longer returned from report loading");
    Assert(deleteLoadResult.Reports.All(report => report.Number != 216), "deleted report number is absent after reload");
    Assert(deleteLoadResult.Reports.Any(report => report.Number == 214), "remaining draft report still loads after deletion");
    Assert(deleteLoadResult.Reports.Any(report => report.Number == 215), "remaining final report still loads after deletion");
    Assert(ReportStore.GetNextReportNumber(deleteReloadedProject) == 216, "authoritative next report number recomputes from the highest remaining finalized report");
    Assert(ReportStore.DeleteReport(deleteReloadedProject, 216) == ReportStore.DeleteReportStatus.NotFound, "delete report returns not found when the report folder is already gone");

    Console.WriteLine("\n== Failed report rename leaves the report untouched ==");
    var renameFailureProject = ProjectStore.Create(
        Path.Combine(workspace, "rename_failure_project"), "Rename Failure", "93", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var renameFailureReport = MakeReport(renameFailureProject, 12, 1, "Sunny", photoFiles);
    ReportGenerator.SaveDraft(renameFailureProject, renameFailureReport);
    var renameFailureFolder = ProjectLayout.ReportFolder(renameFailureProject, renameFailureReport.Number);
    var renameFailureJson = ProjectLayout.ReportFilePath(renameFailureProject, renameFailureReport.Number);
    ReportStore.RenameFailureHookForTesting = (_, _) => new IOException("Simulated locked file during rename.");
    try
    {
        Assert(ReportStore.DeleteReport(renameFailureProject, renameFailureReport.Number) == ReportStore.DeleteReportStatus.InUse, "locked rename scenario returns in-use");
    }
    finally
    {
        ReportStore.RenameFailureHookForTesting = null;
    }

    Assert(Directory.Exists(renameFailureFolder), "failed rename leaves the canonical report folder intact");
    Assert(File.Exists(renameFailureJson), "failed rename leaves report.json intact");
    Assert(ReportStore.LoadAllReports(renameFailureProject).Reports.Count == 1, "dashboard reload still sees the untouched report after rename failure");
    Assert(ProjectStore.Load(renameFailureProject.FolderPath)!.NextReportNumber == renameFailureProject.NextReportNumber, "failed rename does not change next report number");
    Assert(!Directory.EnumerateDirectories(ProjectLayout.ReportsFolder(renameFailureProject)).Any(path => Path.GetFileName(path).Contains(".deleting.", StringComparison.OrdinalIgnoreCase)),
        "failed rename leaves no abandoned deleting folder");

    Console.WriteLine("\n== Abandoned deleting folders are cleaned on next load ==");
    var cleanupProject = ProjectStore.Create(
        Path.Combine(workspace, "cleanup_project"), "Cleanup Project", "94", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var cleanupReport = MakeReport(cleanupProject, 33, 1, "Sunny", photoFiles);
    ReportGenerator.SaveDraft(cleanupProject, cleanupReport);
    string? abandonedDeletingFolder = null;
    ReportStore.RenameObserverForTesting = (_, destination) => abandonedDeletingFolder = destination;
    ReportStore.DeleteFailureHookForTesting = _ => new IOException("Simulated delete failure after rename.");
    try
    {
        ExpectActionFailure(
            () => ReportStore.DeleteReport(cleanupProject, cleanupReport.Number),
            message => message.Contains("Simulated delete failure after rename.", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        ReportStore.RenameObserverForTesting = null;
        ReportStore.DeleteFailureHookForTesting = null;
    }

    Assert(!Directory.Exists(ProjectLayout.ReportFolder(cleanupProject, cleanupReport.Number)), "after rename-plus-delete failure the canonical report folder is already gone");
    Assert(!string.IsNullOrWhiteSpace(abandonedDeletingFolder), "delete failure after rename leaves an abandoned deleting folder");
    Assert(Directory.Exists(abandonedDeletingFolder!), "abandoned deleting folder remains for future cleanup");
    var cleanupLoadResult = ReportStore.LoadAllReports(cleanupProject);
    Assert(cleanupLoadResult.Reports.Count == 0, "cleanup removes abandoned deleting folders before dashboard reload");
    Assert(!Directory.Exists(abandonedDeletingFolder!), "abandoned deleting folder is removed during the next cleanup pass");
    Assert(ProjectStore.Load(cleanupProject.FolderPath)!.NextReportNumber == cleanupProject.NextReportNumber, "cleanup of abandoned deleting folders does not change next report number");

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
    Assert(!File.Exists(ProjectLayout.FinalReportPath(rollbackProject, rollbackReport)), "final report not promoted after rollback failure");
    Assert(!Directory.EnumerateFiles(ProjectLayout.ReportFolder(rollbackProject, 1), "*.finalizing.docx", SearchOption.TopDirectoryOnly).Any(),
        "no finalizing artifacts remain after rollback");
    Assert(rollbackProject.NextReportNumber == 1, "in-memory next report restored after rollback");
    var rollbackLoadedReport = ReportStore.LoadReport(rollbackProject, 1);
    Assert(rollbackLoadedReport is null || rollbackLoadedReport.Status != ReportStatus.Final, "report.json not left finalized after rollback");
    var rollbackPhotosFolder = ProjectLayout.ReportPhotosFolder(rollbackProject, rollbackReport.Number);
    Assert(!Directory.Exists(rollbackPhotosFolder), "failed finalization without a prior draft leaves no orphan stored photo folder");
    Assert(!Directory.EnumerateFiles(ProjectLayout.ReportFolder(rollbackProject, rollbackReport.Number), "*SPIN Report #*.docx", SearchOption.TopDirectoryOnly).Any(),
        "failed finalization without a prior draft leaves no finalized report artifacts behind");

    Console.WriteLine("\n== Historical report search-index import contract ==");
    var invalidImportFolder = Path.Combine(workspace, "historical_invalid_project");
    var invalidImportReportsFolder = Path.Combine(invalidImportFolder, ProjectLayout.ReportsFolderName);
    var invalidImportStagingFolder = Path.Combine(invalidImportReportsFolder, ".importing.0216.test");
    Directory.CreateDirectory(invalidImportStagingFolder);
    var invalidImportSentinel = Path.Combine(invalidImportStagingFolder, "sentinel.txt");
    File.WriteAllText(invalidImportSentinel, "keep me");
    var invalidImportSourceDocx = Path.Combine(workspace, "historical_invalid_project_source.docx");
    File.Copy(templatePath, invalidImportSourceDocx);
    var invalidImportProject = new Project
    {
        Name = "Not A Project",
        Number = "9999",
        Owner = "Owner",
        ContractManager = "CM",
        GeneralContractor = "GC",
        FolderPath = invalidImportFolder
    };
    var invalidImportResult = HistoricalReportImportService.Import(
        invalidImportProject,
        new HistoricalReportImportRequest
        {
            Number = 216,
            Date = new DateTime(2025, 7, 10),
            Weather = "Partly Cloudy",
            Locations = "Gridline 2",
            Inspectors = "Anthony Wintergerst",
            PersonnelOnSite = "CMU Crew",
            DescriptionOfWork = "Filesystem inert invalid-project test.",
            DrawingsReviewed = "HX-INVALID-216",
            Observations = "Invalid project should not trigger cleanup.",
            SourceDocumentPath = invalidImportSourceDocx
        });
    Assert(invalidImportResult.Status == HistoricalReportImportStatus.InvalidProject, "invalid project returns InvalidProject");
    Assert(string.IsNullOrEmpty(invalidImportResult.ReportFolder), "invalid project result leaves report folder path empty");
    Assert(string.IsNullOrEmpty(invalidImportResult.ReportJsonPath), "invalid project result leaves report.json path empty");
    Assert(string.IsNullOrEmpty(invalidImportResult.ImportMetadataPath), "invalid project result leaves metadata path empty");
    Assert(Directory.Exists(invalidImportStagingFolder), "invalid project does not clean abandoned importing folders");
    Assert(File.Exists(invalidImportSentinel), "invalid project leaves sentinel file untouched");
    Assert(!File.Exists(Path.Combine(invalidImportFolder, ProjectLayout.ProjectFileName)), "invalid project import does not create project.json");

    var historicalImportProject = ProjectStore.Create(
        Path.Combine(workspace, "historical_import_project"), "Historical Search", "2160", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    for (var i = 1; i <= 25; i++)
    {
        ReportStore.SaveReport(historicalImportProject, MakeReport(historicalImportProject, i, 0, "Sunny", photoFiles));
    }

    var historicalArchiveFolder = Path.Combine(workspace, "historical_archive");
    Directory.CreateDirectory(historicalArchiveFolder);
    var historicalSourceDocx = Path.Combine(historicalArchiveFolder, "2025-07-10 CMF Structural Repairs SPIN Report #216.docx");
    File.Copy(templatePath, historicalSourceDocx);

    var historicalImportRequest = new HistoricalReportImportRequest
    {
        Number = 216,
        Date = new DateTime(2025, 7, 10),
        Temperature = "85",
        Weather = "Partly Cloudy",
        Locations = "Gridline 2",
        Inspectors = "Anthony Wintergerst",
        PersonnelOnSite = "CMU Crew",
        DescriptionOfWork = "CMU lintel reinforcement review.",
        DrawingsReviewed = "HX-216",
        Observations = "CMU lintel reinforcement and horizontal ladder reinforcing were reviewed.",
        NewDiscrepancies = string.Empty,
        PreviousDiscrepancies = string.Empty,
        SourceDocumentPath = historicalSourceDocx,
        SourceCreatedUtc = new DateTime(2025, 7, 10, 14, 0, 0, DateTimeKind.Utc)
    };
    var historicalAbandonedStagingFolder = Path.Combine(ProjectLayout.ReportsFolder(historicalImportProject), ".importing.0216.test");
    Directory.CreateDirectory(historicalAbandonedStagingFolder);
    File.WriteAllText(Path.Combine(historicalAbandonedStagingFolder, "abandoned.txt"), "remove me");

    var historicalImportResult = HistoricalReportImportService.Import(historicalImportProject, historicalImportRequest);
    Assert(historicalImportResult.Status == HistoricalReportImportStatus.Imported, "historical import returns Imported status");
    Assert(historicalImportResult.Report is not null, "historical import returns the normalized report");
    Assert(Directory.Exists(historicalImportResult.ReportFolder), "historical import creates the canonical report folder");
    Assert(File.Exists(historicalImportResult.ReportJsonPath), "historical import writes report.json");
    Assert(File.Exists(historicalImportResult.ImportMetadataPath), "historical import writes import-metadata.json");
    Assert(!Directory.EnumerateFiles(historicalImportResult.ReportFolder, "*.docx", SearchOption.TopDirectoryOnly).Any(), "historical import does not copy a DOCX into the report folder");
    Assert(!Directory.Exists(historicalAbandonedStagingFolder), "valid project import still cleans abandoned importing folders");

    var importedHistoricalReport = ReportStore.LoadReport(historicalImportProject, 216);
    Assert(importedHistoricalReport is not null, "historical import report reloads from report.json");
    Assert(importedHistoricalReport!.Status == ReportStatus.Final, "historical import persists Final status");
    Assert(string.IsNullOrWhiteSpace(importedHistoricalReport.OutputFileName), "historical import keeps OutputFileName empty when no local DOCX exists");
    Assert(importedHistoricalReport.Photos.Count == 0, "historical import stores no photos in the pilot slice");
    Assert(importedHistoricalReport.CreatedUtc == historicalImportRequest.SourceCreatedUtc, "historical import uses SourceCreatedUtc when provided");

    var historicalMetadata = JsonStore.Load<HistoricalImportMetadata>(historicalImportResult.ImportMetadataPath);
    Assert(historicalMetadata is not null, "historical import metadata reloads");
    Assert(historicalMetadata!.SourceFileName == Path.GetFileName(historicalSourceDocx), "historical import metadata stores source file name");
    Assert(historicalMetadata.SourcePathAtImport == historicalSourceDocx, "historical import metadata stores source path provenance");
    Assert(historicalMetadata.SourceSha256 == FileHash(historicalSourceDocx).ToLowerInvariant(), "historical import metadata stores source SHA-256");
    Assert(historicalMetadata.ParserProfile == HistoricalReportImportService.DefaultParserProfile, "historical import metadata stores parser profile");
    Assert(historicalMetadata.ContractVersion == HistoricalReportImportService.ContractVersion, "historical import metadata stores contract version");
    Assert(historicalMetadata.Warnings.Count == 0, "historical import metadata starts with no warnings");

    var historicalLoadResult = ReportStore.LoadAllReports(historicalImportProject);
    Assert(historicalLoadResult.Reports.Any(r => r.Number == 216), "historical import loads through ReportStore.LoadAllReports");
    Assert(SearchReportNumbers(historicalLoadResult.Reports, new ReportSearchCriteria { SearchText = "lintel" }).SequenceEqual([216]), "historical import is searchable by observations content");
    Assert(SearchReportNumbers(historicalLoadResult.Reports, new ReportSearchCriteria { SearchText = "ladder reinforcing" }).SequenceEqual([216]), "historical import matches multi-word observations search");
    Assert(SearchReportNumbers(historicalLoadResult.Reports, new ReportSearchCriteria { SearchText = "Gridline 2" }).SequenceEqual([216]), "historical import is searchable by location");
    Assert(SearchReportNumbers(historicalLoadResult.Reports, new ReportSearchCriteria { SearchText = "CMU Crew" }).SequenceEqual([216]), "historical import is searchable by personnel");
    Assert(SearchReportNumbers(historicalLoadResult.Reports, new ReportSearchCriteria { SearchText = "HX-216" }).SequenceEqual([216]), "historical import is searchable by drawings reviewed");

    var historicalMatch = ReportMatchSnippetBuilder.Build(importedHistoricalReport, "ladder reinforcing");
    Assert(historicalMatch is not null, "historical import produces a normal match snippet");
    Assert(historicalMatch!.MatchField == "Observations", "historical import match snippet uses the normal field-priority rules");
    Assert(historicalMatch.MatchDisplay.Contains("ladder reinforcing", StringComparison.OrdinalIgnoreCase), "historical import match snippet includes the search text");

    var historicalSummary = ProjectDashboardSummaryBuilder.Build(historicalImportProject, historicalLoadResult);
    Assert(historicalSummary.TotalReports == 26, "historical import counts toward total reports");
    Assert(historicalSummary.FinalReports == 1, "historical import counts toward final reports");
    Assert(historicalSummary.NextReportNumber == 217, "historical import advances authoritative next report number beyond imported history");
    Assert(ReportStore.GetNextReportNumber(historicalImportProject) == 217, "historical import updates next-number collision protection");
    Assert(ReportDraftFactory.CreateBlank(historicalImportProject).Number == 217, "new blank report after import avoids imported report-number collisions");

    var historicalNewFromSelected = ReportDraftFactory.CreateFromExisting(historicalImportProject, importedHistoricalReport);
    Assert(historicalNewFromSelected.Number == 217, "New Report from Selected uses the next safe number after historical import");
    Assert(historicalNewFromSelected.Locations == importedHistoricalReport.Locations, "New Report from Selected copies imported historical locations");
    Assert(historicalNewFromSelected.DescriptionOfWork == importedHistoricalReport.DescriptionOfWork, "New Report from Selected copies imported historical work description");
    Assert(historicalNewFromSelected.Photos.Count == 0, "New Report from Selected from historical import still starts with empty photos");

    var historicalReportJsonBeforeCollision = File.ReadAllBytes(historicalImportResult.ReportJsonPath);
    var historicalCollisionSourceDocx = Path.Combine(historicalArchiveFolder, "2025-07-10 Duplicate Historical Report #216.docx");
    File.Copy(templatePath, historicalCollisionSourceDocx);
    var historicalCollisionResult = HistoricalReportImportService.Import(
        historicalImportProject,
        historicalImportRequest with { SourceDocumentPath = historicalCollisionSourceDocx });
    Assert(historicalCollisionResult.Status == HistoricalReportImportStatus.ReportAlreadyExists, "historical import returns a controlled collision when report number already exists");
    Assert(File.ReadAllBytes(historicalImportResult.ReportJsonPath).SequenceEqual(historicalReportJsonBeforeCollision), "historical collision leaves the original imported report.json unchanged");

    var historicalConflictProject = ProjectStore.Create(
        Path.Combine(workspace, "historical_import_conflict"), "Historical Conflict", "2161", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var historicalConflictFolder = ProjectLayout.ReportFolder(historicalConflictProject, 217);
    Directory.CreateDirectory(historicalConflictFolder);
    File.WriteAllText(Path.Combine(historicalConflictFolder, "notes.txt"), "unexpected content");
    var historicalFolderConflict = HistoricalReportImportService.Import(
        historicalConflictProject,
        historicalImportRequest with
        {
            Number = 217,
            SourceDocumentPath = historicalCollisionSourceDocx
        });
    Assert(historicalFolderConflict.Status == HistoricalReportImportStatus.FolderConflict, "historical import returns a controlled folder conflict when canonical folder already exists");

    File.Delete(historicalSourceDocx);
    var historicalReloadedProject = ProjectStore.Load(historicalImportProject.FolderPath)!;
    var historicalReloadedResult = ReportStore.LoadAllReports(historicalReloadedProject);
    Assert(historicalReloadedResult.Reports.Any(r => r.Number == 216), "historical import still loads after the source archive document is removed");
    Assert(SearchReportNumbers(historicalReloadedResult.Reports, new ReportSearchCriteria { SearchText = "ladder reinforcing" }).SequenceEqual([216]), "historical search remains independent of source archive availability");
    var historicalReloadedSummary = ProjectDashboardSummaryBuilder.Build(historicalReloadedProject, historicalReloadedResult);
    Assert(historicalReloadedSummary.TotalReports == 26 && historicalReloadedSummary.FinalReports == 1, "historical import still counts correctly after archive removal");

    Console.WriteLine("\n== Historical import rejects invalid number/date before staging ==");
    var historicalValidationProject = ProjectStore.Create(
        Path.Combine(workspace, "historical_validation_project"), "Historical Validation", "2164", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var historicalValidationArchiveFolder = Path.Combine(workspace, "historical_validation_archive");
    Directory.CreateDirectory(historicalValidationArchiveFolder);
    var historicalValidationSourceDocx = Path.Combine(historicalValidationArchiveFolder, "2025-07-13 Historical Validation Report #90.docx");
    File.Copy(templatePath, historicalValidationSourceDocx);
    var validationReportsRoot = ProjectLayout.ReportsFolder(historicalValidationProject);
    ExpectActionFailure(
        () => HistoricalReportImportService.Import(
            historicalValidationProject,
            new HistoricalReportImportRequest
            {
                Number = 0,
                Date = new DateTime(2025, 7, 13),
                Weather = "Cloudy",
                Locations = "Gridline 9",
                Inspectors = "Anthony Wintergerst",
                PersonnelOnSite = "Validation Crew",
                DescriptionOfWork = "Invalid number validation test.",
                DrawingsReviewed = "HX-0",
                Observations = "Invalid number validation test.",
                SourceDocumentPath = historicalValidationSourceDocx
            }),
        message => message.Contains("positive whole number", StringComparison.OrdinalIgnoreCase));
    Assert(!Directory.EnumerateDirectories(validationReportsRoot)
        .Any(path => Path.GetFileName(path).StartsWith(".importing.", StringComparison.OrdinalIgnoreCase)),
        "invalid report number does not create staging directories");
    Assert(!Directory.Exists(ProjectLayout.ReportFolder(historicalValidationProject, 0)), "invalid report number does not create a canonical report folder");

    ExpectActionFailure(
        () => HistoricalReportImportService.Import(
            historicalValidationProject,
            new HistoricalReportImportRequest
            {
                Number = 90,
                Date = default,
                Weather = "Cloudy",
                Locations = "Gridline 9",
                Inspectors = "Anthony Wintergerst",
                PersonnelOnSite = "Validation Crew",
                DescriptionOfWork = "Invalid date validation test.",
                DrawingsReviewed = "HX-90",
                Observations = "Invalid date validation test.",
                SourceDocumentPath = historicalValidationSourceDocx
            }),
        message => message.Contains("date is required", StringComparison.OrdinalIgnoreCase));
    Assert(!Directory.EnumerateDirectories(validationReportsRoot)
        .Any(path => Path.GetFileName(path).StartsWith(".importing.", StringComparison.OrdinalIgnoreCase)),
        "invalid date does not create staging directories");
    Assert(!Directory.Exists(ProjectLayout.ReportFolder(historicalValidationProject, 90)), "invalid date does not create a canonical report folder");

    Console.WriteLine("\n== Historical import delete does not touch archive ==");
    var deleteImportedProject = ProjectStore.Create(
        Path.Combine(workspace, "historical_delete_project"), "Historical Delete", "2162", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var deleteImportedArchiveFolder = Path.Combine(workspace, "historical_delete_archive");
    Directory.CreateDirectory(deleteImportedArchiveFolder);
    var deleteImportedSourceDocx = Path.Combine(deleteImportedArchiveFolder, "2025-07-11 Historical Delete Report #44.docx");
    File.Copy(templatePath, deleteImportedSourceDocx);
    var deleteImportedResult = HistoricalReportImportService.Import(
        deleteImportedProject,
        new HistoricalReportImportRequest
        {
            Number = 44,
            Date = new DateTime(2025, 7, 11),
            Weather = "Rainy",
            Locations = "Pier 3",
            Inspectors = "Anthony Wintergerst",
            PersonnelOnSite = "Repair Crew",
            DescriptionOfWork = "Sealant and patch verification.",
            DrawingsReviewed = "SK-44",
            Observations = "Historical import delete behavior test.",
            SourceDocumentPath = deleteImportedSourceDocx
        });
    Assert(deleteImportedResult.Status == HistoricalReportImportStatus.Imported, "historical delete test import succeeds");
    Assert(ReportStore.DeleteReport(deleteImportedProject, 44) == ReportStore.DeleteReportStatus.Deleted, "historical imported report deletes through the normal delete path");
    Assert(!Directory.Exists(deleteImportedResult.ReportFolder), "historical delete removes the canonical report folder");
    Assert(File.Exists(deleteImportedSourceDocx), "historical delete does not delete the source archive DOCX");

    Console.WriteLine("\n== Historical import staging failure leaves no visible report ==");
    var historicalStagingProject = ProjectStore.Create(
        Path.Combine(workspace, "historical_staging_project"), "Historical Staging", "2163", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var historicalStagingArchiveFolder = Path.Combine(workspace, "historical_staging_archive");
    Directory.CreateDirectory(historicalStagingArchiveFolder);
    var historicalStagingSourceDocx = Path.Combine(historicalStagingArchiveFolder, "2025-07-12 Historical Staging Report #88.docx");
    File.Copy(templatePath, historicalStagingSourceDocx);
    HistoricalReportImportService.StageFailureHookForTesting = _ => new IOException("Injected historical import staging failure.");
    try
    {
        ExpectActionFailure(
            () => HistoricalReportImportService.Import(
                historicalStagingProject,
                new HistoricalReportImportRequest
                {
                    Number = 88,
                    Date = new DateTime(2025, 7, 12),
                    Weather = "Cloudy",
                    Locations = "Gridline 8",
                    Inspectors = "Anthony Wintergerst",
                    PersonnelOnSite = "Historical Crew",
                    DescriptionOfWork = "Historical staging failure test.",
                    DrawingsReviewed = "SK-88",
                    Observations = "Historical staging failure test observations.",
                    SourceDocumentPath = historicalStagingSourceDocx
                }),
            message => message.Contains("staging failure", StringComparison.OrdinalIgnoreCase));
    }
    finally
    {
        HistoricalReportImportService.StageFailureHookForTesting = null;
    }

    Assert(!Directory.Exists(ProjectLayout.ReportFolder(historicalStagingProject, 88)), "historical staging failure leaves no canonical report folder");
    Assert(!Directory.EnumerateDirectories(ProjectLayout.ReportsFolder(historicalStagingProject))
        .Any(path => Path.GetFileName(path).StartsWith(".importing.", StringComparison.OrdinalIgnoreCase)),
        "historical staging failure leaves no abandoned importing folder");
    Assert(!ReportStore.LoadAllReports(historicalStagingProject).Reports.Any(r => r.Number == 88), "historical staging failure leaves no visible report for SPINgen");

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

    Console.WriteLine("\n== Historical scanner and deterministic DOCX parser ==");
    var importerProject = ProjectStore.Create(
        Path.Combine(workspace, "historical_scanner_project"), "Scanner Source", "2165", "Owner", "CM", "GC",
        templatePath, photoFiles[0], photoFiles[1]);
    var importerReport = MakeReport(importerProject, 216, 1, "Overcast", photoFiles);
    importerReport.Date = new DateTime(2026, 8, 8);
    importerReport.Locations = "Gridline 2";
    importerReport.Inspectors = "Anthony Pace";
    importerReport.PersonnelOnSite = "Badge 50CFAE40 present";
    importerReport.DescriptionOfWork = "Bond beam reinforcing was inspected";
    importerReport.DrawingsReviewed = "S-201 lintel detail";
    importerReport.Observations = "CMU lintel installation reviewed prior to placement.";
    importerReport.NewDiscrepancies = "Anchor bolts need recheck.";
    importerReport.PreviousDiscrepancies = "Weldback repair completed.";
    var finalizedImporterDocx = FinalizeAndGetOutputPath(importerProject, importerReport);

    var filenameParse = HistoricalFilenameParser.Parse("2026-08-08 CMF Structural Repairs SPIN Report #216.docx");
    Assert(filenameParse.Date == new DateTime(2026, 8, 8), "historical filename parser reads ISO date");
    Assert(filenameParse.ReportNumber == 216, "historical filename parser reads report number");
    Assert(filenameParse.ProjectName == "CMF Structural Repairs", "historical filename parser trims duplicate SPIN token from project name");
    Assert(filenameParse.Warnings.Count == 0, "historical filename parser returns no warnings for canonical file name");

    var filenameMissing = HistoricalFilenameParser.Parse("Legacy report.docx");
    Assert(filenameMissing.ReportNumber is null && filenameMissing.Date is null, "historical filename parser leaves missing fields empty");
    Assert(filenameMissing.Warnings.Count == 2, "historical filename parser warns when date and number are missing");

    var parsedImporterDocx = HistoricalDocumentParser.Parse(finalizedImporterDocx);
    Assert(parsedImporterDocx.Success, "historical parser succeeds on a finalized SPINgen DOCX");
    Assert(parsedImporterDocx.Status == HistoricalReportParseStatus.Parsed, "historical parser returns Parsed when no warnings are present");
    Assert(parsedImporterDocx.Request is not null, "historical parser produces an import request");
    var parsedImporterRequest = parsedImporterDocx.Request ?? throw new InvalidOperationException("Importer parser request should not be null after success.");
    Assert(parsedImporterDocx.ReportNumber == 216 && parsedImporterRequest.Number == 216, "historical parser reads report number from DOCX");
    Assert(parsedImporterDocx.Date == new DateTime(2026, 8, 8) && parsedImporterRequest.Date == new DateTime(2026, 8, 8), "historical parser reads inspection date from DOCX");
    Assert(parsedImporterDocx.ProjectName == "Scanner Source", "historical parser resolves project name");
    Assert(parsedImporterRequest.Weather == "Overcast", "historical parser reads weather");
    Assert(parsedImporterRequest.Locations == "Gridline 2", "historical parser reads locations");
    Assert(parsedImporterRequest.Inspectors == "Anthony Pace", "historical parser reads inspectors");
    Assert(parsedImporterRequest.PersonnelOnSite == "Badge 50CFAE40 present", "historical parser reads personnel on site");
    Assert(parsedImporterRequest.DescriptionOfWork == "Bond beam reinforcing was inspected", "historical parser reads description of work");
    Assert(parsedImporterRequest.DrawingsReviewed == "S-201 lintel detail", "historical parser reads drawings reviewed");
    Assert(parsedImporterRequest.Observations == "CMU lintel installation reviewed prior to placement.", "historical parser reads observations");
    Assert(parsedImporterRequest.NewDiscrepancies == "Anchor bolts need recheck.", "historical parser reads new discrepancies");
    Assert(parsedImporterRequest.PreviousDiscrepancies.Contains("Weldback repair completed.", StringComparison.Ordinal), "historical parser reads previous discrepancies");
    Assert(parsedImporterDocx.ParserProfile == HistoricalReportImportService.DefaultParserProfile, "historical parser stamps the default parser profile");
    Assert(parsedImporterDocx.OverallConfidence == HistoricalConfidenceLevel.High, "historical parser returns high confidence for a clean generated report");
    Assert(parsedImporterDocx.FieldConfidence.ReportNumber == HistoricalConfidenceLevel.High, "historical parser marks report number confidence high when document and filename agree");
    Assert(parsedImporterDocx.FieldConfidence.Date == HistoricalConfidenceLevel.High, "historical parser marks date confidence high when document and filename agree");
    Assert(parsedImporterDocx.FieldExtractions.ReportNumber.Confidence == ExtractionConfidence.High, "historical parser exposes high-confidence report number extraction metadata");
    Assert(parsedImporterDocx.FieldExtractions.ReportNumber.Source.Contains("Filename", StringComparison.Ordinal), "historical parser records report number provenance");
    Assert(parsedImporterDocx.FieldExtractions.InspectionDate.Confidence == ExtractionConfidence.High, "historical parser exposes high-confidence date extraction metadata");
    Assert(parsedImporterDocx.FieldExtractions.Weather.Source.Contains("Weather", StringComparison.OrdinalIgnoreCase), "historical parser records weather field provenance");
    Assert(parsedImporterDocx.FieldExtractions.Observations.Source.Contains("Observations", StringComparison.OrdinalIgnoreCase), "historical parser records observations section provenance");

    var longDateDocx = Path.Combine(workspace, "historical_long_date.docx");
    File.Copy(finalizedImporterDocx, longDateDocx, overwrite: true);
    ReplaceDocumentText(longDateDocx, "2026-08-08", "August 8, 2026");
    var longDateParsed = HistoricalDocumentParser.Parse(longDateDocx);
    Assert(longDateParsed.Success, "historical parser accepts long-form document dates");
    Assert(longDateParsed.Date == new DateTime(2026, 8, 8), "historical parser reads long-form document dates correctly");

    var mismatchDocx = Path.Combine(workspace, "2026-08-09 Scanner Source SPIN Report #999.docx");
    File.Copy(finalizedImporterDocx, mismatchDocx, overwrite: true);
    var mismatchParsed = HistoricalDocumentParser.Parse(mismatchDocx);
    Assert(mismatchParsed.Success, "historical parser still succeeds when filename and document identity disagree");
    Assert(mismatchParsed.Status == HistoricalReportParseStatus.ParsedWithWarnings, "historical parser flags mismatched identity as warnings");
    Assert(mismatchParsed.Warnings.Any(w => w.Contains("report number", StringComparison.OrdinalIgnoreCase)), "historical parser warns on filename/document report number mismatch");
    Assert(mismatchParsed.Warnings.Any(w => w.Contains("inspection date", StringComparison.OrdinalIgnoreCase)), "historical parser warns on filename/document date mismatch");
    Assert(mismatchParsed.FieldConfidence.ReportNumber == HistoricalConfidenceLevel.Medium, "historical parser lowers report number confidence on mismatch");
    Assert(mismatchParsed.FieldConfidence.Date == HistoricalConfidenceLevel.Medium, "historical parser lowers date confidence on mismatch");
    Assert(mismatchParsed.OverallConfidence == HistoricalConfidenceLevel.Medium, "historical parser lowers overall confidence on filename/document mismatch");
    Assert(mismatchParsed.FieldExtractions.ReportNumber.Confidence == ExtractionConfidence.Low, "historical parser exposes low-confidence report number extraction on mismatch");
    Assert(mismatchParsed.FieldExtractions.ReportNumber.Candidates.Count >= 2, "historical parser preserves conflicting report number candidates");
    Assert(mismatchParsed.FieldExtractions.InspectionDate.Confidence == ExtractionConfidence.Low, "historical parser exposes low-confidence date extraction on mismatch");

    var missingFieldsReport = MakeReport(importerProject, 217, 1, "Overcast", photoFiles);
    missingFieldsReport.Date = new DateTime(2026, 8, 7);
    missingFieldsReport.Observations = "OBS-A-REMOVE-123\r\nOBS-B-REMOVE-456";
    var missingFieldsDocx = FinalizeAndGetOutputPath(importerProject, missingFieldsReport);
    ReplaceDocumentText(missingFieldsDocx, "Overcast", string.Empty);
    ReplaceDocumentText(missingFieldsDocx, "OBS-A-REMOVE-123", string.Empty);
    ReplaceDocumentText(missingFieldsDocx, "OBS-B-REMOVE-456", string.Empty);
    var missingFieldsParsed = HistoricalDocumentParser.Parse(missingFieldsDocx);
    Assert(missingFieldsParsed.Success, "historical parser still succeeds when optional text fields are missing");
    Assert(missingFieldsParsed.Status == HistoricalReportParseStatus.ParsedWithWarnings, "historical parser reports warnings when fields are missing");
    Assert(missingFieldsParsed.Warnings.Any(w => w.Contains("Missing weather", StringComparison.OrdinalIgnoreCase)), "historical parser warns when weather is missing");
    Assert(missingFieldsParsed.Warnings.Any(w => w.Contains("Missing observations", StringComparison.OrdinalIgnoreCase)), "historical parser warns when observations are missing");
    Assert(missingFieldsParsed.FieldConfidence.Weather == HistoricalConfidenceLevel.Medium, "historical parser assigns medium confidence to missing optional weather");
    Assert(missingFieldsParsed.FieldConfidence.Observations == HistoricalConfidenceLevel.Low, "historical parser assigns low confidence to missing required observations");
    Assert(missingFieldsParsed.FieldExtractions.Weather.Confidence == ExtractionConfidence.None, "historical parser exposes missing optional weather as no extraction confidence");
    Assert(missingFieldsParsed.FieldExtractions.Observations.Confidence == ExtractionConfidence.None, "historical parser exposes missing required observations as no extraction confidence");

    var invalidDocx = Path.Combine(workspace, "historical_invalid.docx");
    File.WriteAllText(invalidDocx, "not a real docx");
    var invalidParsed = HistoricalDocumentParser.Parse(invalidDocx);
    Assert(!invalidParsed.Success && invalidParsed.Status == HistoricalReportParseStatus.Failed, "historical parser fails cleanly on an invalid DOCX");

    var nonReportDocx = Path.Combine(workspace, "historical_non_report.docx");
    CreateSimpleDocx(nonReportDocx, "General project notes", "This file is not a CEI report.");
    var nonReportParsed = HistoricalDocumentParser.Parse(nonReportDocx);
    Assert(!nonReportParsed.Success && nonReportParsed.Status == HistoricalReportParseStatus.Failed, "historical parser rejects non-report DOCX files");

    var scanRoot = Path.Combine(workspace, "historical_scan_root");
    Directory.CreateDirectory(scanRoot);
    File.Copy(finalizedImporterDocx, Path.Combine(scanRoot, Path.GetFileName(finalizedImporterDocx)), overwrite: true);
    File.Copy(mismatchDocx, Path.Combine(scanRoot, Path.GetFileName(mismatchDocx)), overwrite: true);
    File.Copy(invalidDocx, Path.Combine(scanRoot, Path.GetFileName(invalidDocx)), overwrite: true);
    File.Copy(nonReportDocx, Path.Combine(scanRoot, Path.GetFileName(nonReportDocx)), overwrite: true);
    var nestedScanFolder = Path.Combine(scanRoot, "nested");
    Directory.CreateDirectory(nestedScanFolder);
    File.Copy(longDateDocx, Path.Combine(nestedScanFolder, "Historical long date.docx"), overwrite: true);

    var productionScanner = new HistoricalReportScanner(new HistoricalDocumentParser());
    var flatScan = productionScanner.Scan(new HistoricalReportScanOptions
    {
        SourceFolder = scanRoot,
        IncludeSubfolders = false
    });
    Assert(flatScan.SessionId != Guid.Empty, "historical scan session assigns a non-empty session id");
    Assert(flatScan.SourceFolder == scanRoot, "historical scan session preserves source folder");
    Assert(!flatScan.IncludeSubfolders, "historical scan session preserves recursive setting");
    Assert(flatScan.StartedUtc != default, "historical scan session captures start time");
    Assert(flatScan.CompletedUtc != default, "historical scan session captures completion time");
    Assert(flatScan.CompletedUtc >= flatScan.StartedUtc, "historical scan session completion time is not earlier than start time");
    Assert(flatScan.ParserProfile == HistoricalReportImportService.DefaultParserProfile, "historical scan session uses the active parser profile");
    Assert(flatScan.FilesDiscovered == 4, "historical scanner counts only top-level DOCX files when recursion is disabled");
    Assert(flatScan.ParsedCount == 2, "historical scanner counts top-level parsed files");
    Assert(flatScan.FailedCount == 2, "historical scanner counts top-level failed files");
    Assert(flatScan.Results.Count == 4, "historical scan session contains one result per discovered file");
    Assert(flatScan.Results.All(r => !string.IsNullOrWhiteSpace(r.SourceFilePath)), "historical scan results retain full source file paths");
    Assert(flatScan.Results.Any(r => r.SourceFileName == Path.GetFileName(finalizedImporterDocx)), "historical scan session includes expected source files");

    var recursiveScan = productionScanner.Scan(new HistoricalReportScanOptions
    {
        SourceFolder = scanRoot,
        IncludeSubfolders = true
    });
    Assert(recursiveScan.SourceFolder == scanRoot, "historical recursive session preserves source folder");
    Assert(recursiveScan.IncludeSubfolders, "historical recursive session preserves recursive setting");
    Assert(recursiveScan.SessionId != flatScan.SessionId, "historical rescan creates a new session identity");
    Assert(recursiveScan.FilesDiscovered == 5, "historical scanner includes nested DOCX files when recursion is enabled");
    Assert(recursiveScan.ParsedCount == 3, "historical scanner keeps parsing after failures");
    Assert(recursiveScan.FailedCount == 2, "historical scanner reports failed files without stopping the batch");
    Assert(recursiveScan.WarningCount >= mismatchParsed.Warnings.Count + missingFieldsParsed.Warnings.Count, "historical scan session aggregates warning counts");

    var emptyScanFolder = Path.Combine(workspace, "historical_empty_scan");
    Directory.CreateDirectory(emptyScanFolder);
    var emptyScan = productionScanner.Scan(new HistoricalReportScanOptions
    {
        SourceFolder = emptyScanFolder,
        IncludeSubfolders = true
    });
    Assert(emptyScan.SessionId != Guid.Empty, "historical empty-folder scan still creates a session");
    Assert(emptyScan.FilesDiscovered == 0 && emptyScan.ParsedCount == 0 && emptyScan.FailedCount == 0, "historical scanner handles an empty folder cleanly");
    Assert(emptyScan.Results.Count == 0, "historical empty-folder session contains zero results");

    var fakeScanFolder = Path.Combine(workspace, "historical_fake_scan");
    Directory.CreateDirectory(fakeScanFolder);
    var fakeDocx = Path.Combine(fakeScanFolder, "fake-report.docx");
    File.WriteAllText(fakeDocx, "fake content");
    var fakeParser = new TestHistoricalParser();
    var fakeScanner = new HistoricalReportScanner(fakeParser);
    var fakeSession = fakeScanner.Scan(new HistoricalReportScanOptions
    {
        SourceFolder = fakeScanFolder,
        IncludeSubfolders = false
    });
    Assert(fakeParser.CallCount == 1, "historical scanner uses the injected parser abstraction");
    Assert(fakeParser.LastParsedPath == fakeDocx, "historical scanner passes the discovered file path into the injected parser");
    Assert(fakeSession.ParserProfile == fakeParser.ProfileName, "historical scan session parser profile comes from the injected parser");
    Assert(fakeSession.Results.Count == 1 && fakeSession.Results[0].ParseSucceeded, "historical scanner produces results through the injected parser");

    var reviewSession = new HistoricalReviewSession(recursiveScan);
    Assert(reviewSession.TotalCount == recursiveScan.Results.Count, "historical review session wraps every scan result");
    Assert(reviewSession.UnreviewedCount >= 1, "historical review session keeps clean parses unreviewed until a user marks them ready");
    Assert(reviewSession.NeedsReviewCount >= 1, "historical review session sends warning or failed parses to needs-review");

    var reviewItem = reviewSession.Items.First(item => item.ParseSucceeded);
    Assert(reviewItem.OriginalRequest is not null && reviewItem.WorkingRequest is not null, "historical review item keeps both original and working requests");
    Assert(reviewItem.ReviewState == HistoricalReviewState.Unreviewed, "historical review item starts clean parses as unreviewed");
    var notifiedProperties = new List<string>();
    reviewItem.PropertyChanged += (_, args) =>
    {
        if (!string.IsNullOrWhiteSpace(args.PropertyName))
        {
            notifiedProperties.Add(args.PropertyName);
        }
    };
    var editableWorkingRequest = reviewItem.WorkingRequest ?? throw new InvalidOperationException("Review item should have a working request after a successful parse.");
    reviewItem.UpdateWorkingRequest(editableWorkingRequest with { Weather = "Manual override weather" });
    Assert(reviewItem.HasUserChanges, "historical review item tracks manual corrections");
    Assert(reviewItem.WorkingRequest!.Weather == "Manual override weather", "historical review item updates the editable working request");
    Assert(reviewItem.OriginalRequest!.Weather != reviewItem.WorkingRequest.Weather, "historical review item preserves the original parsed request");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.WorkingRequest)), "historical review item notifies WorkingRequest changes");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.HasUserChanges)), "historical review item notifies HasUserChanges changes");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.ChangedText)), "historical review item notifies ChangedText changes");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.DisplayReportNumber)) && notifiedProperties.Contains(nameof(HistoricalReviewItem.DisplayDate)), "historical review item notifies row display fields after edits");

    notifiedProperties.Clear();
    Assert(reviewItem.TryMarkReady(new HistoricalReviewValidator(), out var readyMessages), "historical review item can be marked ready after validation");
    Assert(readyMessages.Count == 0, "historical review ready validation succeeds without messages for a valid item");
    Assert(reviewItem.ReviewState == HistoricalReviewState.Ready, "historical review item transitions into ready state");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.ReviewState)) && notifiedProperties.Contains(nameof(HistoricalReviewItem.ReviewStateText)), "historical review item notifies ready-state transitions");

    notifiedProperties.Clear();
    reviewItem.UpdateWorkingRequest(reviewItem.WorkingRequest with { Observations = "Ready item edit pushes back to needs review." });
    Assert(reviewItem.ReviewState == HistoricalReviewState.NeedsReview, "historical review item demotes ready items back to needs review while editing");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.ReviewStateText)), "historical review item notifies ready-to-needs-review edit demotion");

    notifiedProperties.Clear();
    reviewItem.MarkExcluded();
    Assert(reviewItem.ReviewState == HistoricalReviewState.Excluded, "historical review item can be excluded without deleting source files");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.ReviewStateText)), "historical review item notifies exclusion state changes");

    notifiedProperties.Clear();
    reviewItem.ReturnToReview();
    Assert(reviewItem.ReviewState == reviewItem.InitialReviewState, "historical review item can be returned from excluded to the initial review state");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.ReviewStateText)), "historical review item notifies return-to-review state changes");

    notifiedProperties.Clear();
    reviewItem.ResetChanges();
    Assert(!reviewItem.HasUserChanges, "historical review reset clears manual correction tracking");
    Assert(reviewItem.WorkingRequest == reviewItem.OriginalRequest, "historical review reset restores the original working request");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.WorkingRequest)), "historical review reset notifies working-request restoration");
    Assert(notifiedProperties.Contains(nameof(HistoricalReviewItem.ChangedText)), "historical review reset notifies changed-indicator restoration");

    var invalidReviewResult = new HistoricalScanResult
    {
        SourceFilePath = "C:\\temp\\invalid-report.docx",
        SourceFileName = "invalid-report.docx",
        ParseResult = new HistoricalReportParseResult
        {
            Status = HistoricalReportParseStatus.ParsedWithWarnings,
            FilePath = "C:\\temp\\invalid-report.docx",
            FileName = "invalid-report.docx",
            ParserProfile = HistoricalReportImportService.DefaultParserProfile,
            OverallConfidence = HistoricalConfidenceLevel.Medium,
            FieldConfidence = new HistoricalFieldConfidence(),
            FieldExtractions = new HistoricalFieldExtractions(),
            Request = new HistoricalReportImportRequest
            {
                Number = 0,
                Date = default,
                SourceDocumentPath = "C:\\temp\\invalid-report.docx",
                ParserProfile = HistoricalReportImportService.DefaultParserProfile
            },
            Warnings = ["Needs manual correction."]
        }
    };
    var invalidReviewItem = new HistoricalReviewItem(invalidReviewResult);
    Assert(!invalidReviewItem.TryMarkReady(new HistoricalReviewValidator(), out var invalidReviewMessages), "historical review validator blocks ready state for invalid corrected data");
    Assert(invalidReviewMessages.Count >= 2, "historical review validator returns concrete validation messages");

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

static string FinalizeAndGetOutputPath(Project project, InspectionReport report)
{
    var preview = ReportGenerator.GenerateDraft(project, report);
    ReportGenerator.FinalizeReport(project, report, preview.OutputPath);
    return ProjectLayout.FinalReportPath(project, report);
}

static void CreateSimpleDocx(string path, params string[] paragraphs)
{
    using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
    var mainPart = document.AddMainDocumentPart();
    mainPart.Document = new Document(
        new Body(
            paragraphs.Select(text => new Paragraph(new Run(new Text(text))))));
    mainPart.Document.Save();
}

static void ReplaceDocumentText(string docxPath, string oldValue, string newValue)
{
    using var document = WordprocessingDocument.Open(docxPath, true);
    var texts = document.MainDocumentPart?.Document?.Descendants<Text>() ?? Enumerable.Empty<Text>();
    foreach (var text in texts)
    {
        if (!string.IsNullOrEmpty(text.Text) && text.Text.Contains(oldValue, StringComparison.Ordinal))
        {
            text.Text = text.Text.Replace(oldValue, newValue, StringComparison.Ordinal);
        }
    }

    document.MainDocumentPart?.Document?.Save();
}

static void AssertFinalFileName(Project project, int reportNumber, string expectedFileName, string[] mediaFiles)
{
    var report = MakeReport(project, reportNumber, 1, "Sunny", mediaFiles);
    var fileNameInfo = ProjectLayout.BuildFinalReportFileNameInfo(project, report);
    Assert(fileNameInfo.FileName == expectedFileName, $"final file name matches expected naming contract for report #{reportNumber}");
}

static IReadOnlyList<int> SearchReportNumbers(IEnumerable<InspectionReport> reports, ReportSearchCriteria criteria)
    => ReportSearchService.Filter(reports, criteria)
        .Select(report => report.Number)
        .ToList();

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

file sealed class FakeProjectLocationResolver : IProjectLocationResolver
{
    public int CallCount { get; private set; }

    public Func<string, Task<ProjectCoordinates?>> ResolveFunc { get; set; }
        = _ => Task.FromResult<ProjectCoordinates?>(null);

    public Task<ProjectCoordinates?> ResolveAsync(string locationText, CancellationToken cancellationToken)
    {
        CallCount++;
        return ResolveFunc(locationText);
    }
}

file sealed class FakeProjectTemperatureService : IProjectTemperatureService
{
    private readonly Dictionary<DateTime, TaskCompletionSource<TemperatureLookupResult>> _historicalPendingResults = new();

    public int CurrentCallCount { get; private set; }

    public int HistoricalCallCount { get; private set; }

    public Func<ProjectCoordinates, Task<TemperatureLookupResult>> CurrentResultFactory { get; set; }
        = _ => Task.FromResult(TemperatureLookupResult.Failure("not configured"));

    public Func<ProjectCoordinates, DateTime, int, int, Task<TemperatureLookupResult>> HistoricalResultFactory { get; set; }
        = (_, _, _, _) => Task.FromResult(TemperatureLookupResult.Failure("not configured"));

    public Task<TemperatureLookupResult> GetCurrentTemperatureAsync(ProjectCoordinates coordinates, CancellationToken cancellationToken)
    {
        CurrentCallCount++;
        return CurrentResultFactory(coordinates);
    }

    public Task<TemperatureLookupResult> GetHistoricalDaytimeAverageAsync(
        ProjectCoordinates coordinates,
        DateTime date,
        int startHour,
        int endHour,
        CancellationToken cancellationToken)
    {
        HistoricalCallCount++;
        if (_historicalPendingResults.TryGetValue(date.Date, out var pending))
        {
            return pending.Task;
        }

        return HistoricalResultFactory(coordinates, date.Date, startHour, endHour);
    }

    public void SetHistoricalPendingResult(DateTime date, TaskCompletionSource<TemperatureLookupResult> taskSource)
        => _historicalPendingResults[date.Date] = taskSource;

    public void ResolveHistorical(DateTime date, TemperatureLookupResult result)
    {
        if (_historicalPendingResults.TryGetValue(date.Date, out var pending))
        {
            pending.TrySetResult(result);
        }
    }
}

file sealed class TestHistoricalParser : IHistoricalReportParser
{
    public string ProfileName => "Test-Parser-v1";

    public int CallCount { get; private set; }

    public string LastParsedPath { get; private set; } = string.Empty;

    public HistoricalReportParseResult Parse(string documentPath)
    {
        CallCount++;
        LastParsedPath = documentPath;

        return new HistoricalReportParseResult
        {
            Status = HistoricalReportParseStatus.Parsed,
            FilePath = documentPath,
            FileName = Path.GetFileName(documentPath),
            ParserProfile = ProfileName,
            OverallConfidence = HistoricalConfidenceLevel.High,
            FieldConfidence = new HistoricalFieldConfidence
            {
                ReportNumber = HistoricalConfidenceLevel.High,
                Date = HistoricalConfidenceLevel.High
            },
            Request = new HistoricalReportImportRequest
            {
                Number = 1,
                Date = new DateTime(2026, 8, 8),
                SourceDocumentPath = documentPath,
                ParserProfile = ProfileName
            },
            ProjectName = "Injected Parser Test",
            ReportNumber = 1,
            Date = new DateTime(2026, 8, 8),
            Warnings = Array.Empty<string>()
        };
    }
}
