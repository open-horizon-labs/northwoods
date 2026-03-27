# Northwoods Self-Assessment

Date: 2026-03-27

## Executive summary

Northwoods delivers a complete intake-processing platform with a coherent vertical slice: template-guided upload, dual-provider extraction with confidence scoring, human-in-the-loop review with similar-case assistance, search, case aggregate view, and tenant-isolated multi-tenancy enforced via RLS. The system is documented, tested, and runnable from a fresh clone via `docker compose up -d`.

## What is complete and functioning

### Core workflow (upload, extract, review, finalize)

- **Template-guided upload**: 4 intake templates per tenant (General Assistance, Housing Stability, Financial Assistance, Clinical SOAP Note). Intake workers upload scanned PDFs associated with a template.
- **Background extraction**: Worker polls for queued documents and runs a dual-provider extraction pipeline (mock OCR + optional OpenAI Vision). Each provider produces per-field extraction attempts with confidence scores. Attempts are append-only.
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

- **Unit tests**: Tenancy (2 tests), Worker extraction pipeline (15 tests including dual-provider).
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
| Intake digitization | 3 (Strong) | Template-guided upload, dual-provider background extraction, status tracking, confidence scoring, append-only attempt history. Verified end-to-end via smoke test and integration tests. |
| Human review | 3 (Strong) | Reviewer sees extracted fields with confidence beside source document. Low-confidence fields flagged for review. Finalize persists corrections with audit trail. Similar cases embedded in review payload. |
| Similar-case assistance | 3 (Strong) | Hybrid retrieval (FTS + vector + trigram + structured boosts) produces relevant matches with explanatory summaries. Embedded in review workflow, not standalone. Tenant-isolated. |
| Case visibility | 3 (Strong) | Full-text search with highlighting. Case aggregate view groups documents by person. Both tenant-scoped. |
| Tenant safety and operability | 4 (Excellent) | RLS on all tables, JWT claim propagation, no BYPASS RLS, automated compliance checks, structured logging, correlation IDs, health checks, retry behavior. Integration tests verify cross-tenant isolation. |
| Architecture judgment | 3 (Strong) | Coherent capability boundaries, explicit trade-offs, 5 ADRs, architecture doc with diagram. Dual-provider extraction shows practical AI integration. Postgres-centered retrieval avoids unnecessary infrastructure. |

## Trade-offs and rationale

1. **Postgres-centered retrieval (vector + FTS + trigram + structured boosts)**: Satisfies "RAG must improve review judgment" without adding a second retrieval service. Simpler deployment, straightforward tenant filtering. Trade-off: heavier coupling and future scaling constraints.

2. **Append-only extraction attempts**: Supports auditability and confidence-based review. More storage but clearly improves trust and traceability.

3. **Dual-provider extraction over single-provider**: Mock OCR provides deterministic demo reliability; OpenAI Vision provides real extraction quality. Both run on every document and the best candidate wins per field. Trade-off: higher cost per extraction when Vision is enabled.

4. **Shared Postgres tenancy with RLS backstop**: Single operational footprint with defense-in-depth isolation. Trade-off: query discipline mandatory, cross-tenant analytics become explicit product work.

5. **Mock OCR as default provider**: Ensures the system works reliably without external API keys. OpenAI Vision is opt-in. Trade-off: demo quality is limited to deterministic mock output unless Vision is configured.

## Known gaps

- **Web frontend**: React UI exists but is not included in Docker Compose as a container. Run separately with `pnpm --dir apps/web dev`.
- **Production auth hardening**: Bcrypt hashing is in place, but token refresh, rate limiting, and account lockout are not implemented.
- **OpenAI Vision in Docker**: The worker container does not have `OPENAI_API_KEY` by default. Vision extraction requires setting the key in the worker environment.
- **Embedding quality**: Case profile embeddings use a deterministic hash-based generator, not a real embedding model. Real embeddings would improve similar-case retrieval relevance.
- **UI smoke tests**: Browser-level Playwright tests exist in reports but are not automated in CI.

## AI-assisted development

### Example 1: Architecture and scoping
- **Use:** AI assisted with initial architecture synthesis, rubric design, and ADR drafting -- translating assignment requirements into a coherent system design.

### Example 2: Extraction pipeline design
- **Use:** AI helped design the dual-provider extraction model, including the append-only attempt storage pattern and confidence consensus logic.

### Example 3: Security hardening
- **Use:** Fuzz testing (API + browser) was AI-assisted, producing structured findings reports that drove the fix loop (JWT claim mapping, bcrypt passwords, input validation, token persistence).

### Example 4: RAG retrieval implementation
- **Use:** The hybrid retrieval query (FTS + vector + trigram + structured boosts with reciprocal rank fusion) was developed with AI assistance to combine multiple retrieval modalities without external dependencies.

### Example 5: CI compliance checks
- **Use:** AI helped design AST/linter-level compliance checks for RLS enforcement and append-only guarantees, wrapping them in a CI workflow.
