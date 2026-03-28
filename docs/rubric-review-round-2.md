# Rubric Review Round 2: Final Polish and Submission Readiness

Date: 2026-03-27

## Context

This is the second rubric review pass (issue #10), run after round 1 (#9) fixes were merged. All implementation issues (#1-#8, #11, #23-#27) are merged. This review verifies submission readiness against the rubric and deliverable checklist.

## Environment

- Docker Compose freshly started from clean state (`docker compose down -v && docker compose up -d`)
- All 4 services healthy (postgres, minio, api, worker)
- Tests: 32/32 passing (2 tenancy unit + 15 worker unit + 15 API integration)
- `pnpm check` passing (frontend build + backend build)
- CI checks passing (ADR compliance, secret scan)

## Rubric Scoring

| Area | Round 1 | Round 2 | Evidence |
|------|--------:|--------:|----------|
| Intake digitization | 3 | 3 | 4 templates per tenant. Upload triggers background extraction pipeline with OpenAI-backed providers, including staging + confidence scoring, status transitions, and per-field confidence + append-only attempt metadata. |
| Human review | 3 | 3 | Review queue shows uncertain field count. Review detail shows fields sorted by confidence (uncertain first), source document in sandboxed iframe, audit events. Finalize persists corrections with reviewer note. |
| Similar-case assistance | 3 | 3 | Hybrid retrieval (FTS + vector + trigram + structured boosts with reciprocal rank fusion) embedded in review payload. Returns match score + explanatory summary. Tenant-isolated. 3-4 similar cases returned for seed data. |
| Case visibility | 3 | 3 | Full-text search with snippet highlighting. Case aggregate view groups documents by person. Both tenant-scoped. |
| Tenant safety and operability | 4 | 4 | RLS on all 7 data tables. JWT claim propagation. No BYPASS RLS. Cross-tenant access returns 404/empty. Tenant-scoped metrics. Structured logging with correlation IDs. Health checks. Retry on transient failures. Bcrypt password hashing. Input validation (null bytes, missing fields). CI compliance checks (RLS, append-only, secrets). |
| Architecture judgment | 3 | 3 | Architecture doc with diagram, component responsibilities, extraction model, RAG design, tenancy strategy, trade-offs. 5 ADRs. Self-assessment with rubric scoring and known gaps. AI tooling documentation. |

## Deliverable Checklist

| Deliverable | Present | Notes |
|-------------|---------|-------|
| Source code | Yes | `src/`, `apps/web/`, `tests/` |
| Docker Compose | Yes | `docker-compose.yml` -- starts all 4 services with health checks |
| OpenAPI/Swagger | Yes | `/openapi/v1.json` -- 11 endpoints documented |
| Architecture document | Yes | `docs/architecture.md` with Mermaid diagram |
| Self-assessment | Yes | `docs/self-assessment.md` with rubric scoring, known gaps, AI examples |
| Run instructions | Yes | `README.md` with full E2E smoke test commands |
| Sample data | Yes | 5 PDFs in `samples/intakes/`, seed data in `init.sql` |

## Verification Results

### Fresh clone flow
1. `docker compose up -d` -- starts cleanly, all services healthy
2. `curl /healthz` -- returns "Healthy"
3. Login works for all 4 seed users (2 tenants x 2 roles)
4. Templates endpoint returns 4 templates per tenant
5. Upload produces `intakeId` with status 0 (Uploaded)
6. Worker extracts within ~4 seconds, transitions to status 2 (ReviewReady)
7. Review payload includes 7 extracted fields with confidence, source document URL, audit events, and similar cases
8. Finalize with corrections succeeds, transitions to status 3 (Finalized)
9. Search returns tenant-scoped results with highlighting
10. Case aggregate view returns all documents for a person

### Cross-tenant isolation
- Tenant-B cannot access Tenant-A reviews (404)
- Tenant-B search for "Jamie" returns 0 results (Tenant-A has Jamie Carter)
- Metrics are tenant-scoped (different finalization/success counts per tenant)

### Test suite
- Tenancy unit: 2/2 passed
- Worker unit: 15/15 passed
- API integration: 15/15 passed (includes E2E workflow, tenant isolation, search scoping, case aggregation)

## Issues Fixed Since Round 1

| Issue | Fix | Impact |
|-------|-----|--------|
| #23 | JWT claim mapping (`MapInboundClaims=false`) | All authenticated endpoints now work |
| #24 | Auth token persistence in localStorage | Session survives page reload |
| #25 | Bcrypt password hashing | No more plaintext passwords |
| #26 | Login input validation | Missing fields/null bytes return 400 not 500 |
| #27 | Multi-provider extraction (OpenAI Vision + optional staging providers) | Better extraction quality, token usage logging |
| Metrics | Tenant-scoped ReviewFinalizationCount | Integration test now passes |
| Iframe | Added `sandbox="allow-same-origin"` | CodeRabbit finding addressed |

## Known Gaps (Documented in Self-Assessment)

- Web frontend not in Docker Compose (run separately)
- Token refresh/rate limiting not implemented
- OpenAI Vision requires API key in worker environment
- Case profile embeddings use hash-based generator, not real embedding model
- UI smoke tests not automated in CI

## Conclusion

All rubric areas score 3 or above. All deliverables present. No cross-tenant leakage detected. Docker Compose starts cleanly from fresh clone. System is submission-ready.
