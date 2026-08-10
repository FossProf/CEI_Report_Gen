# Troubleshooting

This guide covers the most common field and developer issues in the current SPINgen baseline.

## Project Will Not Validate

Check for:

- missing required project fields
- missing template
- missing inspector or project manager signature
- unsupported signature extension
- paths that resolve outside the project folder

## Report Generation Fails

Check for:

- missing required report fields
- invalid weather value
- missing or unsupported photo file
- template placeholders removed or renamed
- missing signature content controls

Look for `generation-error.log` in the affected report folder.

## Final Report Naming Looks Wrong

Current finalized naming contract:

`YYYY-MM-DD {project name} SPIN Report #{report number}.docx`

Expected behavior:

- only one `SPIN` appears
- no leading zero padding appears in the final visible report number

## Report Deletion Says a File Is In Use

Most likely causes:

- Explorer preview is holding the file
- another document viewer has the `.docx` open
- Windows antivirus is briefly scanning the file
- a preview or final document handle has not been released yet

Recommended steps:

1. close any document viewers
2. close File Explorer preview panes
3. wait a few seconds and retry
4. reopen SPINgen and try again if needed

SPINgen blocks partial deletion on purpose when Windows reports an active file lock.

## Automatic Temperature Lookup Does Not Fill In

Check for:

- the Application Settings master temperature lookup switch is enabled
- the project has a saved, resolvable project location
- the inspection date is today or in the past
- internet access is available

Important behavior:

- SPINgen never auto-fills the `Weather` field
- future-dated reports do not use forecast temperatures
- historical values are based on the configured daytime window, which defaults to `7:00 AM` through `5:00 PM`

If lookup is unavailable, SPINgen leaves the existing temperature value unchanged and you can enter the field manually.

## Project Location Will Not Resolve

If a project location does not resolve:

1. simplify the location text
2. try ZIP code, city plus ZIP, or full street address
3. save the project anyway if you need to continue working

Unresolved project location does not block report generation. It only disables automatic temperature assistance until the location can be resolved.

## Installer Appears Out Of Date

If a newly installed MSI does not reflect current UI work:

1. rebuild with `.\scripts\build-release.ps1`
2. confirm the MSI timestamp under `artifacts\\installer\\`
3. uninstall the previous build if needed
4. reinstall from the newly built MSI

## Build Or Packaging Fails

Check for:

- SPINgen still running
- locked files under `artifacts\\`
- missing WiX tooling in the packaging environment

If WiX is unavailable, run:

```powershell
.\scripts\build-release.ps1 -SkipInstaller
```

This still produces a verified publish and portable ZIP.
