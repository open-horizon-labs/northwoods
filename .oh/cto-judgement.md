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