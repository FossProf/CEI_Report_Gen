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
