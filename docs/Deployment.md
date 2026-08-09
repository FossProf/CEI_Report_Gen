# Deployment

## System Requirements

- Windows 10 or Windows 11
- x64 operating system
- No separate .NET runtime installation required for the packaged release

## Installer Procedure

1. Build a release with `.\scripts\build-release.ps1`.
2. Obtain `artifacts\installer\SPINgen_0.3.0-alpha_x64.msi`.
3. Optionally keep `artifacts\release\SPINgen_0.3.0-alpha_win-x64.zip` for portable diagnostics.
4. Run the MSI.
5. Accept the installation prompts.
6. Launch the application from the Start Menu entry `SPINgen`.

## Building a Release

Preferred local command:

```powershell
.\scripts\build-release.ps1
```

This command:

- cleans old `artifacts/publish`, `artifacts/installer`, and `artifacts/release`
- restores, builds, and tests the solution
- publishes the current win-x64 self-contained app
- verifies the published executable version
- builds the MSI from that verified publish output
- creates a portable ZIP and SHA-256 hashes

Important output directories:

- `artifacts/publish/win-x64/`
- `artifacts/installer/`
- `artifacts/release/`

Diagnostic-only alternatives:

- `.\scripts\build-release.ps1 -SkipInstaller`
- `.\scripts\publish-release.ps1`

## Installed Application Location

Default application install location:

`C:\Program Files\SPINgen\`

The deployed bundle includes the packaged template at:

`C:\Program Files\SPINgen\Templates\CEI_Base_Template_Refined.docx`

The packaged release includes the current SPINgen executable, bundled template,
branding assets, icon resources, and self-contained .NET runtime dependencies.

## User and Project Data Location

User project data must remain outside the install directory.

Preferred default user location:

`%USERPROFILE%\Documents\SPINgen\Projects\`

The application may still let the user choose another writable project folder.

## Uninstall Behavior

- Uninstall removes installed application files.
- Uninstall must not remove user project folders.
- Uninstall must not remove user-selected project locations.

## SmartScreen Note

This `0.3.0-alpha` build is unsigned.

Windows SmartScreen may warn on first launch or install. Future release engineering can insert code signing at:

- executable signing after publish
- MSI signing after installer build

## Troubleshooting and Logs

- Generated error logs are written next to the affected report folder as `generation-error.log`.
- Preview files are written under a report `working\` folder.
- If installation fails, re-run the MSI with administrator permissions where required for a per-machine install.
- If `SPINgen is currently running`, close it before invoking `build-release.ps1`.
- If artifact cleanup fails, close Explorer windows or other processes holding files under `artifacts\`.
- If WiX packaging is denied or unavailable, use `.\scripts\build-release.ps1 -SkipInstaller` to produce a verified publish and portable ZIP without MSI packaging.

## Clean-Machine Acceptance Test

Use a clean Windows 10 or Windows 11 VM with:

- no Visual Studio
- no VS Code
- no .NET SDK
- no repository checkout

Test steps:

1. Install the MSI.
2. Launch from Start Menu.
3. Create a project.
4. Use the bundled template.
5. Import two signature images.
6. Create a zero-photo report.
7. Generate and finalize it.
8. Create a report with portrait and landscape photos.
9. Close and reopen the application.
10. Reopen the project.
11. Uninstall the application.
12. Confirm the user project still exists.
