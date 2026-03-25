# Session: cto-judgement

## Aim
**Updated:** 2026-03-25

## Aim Statement

**Aim:** Intake workers and reviewers can turn handwritten intake packets into tenant-safe, trustworthy case records by reviewing only the uncertain parts instead of manually transcribing entire documents or ignoring historical context.

**Current State:** Handwritten intake documents are slow to process, hard to search, easy to mis-key, and often live as disconnected files with little confidence information or reusable case context.
**Desired State:** Workers upload a document, the system extracts and structures it, reviewers focus only on low-confidence fields, and they use similar prior cases to finalize records quickly and confidently within their own tenant boundary.

### Mechanism
**Change:** Build a multi-tenant intake-processing platform with JWT auth, template-driven uploads, background OCR/AI extraction, per-field confidence scoring, human review, audit logging, case search, and RAG-backed similar-case assistance.
**Hypothesis:** If the workflow is trustworthy, explainable, and tenant-safe end to end, users will adopt exception-based review instead of full manual transcription, and reviewers will make faster, more consistent decisions because the system brings both uncertain fields and relevant prior cases into one place.
**Assumptions:**
- OCR/AI extraction can be good enough to make human review selective rather than total.
- Similar-case retrieval adds real review context instead of noise.
- Tenant isolation must be visible in the design, not implied, for the solution to be credible.
- A deliberately scoped, well-explained system will demonstrate stronger CTO judgment than a broader but shakier build.

### Feedback
**Signal:** In a local demo, a user can complete the full upload → extract → review → finalize workflow for multiple form types, reviewers can see and correct low-confidence fields, similar cases appear with relevant summaries, and no cross-tenant data appears in search, review, or retrieval results.
**Timeframe:** Immediate during development via end-to-end demos and tests; final validation at submission review.

### Guardrails
- Prefer a coherent, end-to-end vertical slice over feature sprawl; every included capability must reinforce the core workflow.
- Do not fake trust: confidence scores, audit history, processing status, and tenant boundaries must be explicit and testable.
- Keep the architecture defensible: simple but conceptually correct multi-tenancy, RAG, and service boundaries beat premature complexity.
- Treat documentation as part of the product; architecture rationale, trade-offs, and AI usage must make the judgment legible to Banyan reviewers.


## Problem Space
**Updated:** 2026-03-25

> Capability-focused framing: design around durable business capabilities that make the core jobs-to-be-done feasible, not around assignment bullet points as isolated features.

## Problem Space Map

**Date:** 2026-03-25
**Scope:** Banyan CTO assignment — capability-oriented problem framing for a multi-tenant handwritten intake processing platform

### Objective
We are optimizing for: a believable end-to-end operating model where intake workers and reviewers trust the system enough to use exception-based processing, while Banyan evaluators can see clear technical judgment in how the platform is decomposed, justified, and constrained.

### Constraints

| Constraint | Type | Reason | Question? |
|------------|------|--------|-----------|
| Backend must be C# / .NET | hard | Explicit assignment requirement | No |
| Frontend must be React + TypeScript | hard | Explicit assignment requirement | No |
| Solution must present as Clean Architecture implemented via microservices | hard | The brief is evaluating architectural judgment as much as feature completeness | The exact service count is questionably fixed; the requirement for separable services is not |
| RAG with a vector database is required in the review workflow | hard | Explicit requirement and a primary evaluation criterion | No |
| Multi-tenancy must isolate templates, uploads, processing results, review actions, search, and vector retrieval | hard | Explicit requirement; cross-tenant leakage would invalidate the exercise | No |
| The system must run locally with Docker Compose and be explainable through docs | hard | Explicit deliverable; reviewers need runnable evidence and rationale | No |
| OCR/extraction may be real or mocked | soft | The brief allows either if the choice is explained | Yes — use the simplest approach that still preserves workflow credibility |
| The dataset for similar-case retrieval may be synthetic | soft | The brief allows generated data to make retrieval meaningful | Yes — synthetic data is acceptable if realistic enough to demonstrate patterns |
| Representative rather than exhaustive tests are acceptable | soft | The brief asks for thoughtful coverage, not total coverage | Yes — focus tests on trust boundaries and core workflow transitions |
| 'Microservices' means many independently complex services | assumed | Common interview over-interpretation, not stated by the brief | Yes — a small number of sharply bounded deployables may show better judgment |
| Every requirement deserves equal implementation depth | assumed | Checklist thinking tempts breadth-first delivery | No — the review workflow, tenancy boundaries, and RAG depth matter more than peripheral richness |

