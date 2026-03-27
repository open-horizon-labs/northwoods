# Northwoods

Northwoods is an intake-processing system for human services workflows. It ingests uploaded intake documents, extracts structured fields with confidence metadata, and routes records into a human-in-the-loop review flow.

## What is in this repo

- `src/Services/Northwoods.Api` — API for intake upload, status, and review endpoints
- `src/Workers/Extraction.Worker` — background extraction pipeline (local OCR + normalization stages)
- `src/BuildingBlocks` — shared tenancy, storage, and infrastructure utilities
- `apps/web` — web UI
- `infra/postgres/init.sql` — database schema bootstrap
- `samples/intakes` — sample intake PDFs for local validation
- `docs/ADRs` — architectural decisions

## Local development

### Prerequisites

- Docker + Docker Compose
- .NET SDK (via `mise` or local install)
- Node + pnpm
- Python 3 (for local OCR helper scripts)

### Start core services

```bash
docker compose up -d postgres minio api worker
```

API is available at `http://localhost:5100`.

### Build checks

```bash
pnpm check
```

This runs:
- `pnpm --dir apps/web build`
- `dotnet build src/Northwoods.slnx`

## Extraction pipeline (current)

The worker runs a staged extraction model with per-field confidence and append-only attempt history.

Current local-first path:
1. OCR stage (PaddleOCR-capable provider path)
2. Optional normalization stage (OpenAI mini)
3. Consensus + confidence gating into review-ready output

Key confidence thresholds:
- `>= 0.90` auto-acceptable confidence band
- `0.75 - 0.90` warning/review band
- `< 0.75` review-required

All extraction attempts are persisted with run-level metadata (`extraction_run_id`, provider, stage, technique, confidence values).

## Environment configuration

Use `.env.example` as the template and copy into local secret config:

```bash
cp .env.example .env.local
```

Fill secrets in `.env.local` (do not commit secrets).

Notable flags:
- `Extraction__UsePaddleOcr=true|false`
- `Extraction__UseOpenAiNormalizer=true|false`
- `OPENAI_API_KEY=...`

## API smoke flow

1. Upload intake file:
   - `POST /intakes` with multipart file + `templateId`
2. Poll intake status:
   - `GET /intakes/{id}`
3. Fetch review payload:
   - `GET /reviews/{id}`

## Related docs

- `docs/ADRs/005-portable-consensus-extraction-pipeline.md`
- `docs/ai-tooling.md`
- `.oh/ocr-confidence-tiering.md`
