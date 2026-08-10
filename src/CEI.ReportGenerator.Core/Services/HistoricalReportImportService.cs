using System.Security.Cryptography;
using CEI.ReportGenerator.Core.Models;

namespace CEI.ReportGenerator.Core.Services;

public static class HistoricalReportImportService
{
    public const string ContractVersion = "spingen-search-import-v1";

    public const string DefaultParserProfile = "CEI-SPIN-Legacy-v1";

    public static Func<string, Exception?>? StageFailureHookForTesting { get; set; }

    public static HistoricalReportImportResult Import(Project project, HistoricalReportImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(request);

        if (!ProjectLayout.IsValidProjectFolder(project.FolderPath))
        {
            return new HistoricalReportImportResult
            {
                Status = HistoricalReportImportStatus.InvalidProject,
                Message = "The destination must be an existing SPINgen project.",
                ReportFolder = string.Empty,
                ReportJsonPath = string.Empty,
                ImportMetadataPath = string.Empty
            };
        }

        if (request.Number <= 0)
        {
            throw new InvalidOperationException("Historical report number must be a positive whole number.");
        }

        if (request.Date == default)
        {
            throw new InvalidOperationException("Historical report date is required.");
        }

        var reportFolder = ProjectLayout.ReportFolder(project, request.Number);
        var reportJsonPath = ProjectLayout.ReportFilePath(project, request.Number);
        var importMetadataPath = ProjectLayout.ImportMetadataPath(project, request.Number);

        CleanupAbandonedImportFolders(project);

        if (string.IsNullOrWhiteSpace(request.SourceDocumentPath) || !File.Exists(request.SourceDocumentPath))
        {
            throw new InvalidOperationException("Historical source document path must point to an existing .docx file.");
        }

        if (!string.Equals(Path.GetExtension(request.SourceDocumentPath), ".docx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Historical source document must be a Word .docx file.");
        }

        if (Directory.Exists(reportFolder))
        {
            if (File.Exists(reportJsonPath)
                && JsonStore.TryLoad<InspectionReport>(reportJsonPath, out var existingReport, out _)
                && existingReport is not null)
            {
                return new HistoricalReportImportResult
                {
                    Status = HistoricalReportImportStatus.ReportAlreadyExists,
                    Message = $"Report #{request.Number} already exists in the destination project.",
                    ReportFolder = reportFolder,
                    ReportJsonPath = reportJsonPath,
                    ImportMetadataPath = importMetadataPath,
                    Report = existingReport
                };
            }

            return new HistoricalReportImportResult
            {
                Status = HistoricalReportImportStatus.FolderConflict,
                Message = $"Report #{request.Number} cannot be imported because the canonical report folder already exists.",
                ReportFolder = reportFolder,
                ReportJsonPath = reportJsonPath,
                ImportMetadataPath = importMetadataPath
            };
        }

        var importedUtc = DateTime.UtcNow;
        var report = BuildImportedReport(request, importedUtc);
        var metadata = BuildMetadata(request, importedUtc);
        var validationErrors = Validation.ValidateReport(report);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, validationErrors));
        }

        var reportsRoot = ProjectLayout.ReportsFolder(project);
        Directory.CreateDirectory(reportsRoot);

        var stagingFolder = Path.Combine(
            reportsRoot,
            $".importing.{ProjectLayout.FormatStorageReportNumber(request.Number)}.{Guid.NewGuid():N}");
        EnsureChildOfReportsRoot(reportsRoot, stagingFolder, "Import staging folder resolves outside the project reports folder.");

        try
        {
            Directory.CreateDirectory(stagingFolder);
            Directory.CreateDirectory(Path.Combine(stagingFolder, ProjectLayout.PhotosFolderName));
            JsonStore.Save(Path.Combine(stagingFolder, ProjectLayout.ReportJsonFileName), report);
            JsonStore.Save(Path.Combine(stagingFolder, ProjectLayout.ImportMetadataFileName), metadata);

            if (StageFailureHookForTesting?.Invoke(stagingFolder) is { } stageFailure)
            {
                throw stageFailure;
            }

            try
            {
                Directory.Move(stagingFolder, reportFolder);
            }
            catch (IOException) when (Directory.Exists(reportFolder))
            {
                TryDeleteDirectory(stagingFolder);
                return new HistoricalReportImportResult
                {
                    Status = HistoricalReportImportStatus.FolderConflict,
                    Message = $"Report #{request.Number} cannot be imported because the canonical report folder already exists.",
                    ReportFolder = reportFolder,
                    ReportJsonPath = reportJsonPath,
                    ImportMetadataPath = importMetadataPath
                };
            }

            try
            {
                ProjectStore.AdvanceReportNumber(project, request.Number);
            }
            catch
            {
                // The report folder itself is the authoritative collision guard.
                // Next-report persistence can self-heal from occupied report folders.
            }

            return new HistoricalReportImportResult
            {
                Status = HistoricalReportImportStatus.Imported,
                Message = $"Historical report #{request.Number} was imported for search/indexing.",
                ReportFolder = reportFolder,
                ReportJsonPath = reportJsonPath,
                ImportMetadataPath = importMetadataPath,
                Report = report
            };
        }
        catch
        {
            TryDeleteDirectory(stagingFolder);
            throw;
        }
    }

    private static InspectionReport BuildImportedReport(HistoricalReportImportRequest request, DateTime importedUtc)
        => new()
        {
            Number = request.Number,
            Status = ReportStatus.Final,
            Date = request.Date,
            Temperature = request.Temperature ?? string.Empty,
            Weather = request.Weather ?? string.Empty,
            Locations = request.Locations ?? string.Empty,
            Inspectors = request.Inspectors ?? string.Empty,
            PersonnelOnSite = request.PersonnelOnSite ?? string.Empty,
            DescriptionOfWork = request.DescriptionOfWork ?? string.Empty,
            DrawingsReviewed = request.DrawingsReviewed ?? string.Empty,
            Observations = request.Observations ?? string.Empty,
            NewDiscrepancies = request.NewDiscrepancies ?? string.Empty,
            PreviousDiscrepancies = request.PreviousDiscrepancies ?? string.Empty,
            Photos = new List<Photo>(),
            OutputFileName = string.Empty,
            CreatedUtc = importedUtc
        };

    private static HistoricalImportMetadata BuildMetadata(HistoricalReportImportRequest request, DateTime importedUtc)
        => new()
        {
            SourceFileName = Path.GetFileName(request.SourceDocumentPath),
            SourcePathAtImport = request.SourceDocumentPath,
            SourceSha256 = ComputeSha256(request.SourceDocumentPath),
            ImportedUtc = importedUtc,
            ParserProfile = string.IsNullOrWhiteSpace(request.ParserProfile) ? DefaultParserProfile : request.ParserProfile,
            ContractVersion = ContractVersion,
            Warnings = request.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)).ToList()
        };

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CleanupAbandonedImportFolders(Project project)
    {
        var reportsRoot = ProjectLayout.ReportsFolder(project);
        if (!Directory.Exists(reportsRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(reportsRoot))
        {
            var name = Path.GetFileName(directory);
            if (!name.StartsWith(".importing.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void EnsureChildOfReportsRoot(string reportsRoot, string path, string errorMessage)
    {
        var normalizedReportsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportsRoot));
        var normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedReportsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(errorMessage);
        }
    }
}
