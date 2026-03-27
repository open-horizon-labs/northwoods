# Rubric Review Round 1 (Issue #9)

Date: 2026-03-27

## Scoring Summary

| Area | Score | Post-Fix Score | Evidence |
|------|-------|---------------|----------|
| Intake digitization | 3 (Strong) | 3 | Template-guided upload, dual OCR pipeline, confidence-tiered fields, append-only attempts, status polling |
| Human review | 2 (Credible but shallow) | **3 (Strong)** | Fixed: fields now sorted confidence-ascending (uncertain first); source document embedded in iframe alongside fields |
| Similar-case assistance | 3 (Strong) | 3 | Hybrid retrieval (FTS + vector + trigram + structured boosts); evidence summaries embedded in review; verified 3 matches for test intake |
| Case visibility | 3 (Strong) | 3 | Full-text + fuzzy search with highlighted snippets; case aggregate groups documents by person; search->case->review navigation |
| Tenant safety and operability | 3 (Strong) | 3 | RLS on all tables, DbConnectionFactory tenant scoping, JWT propagation, integration tests; **fixed**: worker UPDATE queries now include tenant_id; API similar-case field query now filters by tenant_id |
| Architecture judgment | 3 (Strong) | 3 | ADRs 001-005, architecture rationale doc, trade-offs documented, AI tooling transparent, capability boundaries coherent |

## Detailed Assessment

### 1. Intake Digitization (Score: 3)

**What good looks like:** Upload is template-guided; background processing is visible; original file, extracted fields, confidence scores, and status are all retained; the result is searchable structured data.

**Evidence:**
- Upload is template-guided (`POST /intakes` requires `templateId`; UI shows template selector with field schemas)
- Background worker polls and processes with status transitions: `uploaded -> extracting -> review_ready`
- Frontend polls at 2.5s intervals and shows real-time status transitions
- Dual OCR pipeline (PaddleOCR + OpenAI Vision) with configurable providers
- Confidence scores are explicit per field with centralized thresholds (`High >= 0.90`, `ReviewRequired < 0.75`)
- Append-only `extraction_attempts` table persists all provider results with run metadata
- `case_profiles` table enables full-text search, vector similarity, and trigram matching

**No issues found below threshold.**

### 2. Human Review (Score: 2 -> 3 after fixes)

**What good looks like:** Reviewers see extracted fields beside the source image/PDF, understand confidence, correct only what is needed, and finalize with an audit trail.

**Issues found:**
1. **Source document opened in separate tab** rather than shown beside fields. Reviewers had to context-switch between windows.
2. **Fields not sorted by confidence.** Uncertain fields (needing review) appeared in arbitrary database order, not prioritized for reviewer attention.

**Fixes applied:**
1. Added iframe embedding of source document directly in review detail section, with "Open in new tab" as secondary option
2. Fields now sorted confidence-ascending so most uncertain fields appear first

**Post-fix evidence:**
- Review detail shows source PDF/image in embedded 480px iframe alongside editable fields
- Fields sorted lowest-confidence first (e.g., notes 0.49 -> monthlyIncome 0.53 -> ... -> applicantName 0.85)
- Confidence tiers displayed with color-coded badges (emerald/sky/amber/rose)
- Reviewer can edit fields, add notes, and finalize with audit trail
- Only Reviewer role can finalize (enforced API-side)

### 3. Similar-Case Assistance (Score: 3)

**What good looks like:** Retrieval is embedded in the review workflow; similar cases are plausibly relevant; summaries/comparison cues help judgment at the moment of review.

**Evidence:**
- `FindSimilarCasesAsync` uses 5-mode hybrid retrieval:
  - Full-text search (`ts_rank_cd`)
  - Vector similarity (`pgvector` cosine distance)
  - Name fuzzy matching (`pg_trgm` similarity > 0.25)
  - Address fuzzy matching (similarity > 0.2)
  - DOB exact match
- Scores fused with reciprocal-rank blending (`1/(60+rank)`)
- Similar cases displayed in review detail with match evidence summaries ("same applicant, matching DOB, matching address, same template")
- Clicking a similar case navigates to its review
- Verified: test intake for "Jamie Carter" found 3 similar cases including a misspelled "Jamie Carrr" via fuzzy matching

### 4. Case Visibility (Score: 3)

**What good looks like:** Users can search by meaningful attributes and understand a person/case across multiple documents and statuses.

**Evidence:**
- Search endpoint: full-text (`websearch_to_tsquery`) + trigram fuzzy matching
- Search results show applicant name, template, status, confidence, and highlighted snippet
- Case aggregate endpoint groups all documents for a person (fuzzy name matching, threshold 0.6)
- Each document in case view shows fields with confidence badges
- Navigation: search result -> case view -> individual review
- Verified: search for "Jamie" returns 3 results; case view groups 2 documents under "Jamie Carter"

### 5. Tenant Safety and Operability (Score: 3)

**What good looks like:** Tenant context enforced across APIs, jobs, storage, search, and retrieval; logs, health checks, retries, tests, and docs make the system inspectable.

**Evidence:**
- RLS enabled on 7 tables with tenant isolation policies
- `DbConnectionFactory` sets `app.tenant_id` and `SET LOCAL ROLE app_user` per session
- All API endpoints use `DbConnectionFactory.OpenSessionAsync(tenantId)` with explicit tenant predicates
- JWT claims propagate tenant_id; `X-Tenant-Id` header not trusted (derived from JWT)
- Object storage keys include tenant_id path prefix
- Integration tests verify cross-tenant isolation (tenant-b cannot access tenant-a documents)
- Verified: tenant-b search for "Jamie" returns 0 results (tenant-a data invisible)
- Health check endpoint (`/healthz`), structured JSON logging, correlation IDs across request lifecycle
- Worker retry with exponential backoff for transient failures

**Fixes applied (defense-in-depth):**
1. Worker `UPDATE documents SET status` queries now include `AND tenant_id = @TenantId` (3 locations)
2. `FindSimilarCasesAsync` extracted_fields query now includes `AND tenant_id = @TenantId`

**Known trade-off:** Worker connects directly to Postgres without RLS session setup (it processes all tenants). Explicit tenant_id predicates are the enforcement mechanism for the worker path. This is documented as an intentional design choice (worker needs cross-tenant polling) with the tenant_id filter as the defense layer.

### 6. Architecture Judgment (Score: 3)

**What good looks like:** Capability boundaries are coherent; complexity is earned; docs explain trade-offs, AI usage, and omissions clearly.

**Evidence:**
- 5 ADRs covering key decisions (hybrid retrieval, Temporal, MinIO, tenancy model, extraction pipeline)
- Architecture doc with system diagram, component responsibility matrix, trade-offs
- Self-assessment (issue #8) with honest gap disclosure
- AI tooling documented with specific tool purposes and human-owned decisions
- README with local dev instructions, extraction pipeline overview, observability notes
- Monorepo with clean capability boundaries:
  - API: auth/request boundary + query surface
  - Worker: extraction behavior + confidence gating
  - BuildingBlocks: tenancy enforcement + storage abstraction
  - Frontend: workflow UI with confidence as primary signal

## Verification

- `pnpm check`: passes (web build + dotnet build)
- `dotnet test tests/Northwoods.Worker.UnitTests/`: 15/15 passing
- Live smoke test: upload -> extraction (review_ready in ~10s) -> review detail with 3 similar cases -> search with tenant isolation confirmed
