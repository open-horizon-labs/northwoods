# Session: review-ready-intake-loop

## Aim
**Updated:** 2026-03-25

## Aim Statement

**Aim:** Intake workers and reviewers can move a handwritten intake from upload to review-ready draft to finalized record inside one tenant-safe workflow, so staff spend their time validating uncertainty instead of re-entering entire forms.

**Current State:** Handwritten forms arrive as opaque files that require manual interpretation, manual re-entry, and disconnected follow-up work.
**Desired State:** A worker uploads a form, the system creates a draft with confidence signals, and a reviewer corrects only what is uncertain before finalizing the case record.

### Mechanism
**Change:** Build the first vertical slice around a single review-ready intake loop: authenticated upload, template selection, object storage, durable extraction workflow, extracted fields with confidence, review UI, correction/finalization, and audit history.
**Hypothesis:** If this loop is trustworthy and complete, it will prove the core product behavior and force most of the critical technical seams—tenant propagation, async orchestration, storage, retrieval readiness, and review correctness—to work together.
**Assumptions:**
- One representative intake-to-review loop exercises enough of the platform to be a credible walking skeleton.
- Review trust matters more than breadth-first feature completion.
- Similar-case retrieval can attach immediately after this loop is stable without changing the underlying workflow shape.

### Feedback
**Signal:** In a demo, an intake worker can upload a document for a tenant, the system asynchronously produces a reviewable draft with confidence scores, and a reviewer can correct fields and finalize it without seeing cross-tenant data.
**Timeframe:** Immediate during implementation through end-to-end walkthroughs and integration tests.

### Guardrails
- This slice must prove tenant safety, not just happy-path data movement.
- Confidence, processing status, and auditability must be explicit in the workflow.
- The slice must stay capability-aligned: intake, review, storage, orchestration, and tenancy should all be visible.

## Problem Statement
**Updated:** 2026-03-25

**Current framing:** We need to pick a representative JTBD that requires a good chunk of the slice.

**Reframed as:** Intake workers and reviewers need one representative workflow that exercises the platform's trust boundaries and operational seams end to end, because the first slice should prove the system can turn an uploaded handwritten document into a reviewed, finalized case record; currently it is easy to choose a narrower demo flow that wires a UI without forcing storage, extraction, tenant isolation, and review correctness to work together.

**The shift:** From choosing a feature-rich demo path to choosing the smallest workflow that still compels the hard parts of the system to exist. The chosen JTBD is: **create a review-ready intake draft and finalize it safely**.

### Constraints
- **Hard:** The flow must include authenticated tenant-aware access, template-based upload, persisted original document storage, asynchronous extraction, extracted values with confidence, reviewer correction/finalization, and auditability.
- **Soft:** Similar-case retrieval, case search, and multiple templates can attach after the loop is operational as long as the design leaves a clean place for them.

### What this framing enables
- Building the first real contract between frontend, APIs, storage, workflow orchestration, and review state.
- Testing the highest-risk seams early: tenant scoping, async processing, and review correctness.
- Choosing service boundaries that reflect business capabilities instead of building shells first.
- Attaching similar-case retrieval to an existing reviewer surface rather than inventing a separate AI demo lane.

### What this framing excludes
- Seeded review demos that bypass upload and extraction.
- Search-first or RAG-first slices that do not prove the intake-to-finalization trust loop.
- Template-management-heavy work that does not materially advance the representative workflow.
- Architecture work that is not earned by the selected JTBD.
