# SPINgen

A standalone Windows desktop application that generates Cornerstone Engineering & Inspection
(CEI) special inspection reports from an approved Word template.

The app takes a filled-in inspection report and a project's configuration (project info,
inspector/PM signatures, approved template) and produces a populated `.docx` report in the
project's `Reports` folder without requiring Microsoft Word.

Project folders, reports, signatures, photos, previews, logs, and generated documents are
runtime/user data and must not be committed to the repository.

## Documentation

- [Documentation Index](docs/README.md)
- [Quick Start](docs/QuickStart.md)
- [User Guide](docs/UserGuide.md)
- [Project Administration](docs/ProjectAdministration.md)
- [Template Guide](docs/TemplateGuide.md)
- [Project Structure](docs/ProjectStructure.md)
- [Report Lifecycle](docs/ReportLifecycle.md)
- [Search Guide](docs/SearchGuide.md)
- [Developer Architecture](docs/DeveloperArchitecture.md)
- [Template Contract](docs/TemplateContract.md)
- [Project Schema](docs/project-schema.md)
- [Report Schema](docs/report-schema.md)
- [Development Guidelines](docs/DevelopmentGuidelines.md)
- [Architecture Baseline](docs/Architecture.md)
- [Report Search Contract](docs/report-search.md)
- [Deployment](docs/Deployment.md)
- [Release Guide](docs/ReleaseGuide.md)
- [Troubleshooting](docs/Troubleshooting.md)
- [Documentation Changelog](docs/Changelog.md)
- [Repository Changelog](CHANGELOG.md)

## Requirements

- Windows 10/11
- .NET 8 SDK (to build) or .NET 8 Desktop Runtime (to run)

## Build and run

```powershell
dotnet build CEI_Report_Gen.sln
dotnet run --project src/CEI.ReportGenerator.App
```

## Building a Release

```powershell
.\scripts\build-release.ps1
```

Publish output: `artifacts/publish/win-x64/`

Installer output: `artifacts/installer/SPINgen_0.3.0-alpha_x64.msi`

Release output: `artifacts/release/`

`build-release.ps1` is the authoritative local release command. It:

- cleans stale artifact directories
- restores, builds, and optionally smoke-tests the solution
- publishes the current application to `artifacts/publish/win-x64/`
- verifies the published executable version against the source version
- generates a publish `release-manifest.json`
- builds the MSI only from verified publish output
- creates a portable ZIP from the verified publish directory
- writes SHA-256 hashes and a final release manifest

Useful options:

- `.\scripts\build-release.ps1 -SkipInstaller`
- `.\scripts\build-release.ps1 -SkipTests`
- `.\scripts\build-release.ps1 -KeepArtifacts`

Deployment guidance: [Deployment](docs/Deployment.md)

## How it works

1. New Project: enter the project name, Cornerstone project number, owner, contract
   manager, and general contractor; select a project folder, the approved CEI Word
   template, and the Special Inspector and Project Manager signature images.
2. A project folder is created with `project.json`, a copy of the template, the signature
   images, and a `Reports` folder. Everything is plain JSON plus files, with no database.
3. New Report: enter the inspection date, weather, locations, inspectors, personnel,
   description of work, drawings reviewed, observations, discrepancies, and photo captions.
4. Generate Report: the app validates the project and report, stores report photos safely,
   fills the approved Word template using Open XML, and writes a preview to
   `Reports\0001\working\preview.docx`.
5. Accept as Final: after review, the preview is promoted to
   `Reports\0001\2026-08-05 Demo Project SPIN Report #1.docx`, the report is marked Final, and the
   project's next report number advances to at least the finalized report number plus one.

Projects can be closed and reopened from the main window; all state is reloaded from disk.

Signatures are managed per-project in the project's `Signatures` folder. When creating or
editing a project you can import signature images (PNG/JPG), refresh the list, and open the
signatures folder from the UI; the Inspector and Project Manager signature targets are
picked with dropdowns.

