# Template Contract

This document is the official contract for supported report-template placeholders and signature controls in `v0.1-foundation`.

## Text Placeholders

- `{project.name}`
- `{project.num}`
- `{project.owner}`
- `{project.contract}`
- `{project.general}`
- `{project.report.num}`
- `{project.report.date}`
- `{project.report.temp}`
- `{project.report.weather}`
- `{project.report.location}`
- `{project.report.inspector}`
- `{project.report.personnel}`
- `{project.report.description}`
- `{project.report.drawing}`
- `{project.report.observations}`
- `{project.report.new_discrepancies}`
- `{project.report.old_discrepancies}`

## Image Placeholders

Photo slots are discovered from the photo table and use indexed placeholders such as:

- `{project.report.photos[1].image}`
- `{project.report.photos[1].caption}`

Rules:

- the template must contain a valid photo table
- the generator clones repeatable photo pages when needed
- empty captions keep the photo label and omit the colon
- report photos are inserted from project-local stored or source images

## Signature Placeholders

The template must contain Word content controls tagged:

- `inspection.signature.inspector`
- `inspection.signature.projectManager`

Rules:

- both controls must exist
- each control must contain one replaceable image drawing
- generation fails if either control is missing or invalid

## Future Compatibility Requirements

- Template placeholders are a public contract.
- Any placeholder change requires updating:
  - the Word template
  - Core mapping logic
  - smoke/regression tests
  - user-facing documentation
- Signature control tags must remain stable unless a coordinated contract update is made.
- Legacy misspelled placeholders are rejected during template validation.
