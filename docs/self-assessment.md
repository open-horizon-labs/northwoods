# Northwoods Self-Assessment

Date: 2026-03-27

## Executive summary

Northwoods delivers a complete intake-processing platform with a coherent vertical slice: template-guided upload, staged multi-provider extraction with confidence scoring, human-in-the-loop review with similar-case assistance, search, case aggregate view, and tenant-isolated multi-tenancy enforced via RLS. The system is documented, tested, and runnable from a fresh clone via `docker compose up -d`.

## What is complete and functioning

### Core workflow (upload, extract, review, finalize)

- **Template-guided upload**: 4 intake templates per tenant (General Assistance, Housing Stability, Financial Assistance, Clinical SOAP Note). Intake workers upload scanned PDFs associated with a template.
- **Background extraction**: Worker polls for queued documents and runs the configured extraction providers (OpenAI Vision plus optional stages). Each provider produces per-field extraction attempts with confidence scores. Attempts are append-only.
- **Confidence-tiered review**: Extracted fields are routed to review when confidence falls below threshold. Reviewers see fields, confidence indicators, the source document, and similar historical cases.
- **Finalize with corrections**: Reviewers correct low-confidence fields and finalize with an audit trail. Corrections are stored alongside original extraction.
- **Audit events**: `intake_uploaded`, `extraction_started`, `extraction_completed`, `extraction_failed`, `finalized` -- all tenant-scoped with correlation IDs.

### Search and case visibility

- **Full-text search** across processed intakes with snippet highlighting, tenant-scoped.
- **Case aggregate view** (`GET /cases/{personKey}`) shows all documents for a person across intakes.

### Similar-case assistance (RAG)

- Hybrid retrieval combining full-text search, vector similarity (pgvector), trigram fuzzy matching (pg_trgm), and structured attribute boosts (DOB, name, address).
- Reciprocal rank fusion produces a single match score.
- Similar cases appear in the review payload with match score and summary explaining why the case matched (same applicant, matching DOB, same template, etc.).
- Retrieval is tenant-isolated.

### Tenant isolation

- JWT claims propagate tenant context across all API endpoints.
- Database sessions set `app.tenant_id` for RLS enforcement.
- RLS policies on all data tables (`documents`, `templates`, `extracted_fields`, `case_profiles`, `audit_events`, `extraction_attempts`).
- `app_user` role has no BYPASS RLS.
- Cross-tenant access returns 404/empty -- verified in integration tests and manual smoke testing.
- Automated compliance checks: `scripts/ci/check-rls-compliance.py` verifies RLS enablement and policy presence.

### Observability

- Structured JSON logs with correlation IDs and scope metadata.
- `X-Correlation-Id` header propagated through request lifecycle.
- Tenant-scoped `GET /metrics` (request count, review finalization count, extraction success/failure).
- `GET /healthz` for liveness/readiness.
- Worker logs extraction timing (`processing_ms`) and OpenAI token usage per attempt.
- Retry with backoff on transient failures (configurable max attempts and delay).

### Tests

- **Unit tests**: Tenancy (2 tests), Worker extraction pipeline (15 tests including staged multi-provider coverage).
- **Integration tests**: Login validation (11 tests), workflow E2E with tenant isolation, search tenant scoping.
- **CI checks**: RLS compliance, append-only extraction_attempts, secret scanning.

### Documentation

- [Architecture Rationale](architecture.md) with system diagram, component responsibilities, extraction model, RAG design, tenancy strategy, trade-offs.
- 5 ADRs covering Postgres hybrid retrieval, Temporal (deferred), MinIO storage, shared-Postgres tenancy with RLS, and portable extraction pipeline.
- [AI Development Tooling](ai-tooling.md) describing tool usage throughout development.
- [Reviewer Rubric](reviewer-rubric.md) used as the internal quality gate.
- OpenAPI spec at `/openapi/v1.json`.

### Deliverables

| Deliverable | Status | Location |
|-------------|--------|----------|
| Source code | Complete | `src/`, `apps/web`, `tests/` |
| Docker Compose | Complete | `docker-compose.yml` |
| API documentation (OpenAPI) | Complete | `http://localhost:5100/openapi/v1.json` |
| Architecture document | Complete | `docs/architecture.md` |
| Self-assessment | This file | `docs/self-assessment.md` |
| Run instructions | Complete | `README.md` |
| Sample data | Complete | `samples/intakes/` + seed data in `init.sql` |

## Rubric scoring

Scoring uses the rubric in [Reviewer Rubric](reviewer-rubric.md) (1-4):

| Area | Score | Evidence |
|------|------:|----------|
| Intake digitization | 3 (Strong) | Template-guided upload, staged multi-provider background extraction, status tracking, confidence scoring, append-only attempt history. Verified end-to-end via smoke test and integration tests. |
| Human review | 3 (Strong) | Reviewer sees extracted fields with confidence beside source document. Low-confidence fields flagged for review. Finalize persists corrections with audit trail. Similar cases embedded in review payload. |
| Similar-case assistance | 3 (Strong) | Hybrid retrieval (FTS + vector + trigram + structured boosts) produces relevant matches with explanatory summaries. Embedded in review workflow, not standalone. Tenant-isolated. |
| Case visibility | 3 (Strong) | Full-text search with highlighting. Case aggregate view groups documents by person. Both tenant-scoped. |
| Tenant safety and operability | 4 (Excellent) | RLS on all tables, JWT claim propagation, no BYPASS RLS, automated compliance checks, structured logging, correlation IDs, health checks, retry behavior. Integration tests verify cross-tenant isolation. |
| Architecture judgment | 3 (Strong) | Coherent capability boundaries, explicit trade-offs, 5 ADRs, architecture doc with diagram. A staged multi-provider extraction model and Postgres-centered retrieval preserve reliability and explainability.

