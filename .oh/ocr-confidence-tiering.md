## Aim
**Updated:** 2026-03-26

## Problem Statement
**Problem:** Extraction is currently simulated, so we need a real pipeline that turns uploaded handwritten intake images/PDFs into confidence-scored structured fields suitable for fast human review.
**Constraint:** The slice must keep tenant isolation, status transitions, and auditability intact while improving extraction accuracy; it should support escalation logic without breaking the current review-ready workflow.

## Solution Space Analysis

### Candidates Considered

| Option | Level | Approach | Trade-off |
|--------|-------|----------|-----------|
| A | Band-Aid | Keep mocked extraction and only improve confidence display heuristics in the UI | Fastest; no extraction capability delivered. Useful only for demos, not production trust. |
| B | Local Optimum | Replace mock with single-engine Tesseract extraction and tune post-processing | Low implementation risk and low cost, but limited structure/quality for handwritten/complex forms. |
| C | Reframe | Implement a staged pipeline: local Tesseract for all docs, escalate low-confidence segments to Azure Form Recognizer, then optionally a small OpenAI normalizer with strict schema fallback | Medium–high complexity, highest quality path, introduces deterministic escalation + auditability by stage. |
| D | Redesign | Replace worker extraction with provider-agnostic ML extraction service with dynamic model routing and async reconciliation | Most robust long-term architecture, but highest complexity and coordination cost for early-stage slice. |

## Evaluation

### Option A: Keep mocked extraction and tune UI confidence
- Solves stated problem: **No** (does not remove the core gap)
- Implementation cost: **Very low**
- Maintenance burden: **Low (now), high later**
- Second-order effects: Risks hard-coding fake behavior into contract and delaying the real extraction decision.

### Option B: Single Tesseract stage only
- Solves stated problem: **Partially**
- Implementation cost: **Low-Medium**
- Maintenance burden: **Medium**
- Second-order effects: Lower operational cost and predictable latency, but poor resilience on low-quality handwriting, poor field normalization consistency.

### Option C: Tesseract → Azure escalation → OpenAI normalizer (recommended)
- Solves stated problem: **Yes**
- Implementation cost: **Medium-High**
- Maintenance burden: **Medium**
- Second-order effects: Better precision/recall trade-off, clear stage-level audit trail, explicit escalation policy, and explicit “human-in-loop” guarantees on ambiguity.

### Option D: Full retrieval-aware extraction service redesign
- Solves stated problem: **Yes**
- Implementation cost: **High**
- Maintenance burden: **High**
- Second-order effects: Great extensibility, but risk of delaying core slice and coupling extraction architecture before the current end-to-end workflow is fully stable.

## Recommendation

**Selected:** Option C — Multi-stage staged extraction
**Level:** Reframe

**Rationale:**
- Delivers real extraction capability quickly while acknowledging real-world failure modes.
- Keeps cost bounded by escalating only ambiguous content rather than every file.
- Produces strong evidence for trust: stage provenance, provider identity, and per-field confidence at each stage.
- Preserves current domain boundary of the review-ready loop and adds depth where it matters (extraction reliability).

### Accepted trade-offs
- Additional complexity in the worker and data model (`extraction_stage`, `provider`, `stage_confidence`, `escalation_reason`, attempt counters).
- Additional operational dependencies on Azure/OpenAI credentials and budget governance.

## Implementation Notes (next run)
- Add a unified extractor abstraction (`IExtractor` with `TryExtract` result: `fields`, `confidence`, `provider`, `latencyMs`, `stageMetadata`).
- Preserve existing tenant-safe intake IDs and status transitions.
- Gate automatically on confidence policy: auto-accept high confidence, force-review medium, escalate low confidence.
- Keep `OpenAI` behind a strict schema/JSON validation layer; reject unparseable responses and fall back to review-required.
- Keep audit rows append-only with explicit provider/stage for each extraction attempt.
- Add an optional fixture harness with 3–5 sample intake files + expected field outputs.
- Decision recorded in `docs/ADRs/005-portable-consensus-extraction-pipeline.md`.

## Execute
**Updated:** 2026-03-26
**Status:** complete
- Implemented extraction worker refactor to a staged provider pipeline with escalation and consensus merge, now with a real PaddleOCR-capable provider path (`Extraction__UsePaddleOcr=true`).
- Added `extraction_attempts` table and indexes in `infra/postgres/init.sql`.
- Added RLS + grants for `extraction_attempts`.
- Build verification completed: `dotnet build src/Northwoods.slnx`.
- Runtime validation completed locally with Paddle mode: uploaded tenant-a text intakes, ran worker with Paddle enabled, and verified parsed fields (including corrected `monthlyIncome=$2400`) persisted through review endpoints.

- Added object download support in `ObjectStore` and Python helper script (`scripts/paddle_extract.py`) plus requirements template for local OCR dependencies.
- Fixed extraction attempt persistence to be append-only across retries/reprocessing by adding `extraction_run_id` and preserving run history instead of upserting-overwriting previous attempts.
- Added explicit `technique` capture per attempt (e.g. `paddleocr+label-regex`, `openai-mini-json-normalize`) for auditability.
- Verified idempotent reprocessing now stores multiple runs (`count(distinct extraction_run_id)` increases across reruns) without unique-key collisions.