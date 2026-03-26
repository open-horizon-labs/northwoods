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


## Solution Space
**Updated:** 2026-03-25

> Recommendation: implement the walking skeleton as a thin end-to-end vertical slice with one React app, capability-aligned .NET services, Temporal orchestration, MinIO object storage, and Postgres-backed tenancy/retrieval, but initially only one template and one reviewer queue path.

## Solution Space Analysis

**Problem:** We need the first implementation slice to prove that an uploaded handwritten intake can become a review-ready draft and then a finalized record across frontend and backend.
**Key Constraint:** The slice must exercise the hard seams—tenant-aware auth, object storage, asynchronous extraction, confidence-aware review, and auditability—without exploding the build into broad checklist work.

### Candidates Considered

| Option | Level | Approach | Trade-off |
|--------|-------|----------|-----------|
| A | Band-Aid | Seeded review loop: build login + review UI against pre-created extracted drafts, then add upload/extraction later | Fastest UI progress, but it dodges the core ingestion and async seams we need to prove |
| B | Local Optimum | Upload-first skeleton: build auth, upload, object storage, and extraction status, but stop before reviewer correction/finalization | Exercises ingestion well, but leaves the trust-winning human review loop unproven |
| C | Reframe | Review-ready intake loop: build one tenant-safe vertical slice from upload through async extraction to reviewer correction/finalization and audit trail | More surface area in the first slice, but it forces most critical contracts to exist together |
| D | Redesign | Full capability platform: include search, similar cases, multiple templates, richer case views, and broader service boundaries in the first pass | Stronger demo breadth, but high risk of shallow trust and unfinished core workflow |

### Evaluation

**Option A: Seeded review loop**
- Solves stated problem: **Partially**
- Implementation cost: **Low**
- Maintenance burden: **High**
- Second-order effects: Encourages a fake first milestone where the frontend looks alive but the backend seams that matter most remain untested. Later integration would likely cause churn in contracts and state modeling.

**Option B: Upload-first skeleton**
- Solves stated problem: **Partially**
- Implementation cost: **Low-Medium**
- Maintenance burden: **Medium**
- Second-order effects: Good for proving storage and workflows, but it delays the reviewer correction/finalization path that actually demonstrates human-in-the-loop trust and the assignment's core workflow correctness.

**Option C: Review-ready intake loop**
- Solves stated problem: **Yes**
- Implementation cost: **Medium**
- Maintenance burden: **Medium-Low**
- Second-order effects: Forces the right contracts early: document identity, workflow status, extracted field shape, confidence modeling, reviewer mutations, finalization rules, audit events, and tenant scoping across them all. It also leaves an obvious attachment point for Similar Cases.

**Option D: Full capability platform first**
- Solves stated problem: **Yes, but too broadly**
- Implementation cost: **High**
- Maintenance burden: **High**
- Second-order effects: Increases coordination and surface area before the representative JTBD is stable. High risk of progress that looks broad but fails under scrutiny on the core trust loop.

### Recommendation

**Selected:** Option C - Review-ready intake loop
**Level:** Reframe

**Rationale:** This is the smallest slice that still proves the product. It exercises both user roles, makes the backend own real state transitions, and forces the architecture decisions already made—RLS-backed tenancy, Temporal, MinIO, Postgres retrieval readiness—to work in concert. It is broad enough to be credible and narrow enough to finish.

**Accepted trade-offs:**
- Start with a single template and a constrained extracted field schema rather than all four forms.
- Use mocked or hybrid OCR behind the real Temporal workflow contract in the first iteration.
- Defer Similar Cases, search, and broader case views until the upload → extract → review → finalize loop is solid, but keep the schema and service contracts ready for them.

### Implementation Notes

**What the first slice should include**
- Login for two roles: Intake Worker and Reviewer.
- Upload form with template selection for one representative template.
- Document record persisted with tenant context and object storage reference.
- Temporal ingestion workflow that stores the file, runs extraction, writes extracted fields + confidence, and marks the document review-ready.
- Reviewer queue/detail screen showing original document, extracted fields, confidence cues, editable corrections, finalize action, and audit trail.
- Finalized record view or status transition proving completion.

**What the first slice should not include**
- Multiple templates beyond what is needed to prove the pattern.
- Fully featured Similar Cases or search before the review loop works.
- Over-generalized abstractions for future templates or workflows.
- Additional services that do not protect a real capability or trust boundary.

**Suggested first contracts**
- `POST /auth/login` → returns JWT with `tenantId` and role.
- `POST /intakes` → creates document + starts workflow.
- `GET /intakes/{id}` → returns processing status and extracted draft when ready.
- `GET /review-queue` / `GET /reviews/{id}` → reviewer worklist and detail.
- `POST /reviews/{id}/finalize` → persists corrections, finalizes, appends audit event.

**Exit criteria for this slice**
- A tenant-scoped user can upload a document and see status advance asynchronously.
- A reviewer can open the resulting draft, correct fields, finalize, and observe audit history.
- Cross-tenant access fails in API and retrieval tests.
- The code structure clearly supports adding Similar Cases next without reworking the core workflow.

## Execute
**Updated:** 2026-03-26
**Status:** complete

Completed the first real backend slice for the review-ready intake loop.

What now works end to end:
- Tenant-scoped login against seeded users in Postgres
- Multipart intake upload with tenant header enforcement
- Original document storage in MinIO via an S3-compatible API
- Durable document/extracted-field/audit persistence in Postgres
- Worker-driven asynchronous extraction that advances uploaded documents to `review_ready`
- Review detail retrieval with confidence-scored fields and audit history
- Review finalization that persists corrections and marks the document finalized
- Row-level tenant isolation enforced via `app.tenant_id` + `app_user` role + RLS policies

Verification completed during execution:
- `dotnet build src/Northwoods.slnx` passed
- `docker compose up --build -d` brought up Postgres 18, MinIO, API, and worker
- `/healthz` returned healthy and proved DB connectivity
- Upload → extract → review → finalize flow succeeded via curl
- Cross-tenant intake lookup returned no data under the wrong tenant header

Notes:
- The worker currently simulates extraction output; this is the deliberate placeholder for a later Temporal-based workflow implementation.
- Status values are stored in snake_case in Postgres and mapped explicitly to `ProcessingStatus` in the API.
- Frontend screens for login, upload, review queue, review detail, and finalize are now implemented on top of this backend spine.
- Temporal orchestration and similar-case retrieval remain next steps after the working browser flow.

Frontend verification added after the backend slice:
- `pnpm --dir apps/web lint` passed
- `pnpm --dir apps/web build` passed
- Vite dev server was exercised in a browser session
- Browser flow covered seeded-user sign-in, UI-driven upload, live review queue display, review detail inspection, and finalize action