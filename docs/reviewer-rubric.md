# Reviewer Rubric

This rubric translates the assignment from a feature checklist into the system jobs-to-be-done and trust boundaries it should prove.

## How to read this rubric

A strong submission should not merely contain the listed components. It should make these behaviors feasible, trustworthy, and legible:
- users can complete the core workflow with confidence,
- reviewers can understand why the system made its suggestions,
- tenant boundaries are explicit and defensible,
- and the architecture shows judgment rather than checklist compliance.

## Rubric

| Area | Job to Be Done / Outcome | What good looks like | Failure mode to watch for |
|---|---|---|---|
| Intake digitization | Turn handwritten intake packets into structured case records | Upload is template-guided; background processing is visible; original file, extracted fields, confidence scores, and status are all retained; the result is searchable structured data rather than a stored blob | Upload works but extraction is opaque, brittle, or not meaningfully connected to downstream workflow |
| Human review | Focus reviewers on the uncertain parts of each intake | Reviewers see extracted fields beside the source image/PDF, understand confidence, correct only what is needed, and finalize with an audit trail | Reviewer flow is really manual transcription in disguise, or confidence exists but does not change behavior |
| Similar-case assistance | Bring similar historical cases into the review decision | Retrieval is embedded in the review workflow; similar cases are plausibly relevant; summaries/comparison cues help judgment at the moment of review | Vector DB exists, but retrieval is ornamental, noisy, or disconnected from reviewer decisions |
| Case visibility | Make processed intakes searchable and visible as cases | Users can search by meaningful attributes and understand a person/case across multiple documents and statuses | Search is shallow, case view is absent, or documents remain isolated uploads |
| Tenant safety and operability | Prove the platform is tenant-safe and operationally credible | Tenant context is enforced across APIs, jobs, storage, search, and retrieval; logs, health checks, retries, tests, and docs make the system inspectable and believable | Tenancy is implied rather than proved; resilience and observability are afterthoughts |
| Architecture judgment | Make CTO judgment legible in the system | Capability boundaries are coherent; complexity is earned; docs explain trade-offs, AI usage, and omissions clearly | Microservice theater, excessive surface area, or a system that feels broader than it is trustworthy |

## Scoring lens

If I were evaluating this submission, I would score each area on a simple 1-4 scale:

- **1 — Present in name only:** the feature exists, but the behavior is not really enabled
- **2 — Credible but shallow:** the workflow works, but trust, clarity, or integration is weak
- **3 — Strong:** the behavior is clearly enabled and the design choices are defensible
- **4 — Excellent:** the behavior is enabled in a way that shows unusually strong judgment, clarity, and product sense

## Non-negotiables

These are the constraints that should be treated as gating, not bonus points:

- **Tenant isolation must be provable** across API, jobs, storage, search, and vector retrieval.
- **RAG must improve reviewer judgment** inside the review workflow rather than stand alone as technical garnish.
- **Architecture must follow capabilities and trust boundaries** rather than technical-layer decomposition for its own sake.
- **Scope must protect a coherent vertical slice** rather than chase shallow completeness.
- **Documentation must equip the next reader** to understand the operating model, trade-offs, and AI-assisted development approach.

## What this rubric optimizes for

This rubric intentionally favors:
- trust over novelty,
- coherent workflow over checklist breadth,
- explicit trade-offs over accidental complexity,
- and AI as force multiplier over AI as performance art.

That is the standard I want the final system to meet.