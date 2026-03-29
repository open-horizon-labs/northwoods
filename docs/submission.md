# Northwoods -- Submission Package

**Handwritten Intake Document Processor**
.NET 10 / React + TypeScript / Postgres 18 with pgvector

> **TL;DR** -- Live at [northwoods.muness.com](https://northwoods.muness.com). Login popup pre-fills credentials. Upload scanned forms, AI extracts fields with confidence scores, reviewers correct and finalize with similar-case RAG assistance. 184-document synthetic corpus, 5-signal hybrid retrieval (vector + FTS + trigram + structured), row-level security on all tables. Built with Claude Code + Open Horizons workflow orchestration + CodeRabbit automated review.

---

## Table of Contents

1. [Quick Start](#1-quick-start)
2. [RAG Report -- Self-Assessment Tool](#2-rag-report----self-assessment-tool)
3. [Seed Corpus Creation Process](#3-seed-corpus-creation-process)
4. [Technical Overview](#4-technical-overview)
   - 4a. [RAG Technique Overview](#4a-rag-technique-overview)
   - 4b. [Similarity Matching Query Overview](#4b-similarity-matching-query-overview)
   - 4c. [AI Tooling Overview](#4c-ai-tooling-overview)
   - 4d. [Architecture Overview](#4d-architecture-overview)
5. [Deployment Infrastructure](#5-deployment-infrastructure)
6. [Multi-Tenancy Strategy](#6-multi-tenancy-strategy)
7. [What's Complete vs. What's Missing](#7-whats-complete-vs-whats-missing)

---

## 1. Quick Start

**GitHub:** [github.com/open-horizon-labs/northwoods](https://github.com/open-horizon-labs/northwoods)
(Repository access will be granted before sending.)

**Live demo:** [northwoods.muness.com](https://northwoods.muness.com)
(Hosted on Render -- API, Worker, Web, Postgres, and MinIO all deployed via `render.yaml`.)

**Login credentials** are pre-fillable using the popup on the login page. For manual entry:

| Role | Email | Password | Tenant |
|------|-------|----------|--------|
| Intake Worker | worker@sunrise.example | password | tenant-a (Sunrise) |
| Reviewer | reviewer@sunrise.example | password | tenant-a (Sunrise) |
| Intake Worker | worker@lakewood.example | password | tenant-b (Lakewood) |
| Reviewer | reviewer@lakewood.example | password | tenant-b (Lakewood) |

**Suggested evaluation path:**

1. Log in as **reviewer@sunrise.example** to see the review queue with confidence-flagged fields and similar case panels.
2. Open the **RAG Report** tab (or navigate to `/#rag-report`) to see the automated retrieval evaluation.
3. Switch to **worker@lakewood.example** to verify tenant isolation -- you should see only Lakewood data.
4. Visit `/#dev` for the developer scaffold with preset login buttons and raw API access.

**Run locally:**

```bash
git clone https://github.com/open-horizon-labs/northwoods.git
cd northwoods
echo "OPENAI_API_KEY=sk-..." > .env     # required for extraction + embeddings
docker compose up -d                      # Postgres 18 + pgvector, MinIO, API, Worker, Web
open http://localhost:5173                 # React frontend
```

The API serves OpenAPI docs at `http://localhost:5100/scalar/v1` and raw spec at `/openapi/v1.json`.

---

## 2. RAG Report -- Self-Assessment Tool

The RAG Report is a built-in evaluation page that tests whether the similar-case retrieval pipeline actually works. It lives in the reviewer navigation at `/#rag-report` and runs live queries against the deployed system -- nothing is canned.

The page defines a set of **arc queries**, each representing a known relationship from the synthetic corpus. For example, Raymond Castillo (P019) is a frequent flyer at the Sunrise facility with 12 visits spanning two form eras. The report searches for his name, finds his most recent document, fetches similar cases from the review endpoint, and checks whether his other documents appear in the top 5. Similarly, Gloria Navarro (P039) plays the same role at the Lakewood facility. Carlton Hughes (P017) tests cross-facility transfer -- he moved from Sunrise to Lakewood, so his cases should appear together within the correct tenant boundary.

The report also includes a **tenant isolation check**: Brianna Kowalski (P004) is a Sunrise-only client. When the system retrieves similar cases for her, no Lakewood person keys should appear. If they do, the page flags a tenant isolation violation with a red badge. Each query card shows pass/fail status, the anchor document used, and a table of returned similar cases with match scores, template types, and whether each result was expected. The overall summary shows pass/fail counts so a reviewer can see at a glance whether the RAG pipeline is producing meaningful, tenant-safe results.

This page exists because it is one thing to claim "RAG works" and another to prove it with repeatable, inspectable evidence. The report runs on every deployment and gives both the developer and the evaluator a concrete answer to "does similar-case retrieval actually find what it should?"

**File:** `apps/web/src/pages/RagReportPage.tsx`

---

## 3. Seed Corpus Creation Process

The corpus represents 40 fictional people across two tenants (Sunrise and Lakewood), with a long-tail visit distribution: 25 people with 1 visit, 8 with 2-3, 5 with 4-6, and 2 frequent flyers with 10+ visits. Each person has a narrative arc that progresses across two form eras (v2: older county forms from June-September 2025, v1: new agency forms from December 2025-March 2026).

### Step 1: People roster and narrative design

`scripts/corpus/people.py` defines all 40 people with demographics, addresses, tenant assignments, and visit counts. `scripts/corpus/narrative.py` defines the narrative arcs -- what changes between v2 and v1 for each person. Raymond Castillo goes from homeless with active substance use to a room rental with part-time income. Carlton Hughes transfers from Sunrise to Lakewood after finding work. Five people are v2-only (discharged or lost to follow-up), five are v1-only (new intakes after the form changeover).

### Step 2: PDF generation (8 scripts)

Each of the 4 form types has two generation scripts -- one per era:

| Template | v2 (county form style) | v1 (new agency style) |
|----------|----------------------|---------------------|
| General Assistance | `gen_general_v2.py` | `gen_general.py` |
| Housing Stability | `gen_housing_v2.py` | `gen_housing.py` |
| Behavioral Health | `gen_behavioral_v2.py` | `gen_behavioral.py` |
| SOAP Progress Note | `gen_soap_v2.py` | `gen_soap.py` |

These scripts use ReportLab to generate realistic PDFs with handwriting-style fonts, ink color variation, occasional cross-outs and corrections, and intentionally left-blank fields. The v2 forms use a different visual layout than v1 -- mimicking a real-world form template changeover. Each person's field values are drawn from their narrative arc state for that era.

### Step 3: Collection and SQL generation

`scripts/corpus/collect_corpus.py` gathers all generated PDFs from staging directories into `samples/corpus/{sunrise,lakewood}/`.

`scripts/corpus/generate_seed_sql.py` produces `infra/postgres/seed_corpus.sql` -- the SQL that inserts:
- **documents** (one per form, with status `finalized`, tenant-scoped)
- **extracted_fields** (realistic field values with computed confidence scores -- structured fields get higher confidence, narrative fields get lower, v2 forms are slightly noisier than v1)
- **case_profiles** (search text composed from field values and demographics; embedding column is NULL)

### Step 4: Embedding generation at runtime

The seed SQL intentionally leaves `embedding` as NULL. When the extraction worker processes a document (or when `reset_demo.py` uploads corpus PDFs through the intake API), the worker generates embeddings using OpenAI `text-embedding-3-small` (1536 dimensions) and stores them in the `case_profiles.embedding` column -- a `VECTOR(1536)` column indexed with HNSW for cosine similarity.

### Step 5: Demo reset

`scripts/reset_demo.py` provides idempotent demo setup: it cleans all documents for both tenants, then uploads all 184 corpus PDFs through the real intake API (`POST /intakes`). The worker picks them up and processes them through the full extraction pipeline.

```
python3 scripts/reset_demo.py --api https://northwoods-api.onrender.com
```

The full pipeline: **people roster** -> **narrative arcs** -> **PDF generation** -> **SQL seed** -> **API upload** -> **extraction + embedding** -> **searchable vector store**.

---

## 4. Technical Overview

### 4a. RAG Technique Overview

The RAG implementation uses **Postgres as a unified retrieval store** rather than a separate vector database. This was a deliberate architectural decision documented in [ADR 001](ADRs/001-postgres-hybrid-retrieval-with-pgvector-and-pg-trgm.md).

**Embedding strategy:**
- Model: OpenAI `text-embedding-3-small` producing 1536-dimensional vectors
- Embeddings are generated at two points: (1) by the extraction worker after processing a document, and (2) by the API during finalization when the reviewer's corrections update the case profile
- The text embedded is a structured composite -- it includes template ID, finalized field values, OCR segments, reviewer notes, and any discovered fields

**Vector storage:**
- Column: `case_profiles.embedding VECTOR(1536)` in Postgres with pgvector extension
- Index: HNSW with cosine distance operators (`vector_cosine_ops`) for approximate nearest neighbor search
- Tenant-scoped: `case_profiles.tenant_id` + RLS policy ensures embeddings never cross tenant boundaries

**OCR/extraction strategy -- OpenAI Vision:**
The extraction pipeline uses OpenAI's vision-capable models (gpt-5.4-nano, escalating to mini on low confidence) rather than a traditional OCR library. This choice was based on prior successful experience with vision models for document extraction and the advantage of combined OCR + field extraction in a single pass -- the model reads the handwritten form *and* maps values to template fields simultaneously, eliminating a separate OCR-then-parse pipeline. The trade-off is API dependency, mitigated by the append-only extraction attempts design that preserves all provider outputs for auditability.

**Retrieval pipeline -- 5-signal hybrid with Reciprocal Rank Fusion:**

The similar-case query (`FindSimilarCasesAsync` in `ReviewEndpoints.cs`) does not rely on vector similarity alone. It fuses five retrieval signals:

1. **Full-text search (FTS)** -- `tsvector`/`tsquery` over `search_text`
2. **Vector similarity** -- cosine distance via pgvector over embeddings
3. **Name fuzzy match** -- `pg_trgm` similarity on `applicant_name`
4. **Address fuzzy match** -- `pg_trgm` similarity on `address`
5. **DOB exact match** -- exact equality on `date_of_birth`

Each signal produces a ranked list. These are fused using **Reciprocal Rank Fusion**: `score = SUM(1.0 / (60.0 + rank))` across all signals where a document appears. The constant 60 is the standard RRF smoothing parameter.

**Why hybrid instead of vector-only?** In my experience, pure semantic similarity search produces disappointing results for structured case data -- exact name matches routinely fail to surface, and the ranking feels arbitrary to reviewers. Supplementing vector search with full-text search and specific field matching (name, address, DOB) is straightforward to implement and dramatically improves retrieval quality. This pattern proved itself in client work (Mayo Clinic) and in the open-source RNA project. The query-time overhead is minimal -- typically 50-200ms total for all five signals -- and the quality boost has consistently justified the added complexity.

**AI-generated summaries:**
When an OpenAI key is available and AI summaries are enabled, each similar case gets a contextual summary generated by `gpt-5.4-nano`. The prompt provides the current case fields, the matched case fields, and the algorithmic match signals, asking for a 1-2 sentence factual explanation of the relationship. An algorithmic fallback summary is always available if the AI call fails.

### 4b. Similarity Matching Query Overview

The similar-case query (`FindSimilarCasesAsync` in `ReviewEndpoints.cs`) is a single SQL statement built from six CTEs following a **retrieve-then-fuse** pattern:

1. **Target CTE** -- Loads the current document's profile (embedding, search text, name, DOB, address) as the anchor for all comparisons.
2. **Five signal CTEs** -- Each independently ranks candidate documents by one retrieval method (FTS, vector cosine, name trigram, address trigram, DOB exact). Each CTE filters on `tenant_id` so isolation is enforced per-signal, not as a post-filter.
3. **Fusion CTE** -- Unions all five ranked lists and applies Reciprocal Rank Fusion: `score = SUM(1.0 / (60.0 + rank))`. Documents appearing in multiple signals score higher. The output includes a `match_sources` array so the UI can explain *why* a case matched (e.g., "same applicant + semantic match + matching DOB").
The query returns the top N fused results with match scores, which the API enriches with field values and AI-generated or algorithmic summaries.

**Index strategy:** Each signal is backed by a purpose-built index -- GIN for full-text and trigram, HNSW for vector cosine, B-tree for DOB. All indexes are defined in `DatabaseInitializer.cs` and created idempotently on startup.

> Full query source: `src/Services/Northwoods.Api/Endpoints/ReviewEndpoints.cs`

### 4c. AI Tooling Overview

This project was built through an AI-assisted workflow where AI increased speed and context retention while the human retained responsibility for system framing, architectural choices, scope decisions, and trade-off calls.

#### Standard tools

- **Claude Code (Sonnet/Opus)** -- Extended coding sessions, multi-file refactors, execution-heavy phases. Used via Claude Code Max subscription.
- **ChatGPT Pro (gpt-5.4 family)** -- Architectural reasoning, extraction normalization, and coding when Claude was rate-limited. Both subscriptions were used in parallel to maintain throughput during intensive build sessions.
- **CodeRabbit** -- AI-powered PR review integrated into the GitHub workflow. Provides severity-classified findings (Critical, Major, Minor, Trivial) on every PR. During this project, CodeRabbit caught accessibility gaps, performance issues, and security concerns that were fixed before merge.
- **GitHub CodeQL** -- Semantic static analysis for security vulnerabilities and code quality across .NET and TypeScript. Runs on every push and PR via a GitHub-managed workflow.
- **Playwright CLI** -- Automated browser interactions for intake/review flow validation against the running stack.

#### Open Horizons (OH) workflow skills

Open Horizons is a strategic alignment framework that structures how work gets done. It lives in the `.oh/` directory and was used as an MCP server throughout development.

**What OH provided:**

- **Outcomes** (`.oh/outcomes/`): Seven outcome definitions that kept development aligned with what actually matters -- not assignment bullet points, but capability-oriented goals like "bring similar cases into review" and "prove tenant-safe and operable delivery". Each outcome has a mechanism, success signal, and linked files.

- **Guardrails** (`.oh/guardrails/`): Five guardrails enforcing design discipline -- "tenant isolation must be provable", "RAG must improve review judgment", "scope must protect a coherent vertical slice". These acted as automated constraint checks during agent sessions.

- **Decision logging**: Major architectural decisions were logged with reasoning tied to specific outcomes, creating an auditable trail from "why did we choose Postgres for retrieval?" to the ADR that answers it.

- **Aiming and problem framing** (`.oh/cto-judgement.md`): Before writing code, the OH workflow structured the problem space analysis -- constraints, terrain, stakeholders, capability-oriented JTBD workflows, and an explicit X-Y check ("the stated need is the feature checklist; the underlying need is demonstrating CTO-level judgment").

**How OH was used in practice:**

Each GitHub issue was worked end-to-end by a dev-pipeline agent (`.claude/agents/dev-pipeline.md`). The pipeline runs four phases: problem statement, solution space, execute, ship. The `dev-pipeline-oversight` variant adds a fifth phase -- post-merge comment audit that verifies all external review findings (CodeRabbit, `/review`, `/dissent`) were addressed before moving on.

The OH MCP server (`oh-mcp` in `.mcp.json` equivalent) provided context to these agents so they could connect their work to outcomes and guardrails rather than working in isolation.

#### Repo-Native Alignment (RNA) MCP

RNA is a code intelligence tool that makes the repository queryable through semantic search and graph-style relationships. It was configured as an MCP server in `.mcp.json`:

```json
{
  "mcpServers": {
    "rna-mcp": {
      "type": "stdio",
      "command": "repo-native-alignment",
      "args": ["--repo", "."]
    }
  }
}
```

**What RNA provided:**

- **Semantic code search**: Instead of grep, agents could search for concepts like "tenant isolation enforcement" or "embedding generation" and get relevant symbols, files, and documentation ranked by importance.
- **Graph traversal**: `mode: neighbors` and `mode: impact` queries let agents understand call chains, dependency relationships, and blast radius before making changes.
- **Repo map**: A single call to `repo_map` returns the top symbols by importance, subsystem boundaries, hotspot files, active outcomes, and entry points -- giving an agent immediate orientation in an unfamiliar codebase.
- **Cross-artifact search**: RNA indexes code symbols, markdown sections, business artifacts (outcomes, guardrails), and git commits in a single searchable store.

RNA went beyond "find me this function" -- it connected code changes to business outcomes, helping agents understand *why* something exists, not just *where*.

#### Impeccable design skills

A suite of 17+ design-focused skills (`.claude/skills/`) provided systematic UI quality control: `audit` runs diagnostics across accessibility, theming, responsive design, and anti-patterns; `normalize`, `harden`, and `polish` fix issues found by the audit; `frontend-design` provides creative direction and AI-generated-UI detection criteria.

#### How these tools combined

The tools formed a layered operating system for development:

1. **OH** set the strategic direction (what to build and why)
2. **RNA** provided codebase awareness (where things are and how they connect)
3. **Dev-pipeline agents** executed the work (branch, implement, review, ship)
4. **CodeRabbit + CodeQL + /review + /dissent** caught issues the agent missed
5. **Dev-pipeline-oversight** verified nothing slipped through

The human role was scoping issues, reviewing agent output, catching drift, and making architectural calls. AI increased leverage without replacing accountability.

### 4d. Architecture Overview

The system follows a capability-sliced topology with three deployable services sharing a Postgres database:

- **Northwoods.Api** (.NET 10) -- Handles auth, intake uploads, review workflow, search, case aggregation, and similar-case retrieval
- **Extraction.Worker** (.NET 10) -- Background service that polls for uploaded documents and runs the extraction pipeline
- **Web** (React + Vite + Tailwind) -- Single-page application with role-based dashboards

**Data layer:**
- **Postgres 18** with pgvector, pg_trgm, and full-text search extensions -- stores all relational data, extracted fields, audit events, case profiles, and embeddings
- **MinIO** (S3-compatible) -- stores original uploaded document blobs

![Northwoods Architecture](architecture-overview.svg)

> d2 source: [`docs/architecture-overview.d2`](architecture-overview.d2) -- render with `d2 docs/architecture-overview.d2 docs/architecture-overview.svg`

**Key flows:**

| Flow | Path |
|------|------|
| Upload | Worker dashboard -> `POST /intakes` (multipart) -> store blob in MinIO -> insert document row (status: `uploaded`) |
| Extract | Worker polls `WHERE status = 'uploaded'` -> downloads blob -> runs OpenAI Vision (nano, escalates to mini on low confidence) -> persists `extracted_fields` + `extraction_attempts` + `case_profiles` -> generates embedding -> sets status to `review_ready` or `completed` |
| Review | Reviewer opens queue -> `GET /reviews/{id}` returns fields, confidence, source URL, similar cases, audit trail -> reviewer corrects low-confidence fields -> `POST /reviews/{id}/finalize` persists corrections, regenerates embedding, records audit events |
| Search | `GET /search?q=...` uses FTS + trigram fuzzy matching over `case_profiles`, tenant-scoped |

---

## 5. Deployment Infrastructure

### CI Pipeline (`.github/workflows/ci.yml`)

Every push to `main` and every PR runs 5 parallel checks:

| Check | What it does |
|-------|-------------|
| **Build** | .NET 10 solution restore + build, Node 24 + pnpm web app build |
| **Test** | Unit tests (Tenancy, Worker), integration tests on main (requires live Postgres) |
| **Playwright Smoke Tests** | Headless Chromium e2e tests against the frontend (Playwright 1.58) |
| **ADR Compliance** | Python scripts verify RLS policies on all tenant-scoped tables (ADR 004) and append-only extraction attempts (ADR 005) |
| **Secret Scan** | Bash script scans for committed secrets/credentials |

### Code Quality (GitHub-managed)

- **CodeQL** -- GitHub's semantic code analysis runs on every push and PR via a separate CodeQL workflow. Performs static analysis for security vulnerabilities and code quality issues across the .NET and TypeScript codebases.
- **CodeRabbit** -- AI-powered PR review bot integrated into the GitHub workflow. Provides severity-classified findings (Critical, Major, Minor, Trivial) on every PR. During this project, CodeRabbit caught accessibility gaps (missing ARIA attributes, keyboard handlers), performance issues (listener churn, missing `useCallback`), and security concerns (unhandled `decodeURIComponent` exceptions).

### Deploy Pipeline (`.github/workflows/deploy.yml`)

Triggered on `v*` tags (e.g., `git tag v0.7.0 && git push origin v0.7.0`):

1. **Build + Push** -- Three parallel matrix jobs build Docker images for API, Worker, and Web using multi-stage Dockerfiles, push to `ghcr.io/open-horizon-labs/northwoods-{api,worker,web}:latest`
2. **Deploy** -- Updates Render service image references via the Render API, then triggers deploys for all 3 services
3. **Smoke Test** -- Waits for services to come up (up to 5 min for Render free tier), then runs `scripts/production-smoke.sh` against the live URL
4. **Release** -- Creates a GitHub Release with auto-generated release notes

### Render Topology (`render.yaml`)

| Service | Type | Image Source |
|---------|------|-------------|
| `northwoods-api` | Web Service | `ghcr.io/.../northwoods-api:latest` |
| `northwoods-worker` | Background Worker | `ghcr.io/.../northwoods-worker:latest` |
| `northwoods-web` | Static Site (nginx) | `ghcr.io/.../northwoods-web:latest` |
| `northwoods-db` | Postgres 18 | Render managed (pgvector enabled) |
| `northwoods-minio` | Private Service | MinIO for document blob storage |

Custom domain: `northwoods.muness.com` via Cloudflare DNS.

### Docker Multi-Stage Builds

Each Dockerfile uses a two-stage pattern:
- **Build stage**: Full SDK image for restore/build/publish
- **Runtime stage**: Minimal runtime image (aspnet for .NET services, nginx:alpine for web)

This keeps production images lean while preserving full build tooling in CI.

---

## 6. Multi-Tenancy Strategy

**Model:** Shared Postgres tables with `tenant_id` on every tenant-scoped record, enforced at two layers.

**Application layer:**
- JWTs carry `tenantId` and `role` claims issued at login
- Every API endpoint extracts tenant context from the authenticated user
- The `DbConnectionFactory` opens a session that sets `SET LOCAL app.tenant_id = '{tenantId}'` on each transaction
- Every SQL query includes explicit `WHERE tenant_id = @TenantId` clauses
- The extraction worker reads `tenant_id` from the document record it picks up

**Database layer (RLS backstop):**
- Row-Level Security is enabled on all 7 data tables: `users`, `templates`, `documents`, `extracted_fields`, `case_profiles`, `extraction_attempts`, `audit_events`
- Each table has an identical policy: `USING (tenant_id = current_setting('app.tenant_id', true))`
- The `app_user` role has no `BYPASSRLS` privilege
- This means even if application code misses a tenant filter, the database will not return cross-tenant rows

```sql
-- From DatabaseInitializer.cs
ALTER TABLE documents ENABLE ROW LEVEL SECURITY;
CREATE POLICY documents_tenant_isolation ON documents
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
```

**Vector search respects tenant boundaries:** Every CTE in the similarity query filters on `cp.tenant_id = t.tenant_id`. The HNSW index covers all tenants, but the query filters are applied before results are returned. The RAG Report page includes an explicit tenant isolation test case to verify this works.

**Automated compliance:** `scripts/ci/check-rls-compliance.py` runs in CI and verifies that every table with a `tenant_id` column has RLS enabled and a tenant isolation policy.

**Design rationale:** See [ADR 004](ADRs/004-shared-postgres-tenancy-with-rls-backstop.md).

---

## 7. What's Complete vs. What's Missing

### Complete and functioning

| Capability | Evidence |
|------------|----------|
| JWT auth with two roles (Worker, Reviewer) + Admin | `src/Services/Northwoods.Api/Endpoints/AuthEndpoints.cs` |
| 4 form templates per tenant with blank PDF download | `AdminTemplates.tsx`, template CRUD endpoints |
| Document upload with template association | `POST /intakes`, Worker dashboard |
| Background extraction with OpenAI Vision (nano -> mini escalation) | `ExtractionBackgroundService.cs`, `OpenAiVisionProvider.cs` |
| Per-field confidence scoring with review routing | Fields below 0.75 flagged `requires_review` |
| Human-in-the-loop review with corrections and finalization | `ReviewEndpoints.cs`, `ReviewDetail.tsx` |
| Append-only audit trail with correlation IDs | `audit_events` table, every state transition logged |
| Full-text + fuzzy search across intakes | `SearchEndpoints.cs` |
| Case aggregate view by person | `GET /cases/{personKey}` |
| 5-signal hybrid RAG with RRF fusion | Vector + FTS + name + address + DOB signals |
| AI-generated similar-case summaries | `gpt-5.4-nano` contextual summaries with algorithmic fallback |
| Multi-tenancy with RLS on all tables | 7 tables, automated compliance checks |
| 184-document synthetic corpus with narrative arcs | 40 people, 2 tenants, 2 form eras |
| RAG pipeline self-assessment page | `RagReportPage.tsx` |
| Structured logging, correlation IDs, health checks, metrics | `/healthz`, `/metrics`, `X-Correlation-Id` |
| Retry with backoff on transient extraction failures | Configurable max attempts and delay |
| Docker Compose for local development | `docker-compose.yml` |
| Render deployment for live demo | `render.yaml`, live at northwoods.muness.com |
| OpenAPI documentation | Scalar UI at `/scalar/v1` |
| 5 ADRs documenting key decisions | `docs/ADRs/` |
| Unit tests (17+), integration tests, CI compliance checks | `tests/` directory |
| CI/CD pipeline with 5 PR checks + tag-triggered deploy | `.github/workflows/ci.yml`, `deploy.yml` |
| CodeQL static analysis + CodeRabbit AI PR review | GitHub-managed workflows |
| Production smoke tests post-deploy | `scripts/production-smoke.sh` |

### Known gaps

- **Production auth hardening**: bcrypt hashing is in place, but token refresh, rate limiting, and account lockout are not implemented.
- **OpenAI key required**: The worker requires `OPENAI_API_KEY` for both extraction (Vision) and embedding generation. Without it, the worker will not start and no documents will process. This is documented in the README.
- **Playwright e2e tests**: Browser smoke tests run in CI against the frontend only (no backend services in the test environment).
- **Advanced reranking**: The RRF fusion is effective but a cross-encoder reranker (stage 4 in ADR 001) was not implemented.
- **Temporal deferral**: ADR 002 proposed Temporal for workflow orchestration. This was intentionally deferred in favor of a simpler poll-based worker, which proved sufficient for the exercise scope.

### Design trade-offs

1. **Postgres for everything** -- Vector search, FTS, trigram, and relational data all in one store. Simpler deployment and tenant isolation story, at the cost of retrieval workload sharing infrastructure with transactional data.
2. **Single API service** -- The solution space analysis considered splitting into separate Identity, Intake, and Review services. A single API with clean internal module boundaries was chosen for delivery pragmatism while preserving the capability separation in code organization.
3. **Append-only extraction attempts** -- More storage but clearly improves trust and auditability. Reviewers can see per-provider breakdowns and understand *why* the system is uncertain.
4. **Synthetic corpus over real data** -- 40 fictional people with hand-designed narrative arcs let us demonstrate meaningful retrieval patterns (longitudinal care, facility transfers, tenant isolation) that random data would not produce.

---

*Full documentation index: [README.md](../README.md) | [Architecture](architecture.md) | [AI Tooling](ai-tooling.md) | [ADRs](ADRs/)*
