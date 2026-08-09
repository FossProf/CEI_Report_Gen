# Project Structure

This guide describes the repository layout and the on-disk structure of runtime SPINgen projects.

## Repository Structure

```text
CEI_Report_Gen/
|-- CHANGELOG.md
|-- CEI_Report_Gen.sln
|-- README.md
|-- assets/
|-- docs/
|-- projects/
|   `-- .gitkeep
|-- scripts/
|-- src/
|   |-- CEI.ReportGenerator.App/
|   |-- CEI.ReportGenerator.Core/
|   `-- CEI.ReportGenerator.SmokeTests/
|-- templates/
|   `-- CEI_Base_Template_Refined.docx
`-- tools/
    `-- UpdateTemplateTags/
```

## Project Data Root

By default, SPINgen stores user projects under:

`%USERPROFILE%\\Documents\\SPINgen\\Projects\\`

The repository `projects\\` folder exists for local development convenience and is ignored by Git as user data.

## Single Project Layout

```text
<Project Folder>/
|-- project.json
|-- Template.docx
|-- Signatures/
|   |-- Inspector.png
|   `-- Manager.png
`-- Reports/
    |-- 0001/
    |   |-- report.json
    |   |-- working/
    |   |   `-- preview.docx
    |   |-- photos/
    |   `-- 2026-08-08 Demo Project SPIN Report #1.docx
    `-- 0002/
```

## Folder Responsibilities

- `project.json`
  Stores project-level metadata and numbering state.

- `Template.docx`
  Stores the project-local working copy of the approved template.

- `Signatures\\`
  Stores project-local inspector and project manager signature assets.

- `Reports\\0001\\report.json`
  Stores the persisted draft/final report fields.

- `Reports\\0001\\working\\preview.docx`
  Stores the current preview document.

- `Reports\\0001\\photos\\`
  Stores project-local copies of report photos.

- `Reports\\0001\\{final docx}`
  Stores the finalized report document.

## Data Ownership Rule

SPINgen runtime data lives in project folders, not in the installation directory and not in source-controlled repository content.

That separation protects:

- uninstall behavior
- project portability
- Git hygiene
- field data retention
