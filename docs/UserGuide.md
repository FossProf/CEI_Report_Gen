# User Guide

SPINgen is a Windows desktop application for creating, previewing, finalizing, and searching CEI special inspection reports without requiring Microsoft Word to generate the document.

## Main Window

The main window lets you:

- create a new project
- open an existing project
- reopen recent projects
- remove stale recent-project shortcuts
- open application settings

## Application Settings

Application Settings currently controls:

- default projects folder
- number of recent projects shown
- whether the last project reopens on startup

These settings affect the shell experience only. They do not modify project business data.

## Project Window

The project window is the main workspace for an active project. It includes:

- project identity and counts
- a compact readiness indicator
- a `Reports` workspace
- a `Search` workspace
- a shared report grid
- project, report, tools, and help menus

### Reports Workspace

Use `Reports` for day-to-day report work:

- create a blank report
- open a selected report
- open a selected report folder
- create a new report from a selected report
- delete a selected report

### Search Workspace

Use `Search` to find reports within the current project by:

- keyword text
- report status
- weather
- date range

Search results share the same report grid and keep match-context snippets visible.

## Report Editor

The report editor has two tabs:

- `Report Details`
- `Photos`

The editor supports:

- saving draft reports
- editing required inspection fields
- reordering photos
- captioning photos
- generating preview output

## Preview and Finalization

Preview generation writes a working document under the report folder. Finalization promotes that work into the final `.docx` and updates project/report metadata only after success.

Use preview when:

- you need to inspect the generated document
- you want to regenerate after edits
- you want to confirm photos and signatures before committing a report number

Use finalization when:

- the preview is approved
- the report should become part of the permanent project record

## Safe Report Deletion

Deleting a report requires confirmation. The operation removes:

- `report.json`
- the finalized `.docx`
- photos stored for that report
- preview and temporary generation artifacts

If Windows reports that a file is in use, SPINgen stops the delete rather than partially removing the report.
