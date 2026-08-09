# Development Guidelines

## Rule 1

New features should be additive.

- Prefer extending UI or adding new Core entry points.
- Avoid changing proven storage or generation contracts unless fixing a defect.

## Rule 2

UI changes should not change business logic.

- WPF should present and orchestrate.
- Numbering, validation, finalization, and path safety stay in Core.

## Rule 3

Business logic belongs in Core.

- Project and report lifecycle rules belong in `CEI.ReportGenerator.Core`.
- UI code should call Core services instead of duplicating the rules.

## Rule 4

No UI should directly manipulate JSON.

- All project and report persistence goes through `ProjectStore`, `ReportStore`, and `JsonStore`.

## Rule 5

No UI should directly manipulate DOCX.

- Template processing and generated document handling go through `TemplateValidator`, `TemplateFiller`, and `ReportGenerator`.

## Rule 6

Template placeholders are a public contract.

Changing one requires updating:

- the template
- placeholder mapping code
- regression tests
- documentation

## Protected Foundation Areas

Future work should not modify these baseline systems unless fixing bugs or intentionally changing a documented contract:

- `ProjectStore`
- `ReportStore`
- `JsonStore`
- `ProjectLayout`
- `Validation`
- `TemplateValidator`
- `TemplateFiller`
- `ReportGenerator`
- `SignatureStore`
- photo storage
- photo normalization
- report numbering
- preview/final lifecycle
- `ReportDraftFactory` creation semantics
- project/report JSON contracts
- template validation
- OpenXML generation

UI/UX features may still evolve, but they should not modify these areas without a documented reason tied to a real defect or contract change.
