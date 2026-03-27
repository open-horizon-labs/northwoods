# ADR 005: Use a portable multi-stage consensus extraction pipeline

- **Status:** Accepted
- **Date:** 2026-03-26

## Context

The current extraction implementation is simulation-based. For production-use intake review, we need extraction that:

- works on scanned/handwritten documents,
- produces structured fields plus confidence and provenance,
- supports high reviewer trust and auditability,
- can escalate to richer models only when needed,
- and remains operable as model providers evolve.

The product goal is to keep a strong human-in-the-loop workflow: uncertain extractions are surfaced and corrected by reviewers, while high-confidence results can flow with minimal friction.

## Decision

We will implement extraction as a **portable multi-stage pipeline with consensus gating**, rather than a single provider default.

The canonical stages are:

1. **Fast local stage (Tesseract):** baseline OCR pass over full document/pages and initial field hypotheses.
2. **Structured escalation stage (Azure Form Recognizer / equivalent):** run on low-confidence or ambiguous outputs to produce structured field candidates and their provider-specific confidence.
3. **Optional consensus/normalization stage (LLM, e.g., OpenAI Nano/mini):** normalize values, resolve near-matches, and generate deterministic alternatives only when needed.

Final field value for each field is selected using a **consensus policy**, not by source preference alone:

- If two stages agree semantically (after normalization), raise confidence.
- If one stage disagrees or all scores are weak, mark `requiresReview` and retain all stage attempts.
- Always persist per-stage metadata (`provider`, `stage`, `rawConfidence`, `parsedValue`, `normalizedValue`, `latencyMs`, `errors`, `attemptId`).

A single extraction result record must be represented as:

- `accepted` (boolean)
- `requiresReview` (boolean)
- `systemConfidence` (fused score)
- `stageConsensusScore` (or equivalent)
- `sourceValues` (array of provider candidate values and metadata)

## Rationale

- A single-engine approach is brittle for handwritten forms and leads to either over-trust or over-review.
- Multi-stage escalation provides a bounded-cost path: only uncertain data pays premium provider cost.
- Consensus-driven fusion produces better reliability than raw provider confidence alone and gives reviewers actionable transparency (`who said what, and why`).
- Provider portability is retained because the pipeline is stage-oriented with standardized contracts; switching or adding providers does not require changing API contracts.

## Consequences

### Positive

- Higher extraction quality on varied documents through fallback escalation.
- Better reviewer UX via calibrated confidence and explicit provenance.
- Safer operations through deterministic audit trail and explicit review gating.
- Lower lock-in and easier model evolution by keeping providers behind a common interface.

### Negative / Risks

- Increased worker complexity (stage orchestration, result fusion, calibration tables).
- Additional latency/cost on escalated documents.
- Requires governance for provider credentials, rate limits, and budget.

## Implementation implications

- Introduce a provider-agnostic `IExtractionProvider` interface with a normalized `ExtractionAttempt` payload.
- Keep extraction contracts unchanged for current UI/API except enriched metadata:
  - per-field `confidence`
  - `requiresReview` behavior remains first-class
  - optional `extractionAttempts` for diagnostics/troubleshooting.
- Add explicit threshold policy:
  - `>= 0.90` auto-accepted (still auditable)
  - `0.75 - 0.90` warning review path
  - `< 0.75` forced review
- Store every stage attempt; never overwrite previous attempts.
- Add fixtures/integration tests for:
  - provider agreement
  - provider disagreement
  - low-confidence escalation
  - reviewer correction persistence

## Rejected alternatives

### Single-provider extraction only

Rejected because it concentrates failure modes and provides no robust confidence governance for ambiguous cases.

### UI-only confidence heuristic

Rejected because it does not address underlying extraction quality.

### Full external model-router platform first

Rejected for this slice because it adds orchestration overhead before the extraction workflow itself is stabilized.

## Future extension

The same stage contract supports consensus expansion:

- adding more providers,
- field-specific routing (e.g., handwriting-heavy documents go to OCR-first, then Azure for forms),
- future voting rules (weighted consensus, fraud/risk checks, ensemble thresholds).

The decision is intentionally portable: providers can be reordered, replaced, or augmented without changing the review UI contract.