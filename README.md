# Northwoods

Northwoods is an intake-processing system for human services workflows. It ingests uploaded intake documents, extracts structured fields with confidence metadata, and routes records into a human-in-the-loop review flow with similar-case assistance.

## What is in this repo

- `src/Services/Northwoods.Api` -- API for intake upload, review, search, and case endpoints
- `src/Workers/Extraction.Worker` -- background extraction pipeline (dual-provider: mock OCR + OpenAI vision)
- `src/BuildingBlocks` -- shared tenancy, storage, and infrastructure utilities
- `apps/web` -- React/TypeScript web UI
- `infra/postgres/init.sql` -- database schema bootstrap with seed data (4 templates, 2 tenants, synthetic case profiles)
- `samples/intakes` -- sample intake PDFs for local validation
- `tests/` -- unit tests (tenancy, worker) and API integration tests
- `docs/` -- architecture rationale, ADRs, self-assessment, fuzz reports

## Quick start (from fresh clone)

### Prerequisites

- Docker + Docker Compose
- .NET SDK 10 (via `mise` or local install)
- Node 22+ and pnpm
- Python 3 (for local OCR helper scripts)

### 1. Start all services

```bash
docker compose up -d
```

This starts Postgres (with pgvector), MinIO, the API, and the extraction worker. Schema and seed data load automatically on first run.

- API: `http://localhost:5100`
- OpenAPI/Swagger: `http://localhost:5100/openapi/v1.json`
- MinIO console: `http://localhost:9001` (northwoods/northwoods)

### 2. Verify health

```bash
curl http://localhost:5100/healthz
# => Healthy
```

### 3. Run the full workflow

```bash
# Login as an intake worker (tenant-a)
TOKEN=$(curl -sS -X POST http://localhost:5100/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"worker@sunrise.example","password":"password","tenantId":"tenant-a"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

# Upload a sample intake
curl -sS -F "file=@samples/intakes/chatgpt-sample-general-intake.pdf" \
  -F "templateId=general-assistance" \
  -H "Authorization: Bearer $TOKEN" \
  http://localhost:5100/intakes
# => {"intakeId":"...","status":0}

# Wait ~10 seconds for extraction, then check status
curl -sS -H "Authorization: Bearer $TOKEN" http://localhost:5100/intakes/{id}

# Login as a reviewer
REV_TOKEN=$(curl -sS -X POST http://localhost:5100/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"reviewer@sunrise.example","password":"password","tenantId":"tenant-a"}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['accessToken'])")

# View review payload (fields, confidence, similar cases, audit trail)
curl -sS -H "Authorization: Bearer $REV_TOKEN" http://localhost:5100/reviews/{id}

# Finalize with corrections
curl -sS -X POST -H "Authorization: Bearer $REV_TOKEN" \
  -H "Content-Type: application/json" \
  http://localhost:5100/reviews/{id}/finalize \
  -d '{"fields":[],"reviewerNote":"Verified"}'

# Search across processed intakes
curl -sS -H "Authorization: Bearer $REV_TOKEN" "http://localhost:5100/search?q=Jamie"

# Case aggregate view
curl -sS -H "Authorization: Bearer $REV_TOKEN" "http://localhost:5100/cases/Jamie%20Carter"
```

### 4. Build checks and tests

```bash
# Build all projects (frontend + backend)
pnpm check

# Run unit tests
dotnet test tests/Northwoods.Tenancy.UnitTests/
dotnet test tests/Northwoods.Worker.UnitTests/

# Run integration tests (requires running Docker stack)
dotnet test tests/Northwoods.Api.IntegrationTests/
```

## Seed data

The database initializes with:

- **2 tenants:** `tenant-a` (Sunrise Agency) and `tenant-b` (Lakewood Services)
- **6 users:** worker + reviewer + admin per tenant (password: `password`)
- **4 templates per tenant:** General Assistance, Housing Stability, Financial Assistance, Clinical SOAP Note
- **4 synthetic case profiles** with embeddings for similar-case retrieval

All data is tenant-isolated via RLS policies.

## Extraction pipeline

The worker runs a dual-provider extraction model with per-field confidence and append-only attempt history.

Providers:
1. **Mock OCR** -- deterministic field extraction for demo reliability
2. **OpenAI Vision** (optional) -- sends document image to gpt-5.4-nano via Responses API with structured field extraction and per-field confidence

Each provider's results are stored as separate extraction attempts. The pipeline selects the best candidate per field based on confidence.

Key confidence thresholds:
- `>= 0.90` auto-acceptable
- `0.75 - 0.90` warning/review band
- `< 0.75` review-required

All extraction attempts are persisted with run-level metadata (`extraction_run_id`, provider, stage, technique, confidence, token usage where applicable).

