# Project Schema

`project.json` stores the persisted project configuration.

## Properties

### `name`

- Type: `string`
- Required: yes
- Meaning: project display name shown in the UI and inserted into the report template.

### `number`

- Type: `string`
- Required: yes
- Meaning: Cornerstone project number.

### `owner`

- Type: `string`
- Required: yes
- Meaning: owner name inserted into reports.

### `contractManager`

- Type: `string`
- Required: yes
- Meaning: contract manager inserted into reports.

### `generalContractor`

- Type: `string`
- Required: yes
- Meaning: general contractor inserted into reports.

### `folderPath`

- Type: `string`
- Required: yes
- Typical value: `.`
- Meaning: portable root location of the project relative to `project.json`.

### `templatePath`

- Type: `string`
- Required: yes
- Typical value: `Template.docx`
- Meaning: path to the project-local report template.

### `inspectorSignaturePath`

- Type: `string`
- Required: yes
- Typical value: `Signatures/Inspector.png`
- Meaning: project-local relative path to the special inspector signature image.

### `projectManagerSignaturePath`

- Type: `string`
- Required: yes
- Typical value: `Signatures/Manager.png`
- Meaning: project-local relative path to the project manager signature image.

### `nextReportNumber`

- Type: `integer`
- Required: yes
- Default: `1`
- Meaning: stored report-number candidate. Core may synchronize this value against occupied report artifacts before opening a new report.

### `createdUtc`

- Type: `datetime`
- Required: yes
- Meaning: creation timestamp for the project record.

## Required Fields

Required fields for a valid project:

- `name`
- `number`
- `owner`
- `contractManager`
- `generalContractor`
- `folderPath`
- `templatePath`
- `inspectorSignaturePath`
- `projectManagerSignaturePath`
- `nextReportNumber`

## Optional Fields

There are no optional persisted fields in the current schema. Empty strings still fail project validation for required identity, template, and signature values.

## Path Rules

- Portable project-local paths are preferred.
- `folderPath` should normally be `.`.
- `templatePath` should normally be `Template.docx`.
- Signature paths should stay under `Signatures/`.
- Paths resolving outside the project root are rejected.
- Unsupported signature extensions are rejected.

## Report Numbering

- `nextReportNumber` is a persisted candidate, not a reserved value.
- Core determines the authoritative next report number from both `nextReportNumber` and occupied report artifacts.
- A report number is only consumed on successful finalization.
- Manual report numbers may advance `nextReportNumber` upward after finalization.

## Signatures

- Signatures are stored per-project under `Signatures/`.
- Supported formats: `.png`, `.jpg`, `.jpeg`.
- Duplicate imports do not silently overwrite existing files.
- Project validation requires both signatures to resolve to valid project-local files.

## Template Mapping

Project fields map directly to the template contract:

- `name` -> `{project.name}`
- `number` -> `{project.num}`
- `owner` -> `{project.owner}`
- `contractManager` -> `{project.contract}`
- `generalContractor` -> `{project.general}`

## Final Report Naming

- Finalized reports are saved as `YYYY-MM-DD {project name} SPIN Report #{report number}.docx`.
- The date comes from the report inspection date.
- The report number uses its natural whole-number form with no leading zeros.
- The project name portion is sanitized to remain valid on Windows filesystems.
- If the project name already ends with `SPIN`, the file name does not add a second `SPIN` before `Report`.
