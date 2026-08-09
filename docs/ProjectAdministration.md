# Project Administration

This guide covers how projects are created, stored, maintained, and reopened.

## Creating a Project

When you create a project, SPINgen stores the project as a dedicated folder under the selected projects root. The project name is sanitized so the folder name is valid on Windows.

The new project folder contains:

- `project.json`
- `Template.docx`
- `Signatures\\`
- `Reports\\`

## Editing Project Settings

Project settings can be updated after creation. Typical reasons include:

- correcting project identity fields
- replacing the approved template
- replacing signature images
- changing display metadata used in generated reports

Editing settings updates project data only. Existing finalized reports are not renamed retroactively.

## Signatures

Each project keeps signatures inside its own `Signatures\\` folder.

Supported formats:

- `.png`
- `.jpg`
- `.jpeg`

Guidance:

- use clear signature images with transparent or clean backgrounds when possible
- keep the files project-local rather than referencing a network path
- replace outdated signatures through project settings so future reports use the new image

## Project Validation

Project validation checks:

- required project fields
- approved template presence
- signature presence and file type
- path safety
- template and signature contract readiness

Validation is useful before field use, before release packaging demos, and after moving project folders between machines.

## Recent Projects

SPINgen maintains a recent-project list outside the project data. This list is a convenience feature only.

You can:

- reopen a recent project
- remove a stale recent entry
- configure the number of recent projects shown
- reopen the last project automatically at startup

## Moving or Backing Up Projects

Projects are designed to stay portable because the core data is stored as JSON plus local files.

Best practice:

1. Close SPINgen.
2. Copy the entire project folder.
3. Reopen the copied folder from SPINgen on the destination machine.

Do not copy only `project.json`. The template, signatures, reports, photos, and previews belong to the same project folder.
