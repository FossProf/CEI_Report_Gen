# Deployment

## System Requirements

- Windows 10 or Windows 11
- x64 operating system
- No separate .NET runtime installation required for the packaged release

## Installer Procedure

1. Obtain `CEI_Report_Generator_0.1.0-alpha_x64.msi`.
2. Run the MSI.
3. Accept the installation prompts.
4. Launch the application from the Start Menu entry `SPINgen`.

## Installed Application Location

Default application install location:

`C:\Program Files\SPINgen\`

The deployed bundle includes the packaged template at:

`C:\Program Files\SPINgen\Templates\CEI_Base_Template_Refined.docx`

The current alpha packaging uses a neutral placeholder application icon. Replace
`src/CEI.ReportGenerator.App\Assets\AppIcon.ico` with an approved CEI asset before
shipping a branded release.

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

This `0.1.0-alpha` build is unsigned.

Windows SmartScreen may warn on first launch or install. Future release engineering can insert code signing at:

- executable signing after publish
- MSI signing after installer build

## Troubleshooting and Logs

- Generated error logs are written next to the affected report folder as `generation-error.log`.
- Preview files are written under a report `working\` folder.
- If installation fails, re-run the MSI with administrator permissions where required for a per-machine install.

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
