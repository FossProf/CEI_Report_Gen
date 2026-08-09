# Report Search

This document defines the current search contract for the `0.3.1-alpha` SPINgen baseline.

## Search Scope

- Search is project-local only.
- Search operates against loaded `InspectionReport` objects in memory.
- Search does not read or parse generated DOCX files.
- Search does not maintain a persistent search index.

## Searchable Fields

- Report Number
- Date
- Temperature
- Weather
- Location
- Inspector
- Personnel On Site
- Description of Work
- Drawings Reviewed
- Observations
- New Discrepancies
- Previous Discrepancies
- Photo Caption
- Output File Name

## Matching Rules

- matching is case-insensitive
- matching is substring-based
- multiple keyword terms use AND semantics across the combined searchable fields
- blank keyword text does not filter by keyword
- report number matching supports natural forms such as `216`, `#216`, and the stored padded form
- date matching uses the report's formatted date text

## Structured Filters

- `Status` filters Draft vs Final reports
- `Weather` filters approved weather values
- `FromDate` is inclusive
- `ToDate` is inclusive
- keyword and structured filters combine with AND semantics

## Result Presentation

- `ReportSearchService` decides which reports match
- `ReportMatchSnippetBuilder` decides how to present the matching context
- `ReportSearchResult` wraps the matching `InspectionReport` without changing persistence
- match snippets are built only for filtered results, not for the entire project in advance

## Match Context Priority

When more than one field contains the query, display context uses this priority:

1. Observations
2. New Discrepancies
3. Previous Discrepancies
4. Description of Work
5. Location
6. Drawings Reviewed
7. Personnel On Site
8. Inspector
9. Weather
10. Temperature
11. Photo Caption
12. File Name
13. Report Number
14. Date

For multi-word searches, the preferred context is the field containing the largest number of query terms. Ties then use the priority order above.

## Persistence Contract

Search remains presentation and query behavior only.

No search-specific fields are persisted to `project.json` or `report.json`, including:

- `searchText`
- `searchIndex`
- `filterState`
- `matchSnippet`
