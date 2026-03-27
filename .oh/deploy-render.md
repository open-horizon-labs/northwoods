# Session: deploy-render

## Aim
**Updated:** 2026-03-27

**Aim:** Make the Northwoods system publicly accessible at northwoods.muness.com so Banyan reviewers can interact with a live running instance rather than cloning and running locally.

**Current State:** System runs only via local Docker Compose. Reviewers must clone, install toolchain, and docker compose up.
**Desired State:** northwoods.muness.com serves the frontend and proxies API calls to a Render-hosted backend with Postgres, MinIO (or equivalent), and the extraction worker.

## Problem Statement
**Updated:** 2026-03-27

**Current framing:** We need to deploy to Render with a custom domain.

**Reframed as:** Reviewers need a live URL they can use to evaluate the system without local setup, because the assignment deliverables include "Instructions to run the application locally" but a live demo dramatically reduces friction and demonstrates operational credibility.

**The shift:** From "deploy somewhere" to "make the evaluation experience frictionless while proving the system actually runs."

### Constraints
- **Hard:** Domain is northwoods.muness.com on Cloudflare DNS. Render hosts the services. Postgres must be available (Render managed or external). MinIO must have an equivalent (Render disk, S3, or Cloudflare R2).
- **Soft:** Extraction worker needs OpenAI API key in Render env. PaddleOCR may not run easily on Render (Python + large model files); can fall back to OpenAI-only extraction for deployed instance.

## Solution Space
**Updated:** 2026-03-27

## Solution Space Analysis

**Problem:** Deploy the full Northwoods stack (API, worker, Postgres, object storage, frontend) to Render with a custom domain on Cloudflare.
**Key Constraint:** Render's free/starter tier constraints, PaddleOCR portability, and keeping the deployed instance credible for evaluation.

### Candidates Considered

| Option | Level | Approach | Trade-off |
|--------|-------|----------|-----------|
| A | Band-Aid | Deploy only the frontend as a static site, API stays local | Reviewer cannot interact with real backend |
| B | Local Optimum | Deploy API + worker + frontend on Render, use Render Postgres, skip MinIO (use local filesystem or Render disk) | Simpler but file storage is fragile and non-portable |
| C | Reframe | Deploy full stack on Render: API as web service, worker as background worker, Render Postgres, Cloudflare R2 for object storage, frontend as static site | Production-credible, portable, but more setup |
| D | Redesign | Deploy via Docker Compose on a single Render instance or Railway/Fly.io with full Docker support | Closest to local experience, but Render doesn't natively support multi-container compose |

### Evaluation

**Option A: Frontend-only static deploy**
- Solves stated problem: No (no backend for reviewers to interact with)
- Implementation cost: Low
- Maintenance burden: Low
- Second-order effects: Undermines the "operational credibility" rubric area

**Option B: Render services + Render disk for files**
- Solves stated problem: Partially
- Implementation cost: Medium
- Maintenance burden: Medium (disk is ephemeral on free tier)
- Second-order effects: File uploads may be lost on redeploy; extraction worker may struggle with PaddleOCR dependencies

**Option C: Render services + Cloudflare R2 for files**
- Solves stated problem: Yes
- Implementation cost: Medium-High
- Maintenance burden: Medium
- Second-order effects: R2 is S3-compatible so ObjectStore works with minimal config change; more resilient than Render disk

**Option D: Full Docker Compose on Fly.io or Railway**
- Solves stated problem: Yes
- Implementation cost: High (different platform, new setup)
- Maintenance burden: High (another platform to manage)
- Second-order effects: Overkill for a demo deployment

### Recommendation

**Selected:** Option C — Render services + Cloudflare R2
**Level:** Reframe

**Rationale:**
- Render handles .NET web services and background workers natively
- Render Postgres is managed and easy
- Cloudflare R2 is S3-compatible (our ObjectStore already speaks S3)
- Frontend deploys as a Render static site
- Custom domain via Cloudflare DNS is straightforward
- PaddleOCR can be skipped on deployed instance — use OpenAI nano vision only for extraction (simpler, no Python dependency in container)

**Accepted trade-offs:**
- Deployed instance uses OpenAI-only extraction (no local PaddleOCR)
- R2 requires Cloudflare API token for bucket creation
- Free tier Render services spin down after inactivity (acceptable for demo)

### Implementation Notes

**Services to create on Render:**
1. **northwoods-api** — Web Service (.NET), port 8080
2. **northwoods-worker** — Background Worker (.NET)
3. **northwoods-web** — Static Site (Vite build output from apps/web/dist)
4. **Render Postgres** — Managed database with pgvector extension

**Object storage:**
- Cloudflare R2 bucket `northwoods-intakes`
- S3-compatible endpoint, same ObjectStore code
- Or: Render disk as simpler fallback if R2 setup is too heavy

**DNS:**
- CNAME `northwoods.muness.com` → Render static site URL
- API proxy: either Render's built-in routing or frontend proxy rewrite

**Environment variables on Render:**
- `ConnectionStrings__Default` → Render Postgres connection string
- `Minio__Endpoint` → R2 endpoint (or Render disk path)
- `OPENAI_API_KEY` → from Render secret
- `Extraction__UsePaddleOcr=false`
- `Extraction__UseOpenAiVision=true`

**Deployment approach:**
- Use Render's native Git deploy (connect GitHub repo)
- API and worker use Dockerfiles already in repo
- Frontend uses `pnpm --dir apps/web build` and serves `apps/web/dist`
