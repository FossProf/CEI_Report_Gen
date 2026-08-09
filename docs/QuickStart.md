# Quick Start

This guide is for a first successful SPINgen run on a Windows machine.

## Prerequisites

- Windows 10 or Windows 11
- A CEI-approved report template in `.docx` format
- A Special Inspector signature image in `.png`, `.jpg`, or `.jpeg`
- A Project Manager signature image in `.png`, `.jpg`, or `.jpeg`

## First Run

1. Launch `SPINgen`.
2. Select `New Project...`.
3. Enter the project name, CEI project number, owner, contract manager, and general contractor.
4. Confirm the project folder location. By default, SPINgen uses `%USERPROFILE%\\Documents\\SPINgen\\Projects\\`.
5. Select the approved Word template for the project.
6. Select the Special Inspector signature image.
7. Select the Project Manager signature image.
8. Save the project.

SPINgen creates a self-contained project folder with:

- `project.json`
- `Template.docx`
- `Signatures\\`
- `Reports\\`

## Create Your First Report

1. Open the project if it is not already open.
2. Select `New Report`.
3. Enter the report number, inspection date, weather, location, inspector, personnel, description, drawings, and any remarks.
4. Optionally add photos and captions on the `Photos` tab.
5. Select `Generate Report`.
6. Review the preview output.
7. Accept the preview as final when ready.

## Expected Final Output

Finalized reports are saved inside the report folder using this naming contract:

`YYYY-MM-DD {project name} SPIN Report #{report number}.docx`

Example:

`2026-08-08 CMF Structural Repairs SPIN Report #216.docx`

## If Something Fails

- Open [Troubleshooting](Troubleshooting.md).
- Check the affected report folder for `generation-error.log`.
- Validate the project from the Project menu.
