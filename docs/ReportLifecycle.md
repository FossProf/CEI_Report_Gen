# Report Lifecycle

This guide explains what happens from report creation through finalization and deletion.

## 1. Report Creation

When a user creates a new report, SPINgen:

- determines the authoritative next report number
- creates a draft report object
- opens the report editor

The report number shown to the user is the working number for that draft. It is not fully consumed until finalization succeeds.

## 2. Draft Editing

While a report is in `Draft` status, the user can:

- edit report details
- add, remove, and reorder photos
- save the draft
- generate preview output multiple times

Draft persistence writes `report.json` and stores any imported photos inside the report folder.

## 3. Preview Generation

Preview generation runs a staged pipeline:

1. validate project data
2. validate report data
3. validate template contract
4. create a temporary working document
5. fill placeholders, signatures, and photos
6. promote the generated document to `working\\preview.docx`

Preview generation does not consume the report number.

## 4. Finalization

When the preview is accepted as final, SPINgen:

- stages the final `.docx`
- persists final report metadata
- persists synchronized project numbering
- promotes the staged document into the report folder
- removes preview artifacts on success

Finalization is designed to avoid partial success.

## 5. Final Naming Contract

Finalized reports use:

`YYYY-MM-DD {project name} SPIN Report #{report number}.docx`

Rules:

- the date comes from the inspection date
- the visible report number uses natural numeric formatting
- leading zero padding is not used in the final filename
- the project name is sanitized for Windows filenames
- if the project name already ends with `SPIN`, SPINgen does not duplicate `SPIN`

## 6. Reopen and Search

Saved reports remain available to:

- reopen in the report editor
- duplicate into a new report from the selected report
- search within the current project

Search operates from persisted JSON content, not by parsing generated Word documents.

## 7. Safe Deletion

Deleting a report removes the entire report folder only after SPINgen confirms the operation can be completed safely.

Deletion is intentionally conservative:

- confirmation is required
- in-use files block deletion
- partial deletion is avoided
- preview, temporary, photo, JSON, and final-output artifacts are removed together