## Trade-offs and rationale

1. **Postgres-centered retrieval (vector + FTS + trigram + structured boosts)**: Satisfies "RAG must improve review judgment" without adding a second retrieval service. Simpler deployment, straightforward tenant filtering. Trade-off: heavier coupling and future scaling constraints.

2. **Append-only extraction attempts**: Supports auditability and confidence-based review. More storage but clearly improves trust and traceability.

3. **Multi-provider extraction over single-provider**: A staged combination of OpenAI Vision and optional providers (normalizer/escalation stages) is used before finalization. Trade-off: higher compute cost on escalated/complex documents.

4. **Shared Postgres tenancy with RLS backstop**: Single operational footprint with defense-in-depth isolation. Trade-off: query discipline mandatory, cross-tenant analytics become explicit product work.

- **Current extraction defaults**: `UseOpenAiVision` defaults to `true` and requires an OpenAI key; Paddle OCR, OpenAI normalizer, and additional providers run only when explicitly enabled.

## Known gaps

- **Production auth hardening**: bcrypt hashing is in place, but token refresh, rate limiting, and account lockout are not implemented.
- **OpenAI Vision in Docker**: the worker container requires `OPENAI_API_KEY` in the worker environment when `Extraction:UseOpenAiVision` is true.
- **UI smoke tests**: Playwright e2e test scaffold exists; full automation in CI is a follow-on item.

## AI-assisted development

This project was built primarily through agentic AI development using Claude with a structured pipeline (`dev-pipeline-oversight`). Each GitHub issue was worked end-to-end by an agent: branch, implement, review, dissent challenge, CodeRabbit, merge. The human role was scoping, reviewing agent output, catching drift, and making architectural calls.

### Representative prompts and what they produced

**1. Architecture synthesis (early session)**
```
Read the assignment brief at docs/exercise.md. Produce an architecture that satisfies all requirements
using a single API + worker + Postgres + MinIO topology. Justify every boundary. Draft ADRs for the
four most consequential decisions. Identify what I should NOT build to avoid unearned complexity.
```
→ Produced the one-API-one-worker design, the Postgres-for-retrieval decision (no separate vector DB),
the RLS tenancy model, and ADRs 001-005 including the portable extraction pipeline design.

**2. Staged extraction pipeline (issue #27)**
```
Run the configured extraction stages (PaddleOCR and OpenAI vision) in sequence and preserve append-only attempt history.
Log token usage (prompt, completion, total) in extraction_attempts.details as numeric JSONB.
Escalate to the mini model when confidence gates fail. Keep provider metadata for auditability.
```
→ Produced `OpenAiVisionProvider`, `CallWithFallback`, token logging in JSONB, and extraction test coverage.
The agent caught that `Dictionary<string,string>` needed to become `Dictionary<string,object>`
for numeric JSON serialization — a detail not in the original spec.

**3. Adversarial security audit (fuzz session)**
```
Fuzz the API as a stickler attacker. Test every endpoint for: auth bypass, tenant boundary violations,
null bytes, missing field handling, plaintext secrets, token persistence. Produce a structured report
with severity ratings (Critical/High/Medium) and reproduce steps for each finding.
```
→ Found F-01 (JWT MapInboundClaims breaking all auth), F-05 (plaintext passwords), B-01 (token not
persisted), null byte Postgres crash, and missing input validation. Each became a tracked issue.

**4. Confidence tier implementation (issue #42)**
```
ADR 005 promises three confidence tiers but they aren't implemented. Fix this completely:
- Removed mock provider as default path; `UseOpenAiVision` defaults true
- Implement auto-accept at >= 0.90, review_ready for 0.75-0.90, forced review below 0.75
- Escalate nano -> mini if avg field confidence < 0.75 after extraction
- Separate transient errors (retry 3x) from capability failures (escalate) from hard errors (fail)
- 6+ new tests covering each path
```
→ Agent implemented all four behaviors, correctly identified that `completed` status needed adding
to the document status enum, and caught that the retry test needed a mock HTTP handler.

**5. Requirements audit (adversarial reviewer session)**
```
You are a stickler reviewer with zero prior context. Extract every requirement from docs/exercise.md.
Check each one against the actual repo. Status: PASS / PARTIAL / FAIL / MISSING. Evidence must be
a file path and line number or command output. Do not mark anything PASS without verification.
```
→ Found 11 gaps including a live 500 on /review-queue, 8 failing integration tests, missing frontend
in Docker Compose, and a seed corpus too small for meaningful RAG. Every gap became a filed issue.

### What was not AI-generated
- The 40-person people roster and visit distribution design (`scripts/corpus/people.py`) — manually specified to match a realistic long-tail social services caseload.
- The rubric scoring and gap analysis — human judgment applied to AI output.
- The decision to use Postgres-centered retrieval instead of a dedicated vector DB — a deliberate architectural call made before any code was written.
- The decision not to implement Temporal (ADR 002) — deferred after weighing complexity against timeline.