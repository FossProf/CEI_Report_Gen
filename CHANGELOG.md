# Changelog

## 0.2.0-alpha - SPINgen UI Baseline

### Project Management

- project creation and opening
- editable Project Settings after creation
- project-local template and signature storage
- recent project handling
- application preferences and startup reopen behavior

### Reporting

- draft reports and report persistence
- validation and approved weather enforcement
- photo import, storage, and deduplication
- Word generation through Open XML template filling
- preview/final workflow with rollback protection
- report numbering synchronization and manual override handling
- final-report overwrite protection
- finalized naming contract: `YYYY-MM-DD {project name} SPIN Report #{report number}`

### Productivity

- New Report from Selected via `ReportDraftFactory`
- dashboard report counts
- readiness status and validation summary

### User Interface

- conventional Windows menus
- MainWindow -> ProjectWindow shell flow
- application settings window
- project dashboard and reports grid
- status bar and readiness indicators
- CEI visual identity
- Cornerstone branding
- SPINgen application naming

### Deployment

- self-contained Windows publish for `win-x64`
- MSI installer build support
- release runner script for launching the current commit

### Manual Verification Status

- automated baseline passed on August 9, 2026
- manual UI and installer validation remains partially documented
- GUI launch verification from this environment may be limited by sandbox/desktop restrictions

## Version 0.1 Foundation

- Established the first stable end-to-end baseline for project creation, persistence, report editing, validation, preview generation, and finalization.
- Hardened report numbering, preview/final workflow, and failure recovery.
- Stabilized signature handling, photo storage, template validation, and OpenXML generation.
- Added regression coverage for numbering, collisions, rollback behavior, malformed JSON handling, path safety, and repository hygiene.
- Added baseline architecture, schema, and template-contract documentation.
