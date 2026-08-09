# Search Guide

SPINgen search helps users locate reports within the currently open project without leaving the project window.

## Scope

Search is:

- project-local
- JSON-backed
- in-memory
- immediate after project load

Search is not:

- cross-project
- DOCX-content parsing
- database-backed

## Workspaces

The project window has two workspaces:

- `Reports`
- `Search`

`Search` keeps the same report grid but adds search controls and result context.

## Search Inputs

Users can search by:

- free-text keyword input
- report status
- weather
- from date
- to date

Filters combine with AND semantics.

## Keyword Matching

Keyword matching is:

- case-insensitive
- substring-based
- multi-term AND matching

Useful examples:

- `216`
- `#216`
- `grout`
- `deck repair`
- `2026-08-08`

## Result Context

Search results can show:

- the matched field
- a contextual snippet
- the full matched text as a tooltip or extended context

This helps the user understand why a report matched before opening it.

## Typical Uses

- find a finalized report by number
- find reports mentioning a discrepancy
- isolate draft reports
- find rainy-day inspections
- review reports within a date window

## Limits

Search does not modify persisted report data. It is a read-only navigation aid layered on top of loaded reports.
