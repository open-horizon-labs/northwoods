# Open Horizons Framework

**The shift:** Action is cheap. Knowing what to do is scarce.

**The sequence:** aim → problem-space → problem-statement → solution-space → execute → ship

Each phase can run as a scoped agent in Claude Code (`.claude/agents/`) to keep context focused by phase. Use `.oh/` session notes to pass decisions and constraints between phases.

**Where to start (triggers):**
- Can't explain why you're building this → `oh-aim` agent (or `/aim`)
- Keep hitting recurring blockers/constraints → `oh-problem-space` agent (or `/problem-space`)
- Framing feels wrong → `oh-problem-statement` agent (or `/problem-statement`)
- About to choose implementation approach → `oh-solution-space` agent (or `/solution-space`)
- Ready to build with bounded scope → `oh-execute` agent (or `/execute`)
- Implementation complete, preparing delivery → `oh-ship` agent (or `/ship`)
- Thrashing/reversals → `/salvage`

**Reflection skills (use anytime):**
- `/review` — alignment and quality check before merge
- `/dissent` — challenge assumptions before one-way doors
- `/salvage` — extract learning before restart
- `/distill` — consolidate recurring patterns and guardrails
---

# Project Context

## Purpose
Northwoods processes intake documents into structured, confidence-scored fields that are reviewed by humans before final acceptance. The system optimizes for reviewer trust, auditability, and tenant-safe workflows.

## Current Aims
- Stabilize local-first extraction quality using staged providers (OCR + normalization).
- Preserve full extraction provenance per field and per run.
- Keep review pipeline fast and reliable while handling low-confidence ambiguity safely.

## Key Constraints
- **Tenant isolation is non-negotiable:** all document, extraction, and audit records must remain tenant-safe.
- **Human-in-the-loop guarantee:** ambiguous or low-confidence data must route to review, not silent auto-accept.
- **Append-only auditability:** extraction attempts must be stored as history, not overwritten.
- **Portable provider design:** provider-specific logic stays behind worker abstractions so stages can be swapped.

## Patterns to Follow
- Use staged extraction providers in `Extraction.Worker` with explicit provider/stage metadata.
- Keep confidence thresholds explicit and centralized (`High`, `ReviewRequired`, `Escalate`).
- Persist run-level attempt metadata (`extraction_run_id`, `technique`, confidence inputs/outputs).
- Prefer deterministic parsing/validation before accepting model output.
- Keep docs/ADR updates in the same change set as behavior shifts.

## Anti-Patterns to Avoid
- Do not bypass review gating for low-confidence fields.
- Do not overwrite prior extraction attempts when reprocessing.
- Do not add provider-specific behavior directly in API contracts.
- Do not commit real secrets (`.env.local` stays local; update `.env.example` for new settings).

## Decision Context
- "Done" means: code builds, targeted runtime behavior is validated, and audit/review invariants hold.
- Non-trivial extraction changes should include proof via local intake runs (upload → worker processing → review payload + attempts).
- ADRs in `docs/ADRs` are the source of truth for architecture-level extraction decisions.

## Toolstack Notes
- Primary stack: .NET services/worker, Postgres, MinIO, web app under `apps/web`.
- Typical local commands:
  - `docker compose up -d postgres minio api worker`
  - `dotnet build src/Northwoods.slnx`
  - `pnpm check`
- Browser validation can use Playwright CLI skill when UI workflow verification is needed.
