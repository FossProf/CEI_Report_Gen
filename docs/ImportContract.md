# Historical Import Contract

This document defines the Slice 6A destination contract for historical report search indexing.

## Purpose

The pilot importer exists to convert finalized legacy CEI `.docx` reports into structured SPINgen `report.json` files that load and search naturally inside an existing SPINgen project.

The goal in this slice is searchability, not full historical package reconstruction.

## Pilot Source

- finalized Microsoft Word `.docx` reports
- legacy CEI/SPIN report documents
- archive/source folders outside the SPINgen project

## Pilot Destination

For each successfully imported historical report, SPINgen writes:

```text
<ProjectRoot>/
`-- Reports/
    `-- 0216/
        |-- report.json
        `-- import-metadata.json
```

The exact report-number folder name uses the current Core contract:

- `ProjectLayout.FormatReportNumber(reportNumber)`

At the current baseline, report folder `216` becomes `0216`.

## Original DOCX Handling

- the original archived `.docx` remains in its source/archive location
- SPINgen does not copy that `.docx` into the project in Slice 6A
- SPINgen does not extract embedded photos in Slice 6A

## `report.json` Contract

Imported historical reports become normal `InspectionReport` records persisted through shared Core serialization.

Core writes:

- `number`
- `status`
- `date`
- `temperature`
- `weather`
- `locations`
- `inspectors`
- `personnelOnSite`
- `descriptionOfWork`
- `drawingsReviewed`
- `observations`
- `newDiscrepancies`
- `previousDiscrepancies`
- `photos`
- `outputFileName`
- `createdUtc`

Historical import rules:

- `status` is always `Final`
- `photos` is always `[]` in the pilot
- `outputFileName` is `""` because no local DOCX is stored
- `createdUtc` uses `SourceCreatedUtc` when provided, otherwise import time

Example:

```json
{
  "number": 216,
  "status": 1,
  "date": "2025-07-10T00:00:00",
  "temperature": "85",
  "weather": "Partly Cloudy",
  "locations": "Gridline 2",
  "inspectors": "Anthony Wintergerst",
  "personnelOnSite": "CMU Crew",
  "descriptionOfWork": "CMU lintel reinforcement review.",
  "drawingsReviewed": "S-201",
  "observations": "CMU lintel reinforcement and horizontal ladder reinforcing were reviewed.",
  "newDiscrepancies": "",
  "previousDiscrepancies": "",
  "photos": [],
  "outputFileName": "",
  "createdUtc": "2025-07-10T14:00:00Z"
}
```

Note:

- SPINgen currently serializes `ReportStatus.Final` as the numeric enum value `1`
- this is the existing shared serializer behavior and is intentionally reused

## `import-metadata.json` Contract

Importer provenance is stored separately from `report.json`.

Example:

```json
{
  "sourceFileName": "2025-07-10 CMF Structural Repairs SPIN Report #216.docx",
  "sourcePathAtImport": "D:\\Legacy Reports\\CMF\\2025-07-10 CMF Structural Repairs SPIN Report #216.docx",
  "sourceSha256": "abcdef1234567890",
  "importedUtc": "2026-08-09T20:00:00Z",
  "parserProfile": "CEI-SPIN-Legacy-v1",
  "contractVersion": "spingen-search-import-v1",
  "warnings": []
}
```

Metadata fields:

- `sourceFileName`
- `sourcePathAtImport`
- `sourceSha256`
- `importedUtc`
- `parserProfile`
- `contractVersion`
- `warnings`

SPINgen does not need to read this file for normal loading or search.

## Collision Rules

- importing a report number that already exists returns a controlled collision
- importing into an existing canonical report folder also returns a controlled conflict
- SPINgen does not overwrite `report.json`
- SPINgen does not merge historical imports with existing reports
- SPINgen does not auto-renumber historical reports

## Numbering Rules

- historical report numbers are authoritative identity values
- imported report `216` stays `216`
- future new reports must not collide with imported historical numbers
- Core next-report logic continues to derive the next safe number from occupied report folders

## Transactional Write Behavior

Historical import uses staged creation:

```text
Reports/
`-- .importing.0216.<guid>/
    |-- report.json
    `-- import-metadata.json
```

Only after both files are written successfully does SPINgen rename the staging folder into the canonical report folder.

Failure must not leave a partial canonical report directory.

## Search Behavior

After import, the historical report must load through normal SPINgen flows:

- `ReportStore.LoadAllReports(project)`
- `ReportSearchService`
- `ReportMatchSnippetBuilder`
- dashboard totals/final counts
- `New Report from Selected...`

No importer-specific search branch exists in the production app.

## Delete Behavior

Deleting an imported historical report removes:

- `report.json`
- `import-metadata.json`
- the canonical report folder

Deleting an imported historical report does not delete the original archived source DOCX.

## Open-Report Limitation

In Slice 6A, imported historical reports are indexed for data/search purposes. No local finalized DOCX is stored in the SPINgen project, so `outputFileName` remains empty.

The report remains usable for:

- search
- dashboard counts
- opening the report editor view
- `New Report from Selected...`
- folder-level diagnostics

## Contract Version

Current contract version:

`spingen-search-import-v1`
