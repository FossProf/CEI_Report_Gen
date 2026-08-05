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
3. **New Report** — enter the inspection date, weather, locations, inspectors, personnel,
   description of work, drawings reviewed, observations, discrepancies, and add photographs
   with captions.
4. **Generate Report** — the app validates the report, copies photos into the report
   folder, and fills the approved Word template using the Open XML SDK.
5. Review the generated document, then **Accept as Final**. The report is saved in
   `Reports\0001\0001_SpecialInspectionReport.docx` and the project's next report number
   is incremented.

Projects can be closed and reopened from the main window; all state is reloaded from disk.

## Project structure

```
CEI_Report_Gen/
├── templates/
│   └── CEI_Base_Template_Refined.docx     approved template (with placeholders)
├── src/
│   ├── CEI.ReportGenerator.Core/          models, persistence, template filling, validation
│   ├── CEI.ReportGenerator.App/           WPF desktop application
│   └── CEI.ReportGenerator.SmokeTests/    end-to-end console verification
```

A project folder created by the app looks like:

```
<Project Folder>/
├── project.json
├── Template.docx
├── Signatures/
│   ├── inspector_signature.png
│   └── pm_signature.png
└── Reports/
    └── 0001/
        ├── report.json
        ├── 0001_SpecialInspectionReport.docx
        └── photos/
```

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

## Smoke tests

```powershell
dotnet build src/CEI.ReportGenerator.SmokeTests
dotnet src/CEI.ReportGenerator.SmokeTests\bin\Debug\net8.0\CEI.ReportGenerator.SmokeTests.dll
```

Set `CEI_KEEP_WORKSPACE=1` to keep the temporary workspace the test uses for inspection.
