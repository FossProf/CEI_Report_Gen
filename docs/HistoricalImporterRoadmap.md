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
- `HistoricalScanSession` in-memory handoff model
- `IHistoricalReportParser` as the parser extension seam

## Current 6B Handoff

```text
HistoricalReportScanner
  ->
HistoricalScanSession
  ->
HistoricalScanResult[]
  ->
Future Review / Correction UI
```

Current importer sessions are intentionally in-memory only. They are not yet
persisted to disk and do not commit reports into a SPINgen project.

## Slice 6C

Review and correction UI

- two-pane review workspace
- field-level confidence and provenance display
- editable working copy before any import commit exists
- reversible `Ready` / `Excluded` review states
- in-memory-only review session with discard prompt on rescan
- source document launch from the review pane

## Current 6C Handoff

```text
HistoricalReportScanner
  ->
HistoricalScanSession
  ->
HistoricalReviewSession
  ->
HistoricalReviewItem[]
  ->
Future Import Commit Workflow
```

Slice 6C remains intentionally non-destructive:

- no destination project is selected yet
- no `report.json` files are written
- no importer commit service is called
- parse failures remain visible in the review list and can be excluded, but they do not yet support full manual reconstruction

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
