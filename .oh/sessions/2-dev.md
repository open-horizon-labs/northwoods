# Issue #2: Add search and case aggregate view

## Phase 1: Problem Statement
- Users need to search across processed intakes by name, date, template type
- Users need to view all documents aggregated for a person/case
- All queries must respect tenant boundaries via RLS

## Phase 2: Solution Space
- **Search**: Use existing `case_profiles.search_tsv` tsvector column (GIN indexed) for FTS, plus `pg_trgm` similarity on applicant_name for fuzzy matching
- **Case aggregate**: Match documents by applicant_name using exact + fuzzy matching via `similarity()` with 0.6 threshold
- **Frontend**: Add search form + results list, case detail view with all documents and their fields
- **Tenant isolation**: All queries go through `DbConnectionFactory.OpenSessionAsync` which sets RLS context

## Phase 3: Implementation

### Backend (Program.cs)
- `GET /search?q=...` -- FTS + fuzzy search over case_profiles, returns ranked results with ts_headline snippets
- `GET /cases/{personKey}` -- aggregates documents by applicant name match, returns documents with their extracted fields

### Contracts (Models.cs)
- `SearchResultItem` -- intakeId, templateId, applicantName, status, confidence, snippet
- `SearchResponse` -- query echo + results list
- `CaseDocumentItem` -- intakeId, templateId, status, createdAt, fields
- `CaseAggregateResponse` -- personKey + documents list

### Frontend
- **types.ts** -- Added SearchResultItem, SearchResponse, CaseDocumentItem, CaseAggregateResponse
- **api.ts** -- Added `search()` and `getCaseAggregate()` methods
- **App.tsx** -- Added:
  - State for search query, results, case view
  - Search handler with debounced form submission
  - Case view handler triggered by clicking search results
  - Section 5: Search with input, results list, confidence badges, FTS snippets
  - Section 6: Case view with document list, field details, review links
  - Nav links for both new sections
  - State resets in setPreset for tenant switching

## Phase 4: Ship
- Branch: `2-search-case-view`
- PR: #22
- CodeRabbit review findings addressed:
  - XSS fix: replaced `dangerouslySetInnerHTML` with safe React snippet highlighting
  - Added explicit `tenant_id` WHERE clause for defense-in-depth
  - Fixed N+1 query in case aggregate with batch field fetch via `ANY()`
  - Extracted similarity thresholds to named constants

## Phase 5: Post-Merge Audit
- Pending merge