### Terrain
- **Systems:** Identity and tenant context propagation, template/catalog management, document ingestion, background extraction pipeline, structured case store, audit log, review workspace, semantic retrieval pipeline, vector database, and operational cross-cutting concerns such as logging, correlation IDs, retries, and health checks.
- **Stakeholders:** Intake workers, reviewers, tenant organizations, operators/developers, and Banyan evaluators assessing architecture, tradeoffs, and AI leverage.
- **Blast radius:** Wrong or opaque extraction erodes trust; weak review UX causes manual rework; cross-tenant leakage is disqualifying; shallow RAG makes the AI story look ornamental; overbuilt architecture risks an impressive diagram with a weak working product.
- **Precedents:** Human-in-the-loop document AI systems, case management review queues, exception-based operations, and capability-oriented platforms that separate identity/tenancy, ingestion/processing, review/finalization, and search/intelligence concerns.

### Capability-Oriented Feasible JTBD Workflows
1. **Receive and digitize an intake:** When an intake worker receives a handwritten form, they need to choose the right template, upload the scan, and know whether processing succeeded so the case can enter the system without full manual re-entry.
2. **Review only what is uncertain:** When a reviewer sees extracted data, they need confidence-aware highlighting, source document context, and correction/finalization actions so they can spend attention only where the machine is uncertain.
3. **Use historical context to judge the current case:** When a reviewer is unsure how to interpret a case, they need similar prior cases and concise AI-generated context so they can recognize patterns without leaving the review workflow.
4. **Understand the case as a longitudinal record:** When a user searches for a person or case, they need all related documents and statuses in one place so the work is not trapped in isolated uploads.
5. **Trust the platform operationally:** When an operator or evaluator inspects the system, they need proof of tenant isolation, auditable actions, background retry behavior, and system health so the product feels production-minded rather than demo-fragile.

### Assumptions Made Explicit
1. The center of gravity is the review workflow, not the upload form alone - if false: effort gets spread evenly across the checklist and the product feels shallow where evaluators care most.
2. Capability boundaries should follow workflow and trust boundaries, not technical layers - if false: the system devolves into thin CRUD services with unclear ownership and awkward cross-service choreography.
3. Tenant context can be propagated consistently through APIs, jobs, storage, and retrieval filters - if false: multi-tenancy becomes a documentation claim rather than a system property.
4. Similar-case assistance will be judged on usefulness, not mere existence - if false: a token vector search implementation may pass the letter of the brief but fail the spirit of the evaluation.
5. Synthetic templates and sample data can still demonstrate realistic social-service patterns - if false: retrieval quality and review context will look arbitrary.

### X-Y Check
- **Stated need (Y):** Build the checklist of features in the assignment using .NET, React, microservices, OCR/AI, RAG, and multi-tenancy.
- **Underlying need (X):** Demonstrate CTO-level judgment by choosing a capability model and workflow shape that make the system trustworthy, explainable, and feasibly deliverable within interview constraints.
- **Confidence:** High that Y is not the whole story; the checklist is the vehicle, but the actual test is architecture quality, prioritization, and tradeoff clarity.

### Ready for Solution Space?
Yes - the terrain is clear enough to compare solution options. The next step is to choose a capability decomposition and service topology that keep the JTBD workflows first-class while minimizing accidental complexity.

## Problem Statement
**Updated:** 2026-03-25

> Chosen walking skeleton: intake worker uploads a handwritten form, the system extracts a draft with confidence scores, and a reviewer corrects uncertain fields and finalizes the record within tenant boundaries.

