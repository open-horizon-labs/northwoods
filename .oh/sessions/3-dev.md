# Dev Pipeline -- Add four distinct intake form templates with view/download
**Issue:** #3
**PR:** pending
**Started:** 2026-03-27T03:29:38Z

## Phase 1: Problem Statement
Issue #3 already provided a clear outcome and acceptance criteria:
- Seed four tenant-scoped templates with distinct schemas.
- Expose tenant templates via GET `/templates`.
- Provide frontend browsing plus view/download blank template output.
- Allow selecting any template in intake upload flow.

No issue body reframing required.

## Phase 2: Solution Space
Options considered:
1. Seed-only data expansion with no API/UI changes (insufficient; fails acceptance).
2. Add API contract + endpoints and minimal frontend template browser (selected).
3. Build dynamic form-builder/template designer (out of scope).

Selected approach:
- Expand seed data for both tenants with four distinct template schemas.
- Add API template contracts and tenant-scoped endpoints:
  - `GET /templates`
  - `GET /templates/{templateId}/blank?download=true|false`
- Render simple printable HTML blank form server-side from `field_schema`.
- Update frontend to load templates post-login, browse/select templates, and upload against chosen template.

## Phase 3: Execute
Implemented changes across DB seed, contracts, API, and web app.
Validation run:
- `dotnet build src/Northwoods.slnx`
- `pnpm check`

Both commands succeeded.

## Phase 4: Ship
Pending PR creation, review, and merge.

## RNA Tool Friction Log
| Phase | Tool | What happened | Workaround | Severity |
|-------|------|---------------|------------|----------|
| Phase 3 | RNA search/repo_map | Execution was already in progress before formal session file setup; code navigation used Read/Grep directly | Continued with direct navigation, documented friction for traceability | skipped |
