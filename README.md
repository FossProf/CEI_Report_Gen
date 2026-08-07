# CEI Report Generator

A standalone Windows desktop application that generates Cornerstone Engineering & Inspection
(CEI) special inspection reports from an approved Word template.

The app takes a filled-in inspection report and a project's configuration (project info,
inspector/PM signatures, approved template) and produces a populated `.docx` report in the
project's `Reports` folder — without requiring Microsoft Word.

## Requirements

- Windows 10/11
- .NET 8 SDK (to build) or .NET 8 Desktop Runtime (to run)

## Build and run

```powershell
dotnet build CEI_Report_Gen.sln
dotnet run --project src/CEI.ReportGenerator.App
```

## How it works

1. **New Project** — enter the project name, Cornerstone project number, owner,
   contract manager, and general contractor; select a project folder, the approved CEI
   Word template, and the Special Inspector and Project Manager signature images.
2. A project folder is created with `project.json`, a copy of the template, the signature
   images, and a `Reports` folder. Everything is plain JSON + files — no database.
3. **New Report** — enter the inspection date, weather (chosen from the approved list in
   the dropdown), locations, inspectors, personnel, description of work, drawings
   reviewed, observations, and discrepancies, and add photographs with captions. Reports
   may have zero photos.
4. **Generate Report** — the app validates the project and report, copies photos into the
   report folder, and fills the approved Word template using the Open XML SDK. Generation
   fails cleanly (with a dialog listing each problem) instead of producing a broken or
   partially-filled document. The report number is only consumed on a successful draft.
5. Review the generated document, then **Accept as Final**. The report is saved in
   `Reports\0001\0001_SpecialInspectionReport.docx` and the project's next report number
   is incremented.

Projects can be closed and reopened from the main window; all state is reloaded from disk.

Signatures are managed per-project in the project's `Signatures` folder. When creating or
editing a project you can import signature images (PNG/JPG), refresh the list, and open the
signatures folder from the UI; the Inspector and Project Manager signature targets are
picked with dropdowns.

## Project structure

```
CEI_Report_Gen/
├── templates/
│   └── CEI_Base_Template_Refined.docx     approved template (placeholders + signature controls)
├── Signatures/                            shared signature source library
│   ├── Anthony Wintergerst.png
│   └── Georgiy Orlov.jpg
├── projects/                              project store
│   └── CMF/                               sample project (openable in the app)
├── tools/
│   └── UpdateTemplateTags/                adds signature content controls to the template
├── src/
│   ├── CEI.ReportGenerator.Core/          models, persistence, template filling, validation
│   ├── CEI.ReportGenerator.App/           WPF desktop application
│   └── CEI.ReportGenerator.SmokeTests/    end-to-end console verification
```

`Signatures/` is the shared source library you import from when setting up a project.
`projects/` holds app-created project folders. `projects/CMF/` is a sample project with
sample configuration values; open its folder from the app's main window to use it as a
starting point.

A project folder created by the app looks like:

```
<Project Folder>/
├── project.json
├── Template.docx
├── Signatures/
│   ├── Anthony Wintergerst.png
│   └── Georgiy Orlov.jpg
└── Reports/
    └── 0001/
        ├── report.json
        ├── 0001_SpecialInspectionReport.docx
        └── photos/
```

`project.json` stores `folderPath` and `templatePath` relative to the project folder when
possible, so a project folder can be moved or checked into a repository and reopened
anywhere. Absolute paths (chosen by the user at creation time) are kept as-is.

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
| `ValidateProject` | Report number free, photos exist and are supported, both signature files present |
| `ValidateReport` | Required fields filled, weather is on the approved list, photo captions present |
| `CopyPhotos` | Photos copied into the report folder |
| `ValidateTemplate` | Template preflight: all placeholders, signature controls, and the photo table present |
| `CopyTemplate` | No overwrite of an existing report |
| `FillTemplate` | Temp-file population of the document, then atomic move into place |

On failure the app shows a dialog (with copy/open-log actions) and writes
`generation-error.log` in the report folder. Failed drafts never leave a partial `.docx` and
never consume the report number.

## Smoke tests

```powershell
dotnet build src/CEI.ReportGenerator.SmokeTests
dotnet src/CEI.ReportGenerator.SmokeTests\bin\Debug\net8.0\CEI.ReportGenerator.SmokeTests.dll
```

Set `CEI_KEEP_WORKSPACE=1` to keep the temporary workspace the test uses for inspection.