**Current framing:** We need to pick a JTBD workflow that is a good walking skeleton to get the backend and frontend wired up and working.

**Reframed as:** Intake workers and reviewers need one thin end-to-end trust loop that proves a handwritten document can become a tenant-safe, corrected, finalized case record, because that is the smallest workflow that demonstrates the platform's core value and architectural credibility; currently the assignment is easy to treat as a breadth-first feature checklist, which would wire many parts loosely without proving the central workflow.

**The shift:** From 'what should we build first?' to 'what is the smallest workflow that proves the product is real?' The selected JTBD spine is upload → extract → review uncertain fields → finalize. Search, case view, and similar-case assistance should attach to this spine, not compete with it for first priority.

### Constraints
- **Hard:** .NET backend, React/TypeScript frontend, tenant-aware authorization, template-based upload, stored extraction/confidence/status, human review with corrections and finalization, and a locally runnable system. The assignment's required workflow correctness explicitly centers upload → extract → review → finalize.
- **Soft:** Real OCR versus mocked extraction, number of initial templates needed in the first slice, how much search/case aggregation appears in the first increment, and whether similar-case retrieval is fully implemented in the first walking skeleton or added immediately after the review spine is stable.

### What this framing enables
- A first vertical slice that exercises both frontend and backend with meaningful state transitions instead of isolated CRUD screens.
- Capability boundaries organized around intake processing and review trust, which is the clearest path to a defensible service topology.
- Early proof of tenant propagation, audit logging, confidence handling, and worker orchestration on a real user journey.
- A natural place to attach RAG later in the same reviewer workflow once the review surface exists.

### What this framing excludes
- Starting with search, case aggregation, or vector retrieval before the upload-to-review trust loop exists.
- Building microservice shells or shared abstractions that are not yet earned by a working end-to-end flow.
- Treating template management or blank-template download as the first integration slice; those matter, but they do not prove the product's core behavior.
- Broad checklist progress that looks busy but leaves the reviewer unable to walk a document from intake to finalized record.

## Solution Space
**Updated:** 2026-03-25

> Recommendation: build a thin capability-sliced platform from day one — one React app, small backend services aligned to the trust loop, and a separate worker — with shared relational storage and a real vector store, rather than either a fake seeded demo or a fully overbuilt event mesh.

## Solution Space Analysis

**Problem:** We need a walking skeleton that proves a handwritten document can move from upload to extracted draft to reviewer correction to finalization across backend and frontend.
**Key Constraint:** The first slice must make the core trust loop real while still reading as a credible microservices solution under the assignment's tenant-safety, review, and RAG guardrails.

### Candidates Considered

| Option | Level | Approach | Trade-off |
|--------|-------|----------|-----------|
| A | Band-Aid | Review-first seeded demo: start with a seeded extracted document and build only the review/finalize UI before wiring upload or background processing | Very fast feedback, but it dodges ingestion, async processing, and trust boundaries that the assignment actually evaluates |
| B | Local Optimum | Modular monolith + worker: one ASP.NET backend with clean internal modules, one separate worker, one React app, shared database, row-level tenancy | Fastest credible end-to-end build, but weak signal on the 'set of microservices' requirement and may require later repartitioning |
| C | Reframe | Thin capability-sliced services: Identity/Tenant service, Intake service, Review/Case service, separate Extraction worker, one React app, shared Postgres, real vector DB | More upfront integration work, but the architecture mirrors the workflow and trust boundaries we actually need to prove |
| D | Redesign | Full platform-first event mesh: many services, queue/bus, per-service databases, dedicated audit/search/retrieval services, richer infrastructure from day one | Strong theoretical purity, but high coordination cost and a real risk of architecture theater before the user workflow works |

### Evaluation

**Option A: Review-first seeded demo**
- Solves stated problem: **Partially**
- Implementation cost: **Low**
- Maintenance burden: **High** once real upload, tenant propagation, and worker orchestration are added
- Second-order effects: Gives fast UI momentum, but it hides the most failure-prone seams and risks producing a demo that cannot prove the assignment's central workflow correctness.

