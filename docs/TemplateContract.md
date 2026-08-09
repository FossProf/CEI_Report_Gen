# Template Contract

This is the official SPINgen template contract for supported placeholders, images, and signatures.

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

## Photo Placeholders

Photo content uses indexed placeholder pairs such as:

- `{project.report.photos[1].image}`
- `{project.report.photos[1].caption}`

Rules:

- the template must provide a valid photo layout
- photo placeholders are one-based
- the generator can extend the photo layout for additional photos
- captions come from persisted report photo data

## Signature Content Controls

The template must contain these Word content-control tags:

- `inspection.signature.inspector`
- `inspection.signature.projectManager`

Rules:

- both controls are required
- each control must contain a replaceable image
- missing or malformed controls fail template validation

## Compatibility Requirements

Treat this contract as public and versioned.

Any change to a placeholder or signature tag requires coordinated updates to:

- the Word template
- generator mapping logic
- tests
- user documentation
- release notes when externally visible