### Enabling OpenAI Vision

```bash
cp .env.example .env.local
# Set in .env.local:
# OPENAI_API_KEY=sk-...
# Extraction__UseOpenAiVision=true
```

Then pass the env file to the worker container or run the worker locally.

## Environment configuration

Use `.env.example` as the template:

```bash
cp .env.example .env.local
```

Notable flags:
- `Extraction__UsePaddleOcr=true|false`
- `Extraction__UseOpenAiVision=true|false`
- `Extraction__UseOpenAiNormalizer=true|false`
- `OPENAI_API_KEY=...`
- `Extraction__MaxRetryAttempts=3`

## Live demo

A deployed instance is running at **https://northwoods.muness.com**.

### Demo credentials

| Email | Password | Tenant | Role |
|---|---|---|---|
| worker@sunrise.example | password | Sunrise (tenant-a) | Intake Worker |
| reviewer@sunrise.example | password | Sunrise (tenant-a) | Reviewer |
| worker@lakewood.example | password | Lakewood (tenant-b) | Intake Worker |
| reviewer@lakewood.example | password | Lakewood (tenant-b) | Reviewer |

Login as an **Intake Worker** to reach the upload dashboard (mobile-first, template select + file attach + status polling).
Login as a **Reviewer** to reach the review queue (desktop-optimized, confidence indicators, similar-case panel, finalize action).

### Developer scaffold

The developer scaffold (preset login buttons, raw API explorer) is not linked from the main UI. Navigate directly to:

```
https://northwoods.muness.com/#dev
```

### Deployment topology

Hosted on [Render](https://render.com). Blueprint in `render.yaml`.

| Service | Description |
|---|---|
| northwoods-api | .NET API (Docker, Render Web Service) |
| northwoods-worker | .NET extraction worker (Docker, Render Background Worker) |
| northwoods-minio | MinIO S3-compatible object store (Docker, Render Web Service) |
| northwoods-web | React/nginx frontend (Docker, Render Web Service) |
| northwoods-db | Render Managed Postgres 18 |

Custom domain `northwoods.muness.com` is a Cloudflare CNAME pointing to the Render frontend service.
OpenAI vision extraction is enabled in the live environment (`Extraction__UseOpenAiVision=true`, `gpt-5.4-nano`).
Paddle OCR is disabled in production (local Python dependency not available in the Docker image).

Deployment notes and obstacles encountered during the initial Render setup are in `.oh/2026-03-27-morning-sprint.md` under "Render Deployment (Issue #35)".

## Observability

- API and worker emit structured JSON logs with scope metadata.
- API generates/echoes `X-Correlation-Id` per request and persists `correlation_id` into `audit_events`.
- `GET /metrics` returns tenant-scoped counters: request count, review finalization count, extraction success/failure counts.
- `GET /healthz` is the health endpoint for service readiness/liveness.

## API endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/auth/login` | Authenticate and receive JWT |
| GET | `/templates` | List tenant-scoped templates |
| GET | `/templates/{id}/blank` | Download printable blank template |
| POST | `/intakes` | Upload intake document |
| GET | `/intakes/{id}` | Check intake processing status |
| GET | `/review-queue` | List documents awaiting review |
| GET | `/reviews/{id}` | Review payload with fields, confidence, similar cases |
| POST | `/reviews/{id}/finalize` | Finalize with corrections |
| GET | `/search?q=` | Search processed intakes |
| GET | `/cases/{personKey}` | Aggregate case view across documents |
| GET | `/metrics` | Tenant-scoped service metrics |
| GET | `/healthz` | Health check |

Full OpenAPI spec: `http://localhost:5100/openapi/v1.json`

## Related docs

- [Architecture Rationale](docs/architecture.md)
- [Self-Assessment](docs/self-assessment.md)
- [ADR 001: Postgres hybrid retrieval with pgvector and pg_trgm](docs/ADRs/001-postgres-hybrid-retrieval-with-pgvector-and-pg-trgm.md)
- [ADR 002: Temporal for document processing workflows](docs/ADRs/002-temporal-for-document-processing-workflows.md)
- [ADR 003: MinIO for S3-compatible document storage](docs/ADRs/003-minio-for-s3-compatible-document-storage.md)
- [ADR 004: Shared Postgres tenancy with RLS backstop](docs/ADRs/004-shared-postgres-tenancy-with-rls-backstop.md)
- [ADR 005: Portable multi-stage consensus extraction pipeline](docs/ADRs/005-portable-consensus-extraction-pipeline.md)
- [AI Development Tooling Used](docs/ai-tooling.md)
- [Reviewer Rubric](docs/reviewer-rubric.md)