## Project structure

```text
CEI_Report_Gen/
|-- templates/
|   `-- CEI_Base_Template_Refined.docx
|-- projects/
|-- tools/
|   `-- UpdateTemplateTags/
`-- src/
    |-- CEI.ReportGenerator.Core/
    |-- CEI.ReportGenerator.App/
    `-- CEI.ReportGenerator.SmokeTests/
```

`projects/` holds app-created project folders and should stay local-only.

A project folder created by the app looks like:

```text
<Project Folder>/
|-- project.json
|-- Template.docx
|-- Signatures/
`-- Reports/
    `-- 0001/
        |-- working/
        |   `-- preview.docx
        |-- report.json
        |-- 2026-08-05 Demo Project SPIN Report #1.docx
        `-- photos/
```

`project.json` stores `folderPath` as `.` and stores project-local assets relative to the
project folder when possible, so a complete project directory can be copied and reopened on
another machine.

## Template placeholders

The approved template (`templates/CEI_Base_Template_Refined.docx`) may use these placeholders:

| Placeholder | Content |
| --- | --- |
| `{project.name}` | Project name |
| `{project.num}` | Cornerstone project number |
| `{project.owner}` | Owner |
| `{project.contract}` | Contract manager |
| `{project.general}` | General contractor |
| `{project.report.num}` | Report number |
| `{project.report.date}` | Inspection date |
| `{project.report.temp}` | Temperature |
| `{project.report.weather}` | Weather |
| `{project.report.location}` | Locations inspected |
| `{project.report.inspector}` | Cornerstone Inspector(s) |
| `{project.report.personnel}` | Personnel on site |
| `{project.report.description}` | Description of work inspected |
| `{project.report.drawing}` | Drawing sheets and sections |
| `{project.report.observations}` | General observations |
| `{project.report.new_discrepancies}` | Discrepancies and direction given |
| `{project.report.old_discrepancies}` | Previous discrepancy corrections |
| `{project.report.photos[1].image}` / `{project.report.photos[1].caption}` | Photo 1 image / caption |

More photo slots are cloned automatically from the template's photo table when a report has
more photos than the template provides. Empty optional fields render as `N/A`.

### Signature content controls

The two reviewer signatures live in Word content controls tagged
`inspection.signature.inspector` and `inspection.signature.projectManager`. The app replaces
those controls' embedded images with the project's chosen signature files. The template must
contain both tags; generation fails at preflight with a clear message if they are missing.

### Generation failure behavior

Generation runs through a staged pipeline so failures are reported early and nothing partial
is written:

| Stage | What is checked |
| --- | --- |
| `ValidateProject` | Photos and signature files are present and safe |
| `ValidateReport` | Required fields are filled and photo inputs are supported |
| `ValidateTemplate` | Placeholders, signature controls, and the photo table exist |
| `CopyTemplate` | A temporary document is created without touching the final report |
| `FillTemplate` | Temp-file population, validation, and promotion into preview/final paths |

On failure the app shows a dialog (with copy/open-log actions) and writes
`generation-error.log` in the report folder. Failed drafts never leave a partial `.docx`,
never overwrite the final report, and never consume the report number.

## Smoke tests

```powershell
dotnet build src/CEI.ReportGenerator.SmokeTests
dotnet src\CEI.ReportGenerator.SmokeTests\bin\Debug\net8.0\CEI.ReportGenerator.SmokeTests.dll
```

Set `CEI_KEEP_WORKSPACE=1` to keep the temporary workspace the test uses for inspection.

## Release Troubleshooting

- If you see `SPINgen is currently running`, close the application and retry.
- If artifact cleanup fails, close Explorer windows or processes holding files under `artifacts\`.
- If WiX packaging is unavailable in the environment, run `.\scripts\build-release.ps1 -SkipInstaller` to produce a verified publish and portable ZIP without MSI packaging.
- Windows SmartScreen may warn because current alpha installers are unsigned.
