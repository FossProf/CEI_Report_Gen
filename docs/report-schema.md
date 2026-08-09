# Report Schema

`report.json` stores a persisted inspection report.

## Properties

### `number`

- Type: `integer`
- Required: yes
- Meaning: report number used for folder naming, template insertion, preview, and final output.

### `status`

- Type: `string enum`
- Required: yes
- Values:
  - `Draft`
  - `Final`

### `date`

- Type: `datetime`
- Required: yes
- Meaning: inspection date inserted into the report template.

### `temperature`

- Type: `string`
- Required: no
- Meaning: temperature text; empty values render as `N/A`.

### `weather`

- Type: `string`
- Required: yes
- Meaning: must match one approved weather option.

### `locations`

- Type: `string`
- Required: yes

### `inspectors`

- Type: `string`
- Required: yes

### `personnelOnSite`

- Type: `string`
- Required: yes

### `descriptionOfWork`

- Type: `string`
- Required: yes

### `drawingsReviewed`

- Type: `string`
- Required: yes

### `observations`

- Type: `string`
- Required: no
- Meaning: empty values render as `N/A`.

### `newDiscrepancies`

- Type: `string`
- Required: no
- Meaning: empty values render as `N/A`.

### `previousDiscrepancies`

- Type: `string`
- Required: no
- Meaning: empty values render as `N/A`.

### `photos`

- Type: `array<photo>`
- Required: yes
- Meaning: ordered photo list used for preview/final generation.

### `outputFileName`

- Type: `string`
- Required: no for drafts, yes for finals
- Meaning: final DOCX file name after successful finalization.

### `createdUtc`

- Type: `datetime`
- Required: yes
- Meaning: report identity timestamp used to distinguish ownership collisions.

## Photo Object

Each photo object contains:

### `sourcePath`

- Type: `string`
- Meaning: original source path if still available.

### `storedFileName`

- Type: `string`
- Meaning: project-local deduped file name under the report `photos/` folder.
- Rule: filename only, never a rooted or nested path.

### `caption`

- Type: `string`
- Meaning: caption text inserted under the image.

## Status Values

- `Draft`
  Report may be edited, saved, and previewed.

- `Final`
  Report has a final DOCX and is treated as immutable for finalization purposes in the `0.2.0-alpha` baseline.

## Generation Lifecycle

1. Report is created in memory with `Draft` status.
2. Draft may be saved to `report.json`.
3. Preview generation writes `working/preview.docx`.
4. Finalization stages and promotes the final DOCX.
5. `status` becomes `Final`.
6. `outputFileName` is set to `YYYY-MM-DD {project name} SPIN Report #{report number}.docx`, without leading zeros in the report number and without duplicating `SPIN` when the project name already ends with it.
7. Preview artifacts are removed on success.
