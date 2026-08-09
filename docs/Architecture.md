# Architecture

## Application Layers

`App`

- WPF windows and code-behind.
- Collects user input.
- Displays validation, preview, and load errors.
- Delegates business rules to Core.

`Core`

- Domain models such as `Project`, `InspectionReport`, and `Photo`.
- Business rules for validation, numbering, finalization, path safety, and lifecycle control.
- Coordinates report generation and persistence.

`Storage`

- JSON serialization and file layout.
- Project and report persistence through `ProjectStore`, `ReportStore`, and `JsonStore`.
- Safe handling of signatures, report folders, working previews, and photo files.

`Word Generator`

- Template preflight via `TemplateValidator`.
- OpenXML population via `TemplateFiller`.
- Image insertion, signature replacement, and generated document validation.

## Data Flow

Current generation flow:

```text
ProjectStore
      ->
InspectionReport JSON
      ->
ReportStore
      ->
Validation
      ->
ReportGenerator
      ->
TemplateFiller / Open XML
      ->
Preview
      ->
Final Report
```

`Project`

- Loaded from `project.json`.
- Provides template path, signature paths, numbering state, and report root.

`Report`

- Built in the editor and persisted as `report.json`.
- Holds field values, photos, status, and output file name.

`Validation`

- Project validation checks template, signatures, and folder safety.
- Report validation checks required fields, weather values, and image inputs.
- Template validation checks placeholders, signature controls, and photo-table structure.

`Template`

- OpenXML template is copied to a temporary working document.
- Placeholders and photo slots are populated.
- Signature drawings are replaced in-place.

`Preview`

- Successful generation writes `Reports/<number>/working/preview.docx`.
- Preview can be regenerated safely without consuming the report number.

`Final`

- Finalization stages a `.finalizing.docx`, persists `report.json` and `project.json`, then promotes the staged document to `YYYY-MM-DD {project name} SPIN Report #{report number}.docx`.
- On failure, preview and prior persisted state are preserved or restored.

## Directory Structure

```text
CEI_Report_Gen/
|-- CHANGELOG.md
|-- docs/
|-- templates/
|   `-- CEI_Base_Template_Refined.docx
|-- projects/
|   `-- .gitkeep
|-- Signatures/
|   `-- .gitkeep
|-- src/
|   |-- CEI.ReportGenerator.App/
|   |-- CEI.ReportGenerator.Core/
|   `-- CEI.ReportGenerator.SmokeTests/
`-- tools/
    `-- UpdateTemplateTags/
```

Project-local runtime data:

```text
<Project Folder>/
|-- project.json
|-- Template.docx
|-- Signatures/
`-- Reports/
    `-- 0001/
        |-- report.json
        |-- 2026-08-05 Demo Project SPIN Report #0001.docx
        |-- photos/
        `-- working/
            `-- preview.docx
```

## Configuration Files

- `project.json`
  Stores project identity, portable paths, and report numbering state.

- `report.json`
  Stores report field values, photos, status, and final output file name.

- `templates/CEI_Base_Template_Refined.docx`
  Approved report template contract for placeholder and signature processing.

- `.gitignore`
  Keeps runtime project data and generated artifacts out of source control.

- `.github/workflows/build.yml`
  Windows CI build and smoke-test workflow.

## UI Shell

```text
MainWindow
      ->
ProjectWindow
      |-- Project Dashboard
      |-- Project Readiness
      |-- Reports Grid
      |-- Menus
      `-- Status Bar
```

Supporting UI services:

- `ApplicationSettingsStore`
- `ProjectReadinessEvaluator`
- `ReportDraftFactory`

## Report Lifecycle

1. Create or open a project.
2. Start a report using the authoritative next report number from Core.
3. Edit report details and photos.
4. Save draft to `report.json` and copy/dedupe project-local photos.
5. Generate preview to `working/preview.docx`.
6. Review preview and regenerate if needed.
7. Accept as final.
8. Stage, persist, and promote final output.
9. Advance next report number only after successful finalization.

## Stable Foundation Components

The following systems are considered protected for the `0.2.0-alpha` SPINgen baseline and should only be changed for bug fixes or justified contract updates:

- `ProjectStore`
- `ReportStore`
- `JsonStore`
- `ProjectLayout`
- `Validation`
- `TemplateValidator`
- `TemplateFiller`
- `ReportGenerator`
- `SignatureStore`
- photo storage behavior
- photo normalization
- project numbering behavior
- preview/final lifecycle
- `ReportDraftFactory` creation semantics
- project/report JSON contracts
- template validation
- OpenXML generation
