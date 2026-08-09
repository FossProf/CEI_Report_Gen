# Historical Importer Roadmap

This roadmap describes the planned phased importer build-out after Slice 6A.

## Slice 6A

Search-index destination contract

- normalized import request model
- transactional Core import service
- canonical `report.json` persistence
- `import-metadata.json` provenance file
- search/load/delete/duplication compatibility tests

## Slice 6B

Folder scanner and legacy DOCX extraction

- source-folder scanning
- candidate-file discovery
- initial legacy Open XML extraction
- parser diagnostics

## Slice 6C

Review and correction UI

- field-by-field review
- correction before commit
- collision visibility
- import warnings display

## Slice 6D

Batch commit into a selected SPINgen project

- selected destination project session
- multi-report import commit
- progress reporting
- partial-failure review

## Slice 6E

Duplicate detection, logs, and hardening

- SHA-256 duplicate detection workflows
- richer audit logging
- retry/recovery paths
- importer-side reporting

## Slice 6F

Optional embedded photo and caption extraction

- photo extraction remains explicitly optional
- caption extraction remains future work
- pilot search/indexing value does not depend on photo recovery

## Guiding Rule

Importer-specific parsing stays outside the main SPINgen production app.

Dependency direction remains:

```text
Importer
  ->
CEI.ReportGenerator.Core
```
