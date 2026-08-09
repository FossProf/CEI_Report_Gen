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
