# Northwoods Self-Assessment (Issue #8)

Date: 2026-03-27

## Executive summary

Issue #8 is the final documentation deliverable for the backlog: a candid accounting of what is implemented, what remains, where trade-offs were made, and where AI assistance materially changed the way we worked. The implementation pipeline now has a coherent workflow for intake digitization, reviewer-focused confidence triage, and RAG-assisted review, with strong tenant isolation controls and evidence-based test coverage. The only major functional holes are still the deferred search/aggregate-case surface and full production-like runtime verification, both deferred by environment/tooling constraints in this session.

## What is complete and functioning

### Backend and workflow capabilities delivered
- **RAG-assisted reviewer workflow** is in place for the review screen (`ReviewCaseSection`) and returns similarity context from Postgres-backed hybrid retrieval (`apps/web`, `src/Services/Northwoods.Api`, `src/Workers/Extraction.Worker`, `src/BuildingBlocks/Northwoods.Review`).
- **Tenant isolation is enforced across storage, auth, documents, reviews, metrics, and search paths** via JWT tenant claims and DB session context/RLS configuration (`Tenant-Middleware`, `DbConnectionFactory`, `DbConnectionFactoryTests`).
- **Append-only review/audit trail** behavior exists in extraction and workflow events; extraction attempts are persisted as attempts with confidence inputs, and review/finalize transitions are auditable.
- **Confidence-tiered review behavior** is operationalized in extraction and UI review flows.
- **Representative trust-boundary test suite** covers tenant isolation, upload-to-processing transition, review/finalize flow, and confidence-sensitive extraction behavior.
- **Operational observability** includes structured logging, correlation IDs, metrics, and retry behavior for transient extraction failures.
- **JWT/RBAC model** is implemented with role-aware upload/review/finalize gates.
- **Architecture rationale and rationale traceability** are documented in [Northwoods Architecture Rationale](architecture.md), with explicit trade-offs and non-negotiables.
- **AI tooling stack** and process usage are documented in [AI Development Tooling Used](ai-tooling.md).

### Delivery validation currently available
- Automated checks run and passing in this branch:
  - `pnpm check`
  - `pnpm test`
  - `pnpm test:unit` / `pnpm test:runtime` via repo scripts where applicable.
- PR-level audit artifacts were gathered for completed merged work and found clean after remediation where applicable (details in session logs).
- Live end-to-end runtime smoke has **not** been executed in this environment repeatedly due infrastructure/tooling responsiveness failures (see “Missing/Deferred” section).

## What is missing or deferred (and why)

### Missing: case aggregate/search workflow (Issue #2)
- Full case aggregate and search view remains open and partly unimplemented.
- This was deferred earlier because the first implementation attempt was abandoned before lifecycle completion.
- Impact: limits full fulfillment of “case visibility” rubric area.

### Missing / deferred: runtime smoke verification
- Repeated runtime spot-checks for upload -> extract -> review -> finalize were blocked by environment limitations.
- Specifically blocked commands:
  - `docker compose up -d postgres minio api worker`
  - `docker compose ps`
  - `curl -sS http://localhost:5100/healthz`
  - End-to-end `/intakes` smoke upload command with sample PDF.
- Impact: cannot currently provide a fully observed live path across all services.

### Missing: Playwright UI smoke
- A dedicated UI browser smoke is still deferred (`test:web-smoke` marker exists).
- Impact: manual UI path and browser-side visuals are less directly exercised than API/unit/integration paths.

### Missing: authentication hardening
- Current password storage/verification remains developer-grade from the assignment sequence and is noted as a follow-up security hardening item.

## Trade-offs and rationale

1. **Postgres-centered retrieval (vector + FTS + trigram + structured boost)**
   - **Why this path:** satisfy “RAG must improve review judgment” without adding a second retrieval service while keeping tenant filtering straightforward.
   - **Trade-off:** reduced vendor dependency and simpler deployment, at the cost of heavier coupling and future scaling constraints.

2. **Append-only extraction attempt model over overwrite model**
   - **Why this path:** supports auditability and confidence-based review semantics.
   - **Trade-off:** more storage and query complexity, but clearly improves trust and traceability.

3. **Cleaned scope before ornamental polish**
   - **Why this path:** prioritize confidence-aware review flow, tenancy, and retrieval usefulness before broad UI cosmetics.
   - **Trade-off:** some UX polish and advanced UI testing remain for later passes (Issues #9–#11).

4. **Documented over code-path micro-optimizations**
   - **Why this path:** the assignment evaluation explicitly values explainability and CTO-level judgment over premature micro-optimizations.
   - **Trade-off:** some speed wins are deferred, but decision quality and maintainability improved.

## Rubric scoring reflection

Scoring uses the rubric in [Reviewer Rubric](reviewer-rubric.md) (1–4):

| Area | Score | Rationale | Gap status |
|---|---:|---|---|
| Intake digitization | 3 (Strong) | Upload, async extraction, status/correlation, and field extraction with confidence exist with test coverage. | No blocker, but no live smoke evidence in this environment. |
| Human review | 3 (Strong) | Reviewer sees extracted values with confidence and can finalize/override with audit trail. | No blocker, but runtime smoke evidence is delayed. |
| Similar-case assistance | 3 (Strong) | Similar cases are embedded in review with hybrid scoring and relevance weighting. | External runtime quality validation is pending by environment. |
| Case visibility | 2 (Credible but shallow) | Core workflow is implemented, but case aggregate/search experience (Issue #2) is incomplete. | **Below threshold; known gap.** |
| Tenant safety and operability | 4 (Excellent) | Tenant claim propagation, RLS session context, and isolation checks exist and are tested. | No major known gaps. |
| Architecture judgment | 4 (Excellent) | Cohesive capability boundaries, explicit trade-offs, and rationale docs are present. | No major known gaps. |

### Known score gap and mitigation
The **Case visibility** area is the explicit below-threshold area (`2`). It is tracked as Issue #2 and should be completed before final submission confidence gates are fully met.

## AI-assisted development examples

### Example 1 — planning and scoping synthesis
- **Prompt:** “Create a concise execution summary for Issue #8 that lists completed vs missing work across Issues #1, #3, #4, #5, #6, and #7, and map each to the reviewer rubric areas.”
- **Use:** generated this report’s issue-by-issue inventory and rubric-aligned progress section.

### Example 2 — trade-off clarification
- **Prompt:** “Draft a short trade-off section for an interview-style architecture doc that must prioritize trust, auditability, and tenant safety over convenience.”
- **Use:** produced the trade-off rationales in this file and informed concise architectural framing in [Northwoods Architecture Rationale](architecture.md).

### Example 3 — risk disclosure and deferment wording
- **Prompt:** “List honestly what should stay explicit as incomplete if we cannot run full runtime smoke checks, without masking risk.”
- **Use:** used to keep the “Missing/Deferred” section explicit and actionable for the next operator.

### Example 4 — review audit workflow reasoning
- **Prompt:** “Summarize the minimal post-merge audit evidence required to satisfy a code-review finding cleanup policy before considering docs-only work complete.”
- **Use:** shaped the Phase-5 verification section and comment-audit discipline used in this issue.

## Honest verification statement

The strongest known unknown remains **live runtime verification**. Most implementation and code-path-level confidence checks are in place and documented, but full local containerized smoke remains blocked by the current environment. This is tracked as a concrete deferral with exact commands and can be executed as soon as runtime stability is restored.
