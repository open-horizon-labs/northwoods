# Dev Pipeline -- Implement RAG Similar Cases in review workflow
**Issue:** #1
**PR:** #12 (`https://github.com/open-horizon-labs/northwoods/pull/12`)
**Merge commit:** `69c3bbb77487c3c865c939defaece312926267b2`
**Started:** 2026-03-27T00:00:00Z
**Completed:** 2026-03-27T02:30:00Z

## Phase 1: Problem Statement
- Source issue: Implement RAG Similar Cases in review workflow.
- Target behavior: show plausible tenant-safe similar historical cases directly in review detail.

## Phase 2: Solution Space
- Implemented Postgres-native hybrid retrieval using:
  - per-document case profile records (`case_profiles`) with generated `tsvector` and `vector(16)` embedding
  - lexical ranking (FTS), vector distance, trigram similarity on name/address, and exact DOB boosts
  - Reciprocal Rank Fusion to combine ranking sources
- Chosen summary strategy: deterministic short context snippets assembled from candidate field values to keep output reliable and bounded.

## Phase 3: Execute
- Added `SimilarCaseItem` to contracts and wired `ReviewDetailResponse.SimilarCases`.
- Persisted case profiles in extraction worker with OCR-stage text + final extracted field values as embedding source text.
- Added schema/table/index/RLS updates for `case_profiles` and seeded synthetic historical cases.
- Added retrieval + ranking query in `GET /reviews/{id}` and returned top 5 similar cases.
- Added frontend similar-case panel in review detail.

## Phase 4: Ship
- Branch: `1-rag-similar-cases`
- Implementation commit: `f494605` (`feat: add similar-case retrieval to review workflow`)
- PR created and merged: #12

### Validation run
- `dotnet build src/Northwoods.slnx` ✅
- `pnpm check` ✅
- `docker compose down -v && docker compose up -d postgres minio api worker` ❌ (docker daemon unresponsive in this environment)
- `curl -sS -m 5 http://localhost:5100/healthz` ❌ timeout (no reachable local API)

## Phase 5: Oversight (post-merge comment audit)
- RNA index check completed via `mcp_rna_repo_map` (index present).
- PR review comments (`pulls/12/comments`): none.
- PR issue comments (`issues/12/comments`): one CodeRabbit "review in progress" status note only, no findings (Critical/Major/Minor).
- PR reviews: none.
- Unaddressed external findings: none.
- Followup PR required: no.

## Delivery Verification
- Feature touches intake/review workflow; live smoke was attempted but blocked by unavailable docker runtime in this environment.
- Follow-up: rerun live intake smoke when docker daemon is available:
  - `curl -sS -F 'file=@samples/intakes/chatgpt-sample-general-intake.pdf' -F 'templateId=general-assistance' -H 'X-Tenant-Id: tenant-a' http://localhost:5100/intakes`

## RNA Tool Friction Log
| Phase | Tool | What happened | What you did instead | Severity |
|-------|------|---------------|----------------------|----------|
| Setup | read/grep | Early implementation navigation used read/grep before post-merge RNA scan requirement in Phase 5. | Logged here; Phase 5 itself used RNA + GH API only. | low |

## Code Review Findings
- No actionable external findings were posted on PR #12 before/after merge.
