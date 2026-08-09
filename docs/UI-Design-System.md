# UI Design System

This document defines the CEI Report Generator light-theme visual system introduced in the UI/UX visual identity slice.

## Canonical CEI Colors

These colors are treated as template-derived brand references:

- `#58585A` - Cornerstone Charcoal
- `#9D782C` - Cornerstone Gold
- `#5B8C9C` - CEI Report Teal
- `#5C92A0` - Secondary Report Teal
- `#6E6E6E` - Supporting Gray
- `#FFFFFF` - White

These are centralized in [Colors.xaml](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/Resources/Colors.xaml).

## Derived Application Colors

The following neutrals are application design choices. They support readability and desktop layout separation, but are not claimed to have been extracted directly from the Word template.

- `#F5F6F6` - application background
- `#FFFFFF` - surface/panel background
- `#D7DADB` - control and panel borders
- `#333333` - primary body text
- `#6E6E6E` - muted text
- `#EAF1F3` - light header tint
- `#DCEBED` - selection tint
- `#4F7A87` / `#436A75` - darker teal interaction states
- `#F3EAD7` - restrained gold tint
- `#2F7D5A` - ready state
- `#B24A3D` - error/destructive state

## Resource Structure

Theme resources are merged globally through [App.xaml](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/App.xaml).

- `Resources/Colors.xaml`
- `Resources/Brushes.xaml`
- `Resources/Typography.xaml`
- `Resources/Controls.xaml`
- `Resources/Icons.xaml`

This keeps windows from needing local theme imports.

## Brush Roles

The application uses semantic brush names instead of scattering hex values through view XAML.

- `AppBackgroundBrush` - window background
- `SurfaceBrush` - cards, panels, editable surfaces
- `PrimaryTextBrush` - charcoal/high-priority text
- `SecondaryTextBrush` - helper text and subordinate labels
- `PrimaryAccentBrush` - dominant structural accent
- `SecondaryAccentBrush` - secondary teal accent
- `BrandGoldBrush` - restrained branding accent
- `BorderBrush` - panel and control borders
- `SelectionBrush` - list/grid selected-state tint
- `HeaderBrush` - light header background
- `StatusReadyBrush` - ready state
- `StatusWarningBrush` - warning/attention state
- `StatusErrorBrush` - error/missing/destructive state

## Typography

The desktop application uses Windows-native `Segoe UI`.

- App/page heading: `20-22px`, `SemiBold`, charcoal
- Section heading: `15px`, `SemiBold`, teal
- Field labels: `12px`, `SemiBold`, supporting gray
- Body text: `13px`, normal, charcoal
- Secondary/helper text: `11-12px`, normal, supporting gray

Typography resources are defined in [Typography.xaml](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/Resources/Typography.xaml).

## Button Hierarchy

Buttons are implemented with shared styles in [Controls.xaml](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/Resources/Controls.xaml).

- `PrimaryButton`
  - teal fill, white text
  - use for create/save/generate/new-report actions
- `SecondaryButton`
  - white surface, teal border, teal text
  - use for common actions that are important but not dominant
- `QuietButton`
  - transparent/light treatment
  - use for navigation and low-emphasis utility actions
- `DangerButton`
  - restrained red treatment
  - use only for destructive or discard-style actions

Gold is intentionally not used as the default button fill.

## Panels and Section Headers

- Panels use white surfaces with restrained borders and small radii
- Section headers use text-first hierarchy with a thin teal divider
- Heavy shadows, gradients, and glass effects are intentionally avoided

## Data Grid Rules

The project reports grid keeps native WPF behavior while applying:

- light tinted headers
- teal-accent header rule
- readable tinted row selection
- neutral text on selected rows

Sorting, selection, and keyboard behavior are not replaced.

## Status and Readiness Conventions

Status always remains text-first.

- Ready: green indicator plus explicit `Ready` text
- Attention: amber indicator plus explicit warning text
- Error/destructive: red reserved for failures and destructive actions

Icons and color support the message, but neither stands alone.

## Icon System

Source:

- Bootstrap Icons
- Version `1.13.1`
- License `MIT`
- Official project: `https://icons.getbootstrap.com/`

Vendored assets live in [Assets/Icons](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/Assets/Icons).

License notice:

- [LICENSE-Bootstrap-Icons.txt](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/Assets/Icons/LICENSE-Bootstrap-Icons.txt)

Only the icons actually used or approved for near-term use are vendored:

- `speedometer2`
- `file-earmark-text`
- `folder2-open`
- `gear`
- `plus-circle`
- `copy`
- `search`
- `person-badge`
- `image`
- `check-circle`
- `exclamation-triangle`
- `x-circle`
- `arrow-clockwise`
- `info-circle`
- `pencil`
- `floppy`
- `trash`
- `calendar3`

WPF uses converted geometry resources in [Icons.xaml](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/src/CEI.ReportGenerator.App/Resources/Icons.xaml), not a live SVG runtime dependency.

## Icon Sizing

- Standard inline/menu/button icons: `16px`
- Section icons: `18px`

Icons are support elements, not decorative focal points.

## Accessibility Rules

- Primary body text stays charcoal/dark neutral on light backgrounds
- Gold is reserved for accents and is not used as dense small-body text on white
- Keyboard focus indicators remain visible
- Native menu semantics, tab order, and control behaviors are preserved
- Icons do not replace labels

## Logo Usage

The main shell now uses the approved Cornerstone lockup sourced from the repository-level [assets folder](/C:/Potential%20Projects/CEI%20Tools/CEI_Report_Gen/assets).

- `CEI_Cornerstone_Logo_No_Tagline_Transparent.png` is linked into the WPF app as a packaged resource and used in the main window header
- The app icon asset remains in use for the executable and window icon
- No logo is extracted from the DOCX template at runtime
- Branding remains confined to shell/header presentation so core generation workflows stay untouched
