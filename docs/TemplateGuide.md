# Template Guide

SPINgen generates reports by filling an approved Word `.docx` template through Open XML. The template is a contract, not a free-form document.

## What the Template Must Contain

The project template must include:

- supported SPINgen text placeholders
- the required photo-table structure
- the two required signature content controls

The formal placeholder list is documented in [Template Contract](TemplateContract.md).

## Signatures

The template must contain Word content controls tagged:

- `inspection.signature.inspector`
- `inspection.signature.projectManager`

SPINgen replaces the embedded image inside those tagged controls with the current project signatures.

## Photos

Photo sections are driven by indexed placeholders such as:

- `{project.report.photos[1].image}`
- `{project.report.photos[1].caption}`

If a report has more photos than the base template supplies, SPINgen clones the repeatable photo layout.

## Template Validation Behavior

Before generation, SPINgen validates that the template:

- contains the required placeholders
- contains both signature controls
- contains a usable photo layout
- is structurally safe for generation

Generation stops early if the template contract is not met.

## Recommended Template Workflow

1. Keep one approved baseline template under source control or document control.
2. Distribute project copies through SPINgen project setup.
3. Avoid ad hoc placeholder renames in field deployments.
4. Revalidate the template after any Word-side edits.

## Compatibility Rule

Placeholders are a public contract between:

- the template
- the SPINgen generator
- tests
- documentation

Any placeholder change should be treated as a coordinated contract update, not a casual formatting edit.
