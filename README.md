# Northwoods

Northwoods processes handwritten intake documents into structured, confidence-scored fields that route through human-in-the-loop review before acceptance. It was built for the Banyan Software CTO assignment.

**Live instance:** [northwoods.muness.com](https://northwoods.muness.com)

## For the reviewer

This section is for you. It maps the assignment requirements to where they live in the codebase.

### Try the live system

| Role | Email | Password | What you'll see |
|---|---|---|---|
| Intake Worker | worker@sunrise.example | password | Upload dashboard — select template, attach PDF, watch status |
| Reviewer | reviewer@sunrise.example | password | Review queue — confidence indicators, field corrections, similar cases, finalize |

Tenant-b (Lakewood) credentials also exist (`worker@lakewood.example`, `reviewer@lakewood.example`) — use them to verify tenant isolation.

The developer scaffold (preset login buttons, raw API explorer) is at [northwoods.muness.com/#dev](https://northwoods.muness.com/#dev). It is not linked from the main UI.

### Evaluate the architecture

| Document | What it covers |
|---|---|
| [Architecture Rationale](docs/architecture.md) | System diagram, component responsibilities, extraction model, RAG strategy, tenancy model, trade-offs |
| [ADR 001: Postgres hybrid retrieval](docs/ADRs/001-postgres-hybrid-retrieval-with-pgvector-and-pg-trgm.md) | Why Postgres for vector search instead of a dedicated vector DB |
| [ADR 002: Temporal for workflows](docs/ADRs/002-temporal-for-document-processing-workflows.md) | Considered and deferred — worker polling used instead |
| [ADR 003: MinIO for storage](docs/ADRs/003-minio-for-s3-compatible-document-storage.md) | S3-compatible object store for documents |
| [ADR 004: Shared tenancy with RLS](docs/ADRs/004-shared-postgres-tenancy-with-rls-backstop.md) | Row-level security as defense-in-depth for tenant isolation |
| [ADR 005: Consensus extraction pipeline](docs/ADRs/005-portable-consensus-extraction-pipeline.md) | Multi-provider extraction with append-only attempt history |

### Evaluate AI tool usage

| Document | What it covers |
|---|---|
| [AI Development Tooling](docs/ai-tooling.md) | What tools were used, how, and what remained human-owned |
| [Self-Assessment](docs/self-assessment.md) | What's complete, what's missing, 5 concrete prompts with outputs |

The agentic development pipeline is in `.claude/agents/dev-pipeline.md` and `.claude/agents/dev-pipeline-oversight.md`. Each GitHub issue was worked end-to-end by an agent: branch, implement, `/review`, `/dissent`, CodeRabbit, merge. The human role was scoping, reviewing output, catching drift, and making architectural calls.

### RAG pipeline report

Log in as a reviewer and navigate to [/#rag-report](https://northwoods.muness.com/#rag-report) to see the RAG pipeline results: expected vs. actual similar case retrieval for known narrative arcs across 40 fictional people. This page runs live queries — it is not canned data.

### What to look for

- **Confidence drives behavior.** Low-confidence fields route to review. High-confidence fields can auto-accept. The review UI shows *why* the system is uncertain (per-provider breakdown).
- **Tenant isolation is provable.** RLS on all data tables. `scripts/ci/check-rls-compliance.py` verifies it. Integration tests confirm cross-tenant returns empty.
- **Audit trail is append-only.** `extraction_completed → field_corrected × N → finalized` — each event with actor, timestamp, correlation ID, before/after values.
- **The corpus tells a story.** 40 people across two tenants, two visual form styles, longitudinal narrative arcs. Frequent flyers (P019 Raymond Castillo, P039 Gloria Navarro) show progression from crisis to stability across months. The RAG report page shows whether the system found these relationships.

---

## Run locally

### Prerequisites

- Docker and Docker Compose
- .NET SDK 10
- Node 22+ and pnpm

### Start everything

```bash
docker compose up -d
```

Starts Postgres (with pgvector), MinIO, the API, the extraction worker, and the web frontend. Schema and seed data load automatically.

| Service | URL |
|---|---|
| Web UI | http://localhost:5173 |
| API | http://localhost:5100 |
| OpenAPI spec | http://localhost:5100/openapi/v1.json |
| MinIO console | http://localhost:9001 (northwoods/northwoods) |
| Health check | http://localhost:5100/healthz |

### Run tests

```bash
# All backend tests
dotnet test src/Northwoods.slnx

# Frontend type check + lint
pnpm --filter web check

# Playwright e2e (requires running stack + pnpm dev)
pnpm --filter web test:e2e

# RAG pipeline test (requires running stack + OPENAI_API_KEY)
dotnet test tests/Northwoods.Api.IntegrationTests/ --filter Category=RAG
```

### Enable OpenAI Vision extraction

The extraction worker defaults to `UseOpenAiVision=true`. Without an API key the worker will throw a startup exception and documents will not be processed.

Set `OPENAI_API_KEY` before starting:

```bash
export OPENAI_API_KEY=sk-...
docker compose up -d
```

Or create a `.env` file at the repo root (docker compose picks this up automatically):

```bash
cp .env.example .env
# Edit .env and set OPENAI_API_KEY=sk-...
```

To run without extraction (UI/API development only), disable the provider explicitly:

```bash
# Add to .env:
Extraction__UseOpenAiVision=false
```

In this mode uploads and review UI work, but no documents will be extracted.

---

## System overview

```
Intake Worker → [Upload PDF] → API → MinIO (store) + Postgres (metadata)
                                        ↓
                              Extraction Worker polls
                                        ↓
                              OCR/AI extraction → confidence scoring
                                        ↓
                              review_ready → Reviewer sees queue
                                        ↓
                              Reviewer: correct fields, finalize
                                        ↓
                              Embeddings regenerated → case_profiles updated
                                        ↓
                              RAG: similar cases surfaced for next review
```

### Stack

| Component | Technology | Responsibility |
|---|---|---|
| API | .NET 10 minimal API | Auth, upload, review, search, audit, tenant scoping |
| Extraction Worker | .NET BackgroundService | Polling, staged provider extraction, confidence gating, append-only attempts |
| Frontend | React + TypeScript + Tailwind | Role-based dashboards, confidence visualization, similar case panel |
| Database | Postgres 18 + pgvector + pg_trgm | System of record, RLS, hybrid retrieval, FTS |
| Object Storage | MinIO (S3-compatible) | Document blobs |

### Seed data

- **2 tenants:** Sunrise (tenant-a) and Lakewood (tenant-b)
- **6 users per system:** worker + reviewer + admin per tenant (password: `password`)
- **4 templates per tenant:** General Assistance, Housing Stability, Behavioral Health, SOAP Progress Note
- **184 seeded documents** across two visual form cohorts with narrative arc data
- **Corpus generators** in `scripts/corpus/` — regenerate with `python3 scripts/corpus/generate_seed_sql.py`

---

## Deployment

Deployment is release-driven. Create and push a `v*` tag (for example `v0.3.3`) and GitHub Actions will build images and publish a GitHub Release for that tag.

It then updates all three Render services (api, worker, web) and triggers deploys.

Blueprint: `render.yaml`. Custom domain via Cloudflare CNAME.

---

## API reference

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/auth/login` | None | Authenticate, receive JWT |
| GET | `/templates` | JWT | List tenant-scoped templates |
| GET | `/templates/{id}/blank` | JWT | Printable blank template |
| POST | `/intakes` | JWT (Worker) | Upload intake document |
| GET | `/intakes/{id}` | JWT | Check processing status |
| GET | `/review-queue` | JWT (Reviewer) | Documents awaiting review |
| GET | `/reviews/{id}` | JWT (Reviewer) | Fields, confidence, similar cases, audit trail |
| POST | `/reviews/{id}/finalize` | JWT (Reviewer) | Finalize with corrections |
| GET | `/search?q=` | JWT | Full-text search |
| GET | `/cases/{personKey}` | JWT | Case aggregate across documents |
| GET | `/healthz` | None | Service health |
| GET | `/metrics` | JWT | Tenant-scoped counters |

OpenAPI spec: `http://localhost:5100/openapi/v1.json`