**Option B: Modular monolith + worker**
- Solves stated problem: **Yes**
- Implementation cost: **Low-Medium**
- Maintenance burden: **Medium**
- Second-order effects: Strong delivery speed and easy local debugging, but it under-signals architectural judgment against the explicit microservices ask and pushes service-boundary decisions later when rewrites are more expensive.

**Option C: Thin capability-sliced services**
- Solves stated problem: **Yes**
- Implementation cost: **Medium**
- Maintenance burden: **Medium-Low** if boundaries stay few and capability-aligned
- Second-order effects: Exercises the real seams early — auth/tenant propagation, async extraction, review/finalization, and eventual retrieval — while staying small enough to keep the walking skeleton coherent. It also gives a natural home for search, case view, and similar-case assistance as follow-on slices.

**Option D: Full platform-first event mesh**
- Solves stated problem: **Yes, in theory**
- Implementation cost: **High**
- Maintenance burden: **High**
- Second-order effects: Maximizes distributed-systems overhead before user value is proven. Likely to consume time on infrastructure choreography, boilerplate, and service coordination instead of making the review workflow trustworthy.

### Recommendation

**Selected:** Option C - Thin capability-sliced services
**Level:** Reframe

**Rationale:** This approach best fits the actual test. The assignment is not rewarding the narrowest possible backend, nor the fanciest distributed system. It rewards clear boundaries, credible multi-tenancy, a real async extraction path, and a review workflow where trust is visible. Option C gives us enough architecture to make those claims real without spending the schedule on platform ceremony.

**Why not the others:**
- **Option A:** Useful only as a throwaway prototype; it fails the trust-loop test because upload, extraction orchestration, and tenant seams remain unproven.
- **Option B:** Tempting for speed, but too easy to look like a monolith wearing microservice language. It weakens the architecture signal we want the reviewer to see.
- **Option D:** Over-optimizes for architectural purity and future scale that the exercise does not require. High risk, low interview payoff.

**Accepted trade-offs:**
- Use a shared Postgres instance initially, with explicit service schemas and tenant-aware tables, rather than paying the cost of fully separate operational databases on day one.
- Start with mocked or hybrid extraction behind a real asynchronous job contract so the workflow shape is correct before the extraction model gets fancy.
- Land Similar Cases immediately after the upload/extract/review/finalize spine is stable, attaching it to the same review screen rather than treating it as a separate workstream.

### Implementation Notes

**Recommended service topology**
- **Identity/Tenant Service:** issues JWTs with `tenantId` and role claims for Intake Worker and Reviewer.
- **Intake Service:** owns templates, uploads, document records, processing status, and starts the extraction workflow.
- **Review/Case Service:** owns review tasks, corrections, finalization, audit log, case read model, and later search/similar-case endpoints.
- **Extraction Worker:** runs as a Temporal worker that performs OCR/extraction, writes extracted field values + confidence scores, and records workflow progress for review consumption.
- **React Frontend:** starts with login, upload, review queue/detail, and finalized record views.

**Recommended infrastructure choices**
- **Relational + retrieval store:** Postgres with `tenant_id` on all major entities, service-specific schemas, `pgvector` for embeddings, and built-in full-text search for hybrid retrieval and case search.
- **Object storage:** MinIO in Docker Compose via its S3-compatible API; preserve original documents there and keep object references in Postgres.
- **Async orchestration:** Temporal for durable workflows, retries, visibility, and chained steps; avoid introducing a second jobs table or broker unless a concrete need appears.

**Recommended sequencing**
1. Wire auth + tenant context, one template, upload, document record, object storage, and the extraction workflow start.
2. Wire the Temporal worker to produce extracted draft data + confidence scores and expose processing status.
3. Build the review screen that shows source document, extracted fields, corrections, finalize action, and audit trail.
4. Add Similar Cases into that same review screen using synthetic historical data and Postgres hybrid retrieval (`pgvector` + FTS).
5. Extend into case search/aggregate views, richer observability, and broader template coverage once the spine is trustworthy.