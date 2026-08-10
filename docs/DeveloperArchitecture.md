# Developer Architecture

This guide describes the current SPINgen architecture for developers extending the application without destabilizing the report-generation baseline.

## Architectural Layers

```text
App
  ->
Core
  ->
Storage and Generation Services
```

### App

The WPF application layer is responsible for:

- windows, menus, dialogs, and visual state
- collecting user input
- surfacing validation and generation failures
- switching between Reports and Search workspaces

The App layer should not directly manipulate JSON files or DOCX internals.

The App layer now also owns:

- temperature-assistance session state
- async lookup cancellation and stale-result suppression
- project-location resolution workflow presentation

### Core

The Core layer is responsible for:

- domain models
- report numbering rules
- project and report validation
- path and filename safety
- lifecycle orchestration
- search filtering behavior

### Storage and Generation Services

This layer is responsible for:

- JSON persistence
- project and report folder ownership
- signature and photo storage
- template validation
- Open XML document generation
- provider-backed geocoding and temperature lookup abstractions

## Temperature Assistance Architecture

Temperature assistance is intentionally additive and isolated from the report schema.

- `ApplicationSettings.TemperatureAssistance` controls the master feature switch, new-report Auto default, and historical daytime averaging hours.
- `Project` stores optional `LocationText`, cached coordinates, and timezone data.
- `IProjectLocationResolver` abstracts geocoding.
- `IProjectTemperatureService` abstracts current and historical temperature lookup.
- `ProjectLocationResolutionWorkflow` decides whether saved coordinates can be reused or fresh geocoding is required.
- `TemperatureAssistanceSession` owns report-editor session behavior such as:
  - new-report Auto defaults
  - final-report Auto default off
  - manual override turning Auto off
  - cancellation and latest-request-wins behavior

Provider-specific HTTP, JSON DTOs, URLs, and caching remain outside WPF code-behind.

## Key Protected Components

These components form the current protected baseline and should only change for bug fixes or deliberate contract work:

- `ProjectStore`
- `ReportStore`
- `JsonStore`
- `ProjectLayout`
- `TemplateValidator`
- `TemplateFiller`
- `ReportGenerator`
- `SignatureStore`
- `ReportSearchService`
- `ReportDraftFactory`

## Core Flows

### Project Flow

1. App collects project setup fields.
2. `ProjectStore` initializes the project folder.
3. `JsonStore` persists `project.json`.
4. App opens the project workspace.

### Report Flow

1. App requests a new or duplicated draft.
2. Core determines the working report number.
3. `ReportStore` persists draft JSON and photo assets.
4. `ReportGenerator` coordinates preview and final output.

### Search Flow

1. `ReportStore` loads reports from project folders.
2. `ReportSearchService` filters reports using search criteria.
3. `ReportMatchSnippetBuilder` creates display context.
4. App binds results to the shared report grid.

### Historical Importer Scan Flow

1. `HistoricalReportScanner` discovers `.docx` files in the selected source folder.
2. The scanner delegates each file to `IHistoricalReportParser`.
3. Parser output is wrapped into `HistoricalScanResult` entries.
4. The full scan is returned as one `HistoricalScanSession`.
5. `HistoricalReviewSession` wraps that scan into editable `HistoricalReviewItem` entries.
6. The review UI binds a working copy per report and never mutates the original parse request.
7. Future importer slices will consume only review-approved items during commit workflows.

### Release Flow

1. `scripts\\build-release.ps1` cleans artifacts.
2. `dotnet restore`, build, and tests run.
3. Publish output is generated and version-verified.
4. MSI and portable ZIP are built from verified publish output.

## Ownership Boundaries

Keep these boundaries intact:

- UI state in App
- business rules in Core
- file persistence in store/services
- DOCX manipulation in template/generation services

Crossing those boundaries tends to create fragile behavior, especially around report numbering, finalization rollback, and template compatibility.

## Importer Extension Point

The historical importer currently supports one deterministic Open XML parser.

Dependency direction remains:

```text
HistoricalReportScanner
  ->
IHistoricalReportParser
  ->
HistoricalDocumentParser
```

`HistoricalScanSession` is now the canonical unit of work for importer progress between slices. The current WPF scanner window keeps only the current session in memory and does not persist session history yet.

## Importer Review Boundary

Slice 6C adds a deliberate boundary between parsing and import commit:

- `HistoricalDocumentParser` produces deterministic field extractions, confidence, source provenance, and conflict candidates.
- `HistoricalReviewItem` preserves both `OriginalRequest` and editable `WorkingRequest`.
- `HistoricalReviewValidator` is the gate for moving an item into `Ready`.
- `HistoricalReviewSession` provides review-state counts for the UI summary and filters.

This boundary is intentionally review-only. It does not call `HistoricalReportImportService.Import`, does not select a destination project, and does not persist importer session state yet.
