# Overnight Pipeline Session — 2026-03-26

## Session Goals

- Work through backlog issues using `/ .claude/agents/dev-pipeline-oversight.md` sequentially (one-at-a-time, no worktrees).
- Use OH sessions and preserve notes from each agent run.
- Prioritize advancing the issue log through implementation/review gates.
- Leave any issue requiring salvage/rewrite for next day.

## Context

- All prior issues were created in this repo via `/oh-plan` as:
  - #1 RAG Similar Cases in review workflow
  - #2 Search and case aggregate view
  - #3 Add four distinct intake form templates with view/download
  - #4 JWT authentication with RBAC
  - #5 Observability, logging, metrics, retries
  - #6 Representative test suite
  - #7 Architecture rationale doc
  - #8 Self-assessment
  - #9 Review fix loop round 1
  - #10 Review fix loop round 2
  - #11 Frontend audit: AI slop, accessibility, design context alignment
- Active constraints from AGENTS.md: multi-tenancy, append-only attempts, design context (light mode, institutional, accessibility-first).

## Running Notes

- 2026-03-26 22:00:00: Session started, branch `main`, no additional prerequisite blockers found in local tree at kickoff.
- Planned pipeline order: #1 -> #4 -> #2 -> #3 -> #5 -> #6 -> #7 -> #8 -> #9 -> #10 -> #11.

## Notes from Agents

### Deferred

(Agents not yet run)

## Follow-ups

- If an agent marks an issue as needing salvage/rework, do not force closure in this run.
- Carry unresolved follow-up PRs to next run with explicit notes.


## Issue #1 — Implement RAG Similar Cases in review workflow

- **Session file:** [Dev Pipeline -- Implement RAG Similar Cases in review workflow](sessions/1-dev.md)
- **Branch:** `1-rag-similar-cases`
- **Implementation commit:** `f494605` (`feat: add similar-case retrieval to review workflow`)
- **PR:** #12 (`https://github.com/open-horizon-labs/northwoods/pull/12`)
- **Merged:** yes (merge commit `69c3bbb77487c3c865c939defaece312926267b2`)
- **Issue status:** closed (`2026-03-27T02:24:48Z`)

### Major decisions
- Implemented tenant-scoped `case_profiles` table with `vector(16)` embeddings, `search_tsv`, trgm/FTS indexes, and RLS policy.
- Persisted case profiles during extraction worker runs using extracted fields plus OCR-stage attempt text.
- Used hybrid retrieval in review detail (FTS + vector + fuzzy structured boosts + exact DOB signal) fused by reciprocal rank fusion.
- Returned top 5 similar cases from API and rendered them in the web review panel.
- Seeded synthetic historical documents/fields/profiles for meaningful retrieval results in local/demo datasets.

### Blockers
- Delivery smoke verification blocked by environment: docker daemon unresponsive (`docker info` and `docker compose` commands timed out).
- Follow-up required when docker runtime is available: rerun intake->review smoke and verify `similarCases` is populated end-to-end.

### Code review findings and resolutions
- Post-merge audit fetched both endpoints:
  - `gh api repos/open-horizon-labs/northwoods/pulls/12/comments --paginate` → no inline review comments.
  - `gh api repos/open-horizon-labs/northwoods/issues/12/comments --paginate` → one CodeRabbit status comment (`review in progress`) with no actionable findings.
- No Critical/Major/Minor findings were posted; no follow-up fix PR required.

### Questions asked / follow-ups created
- No external clarifying questions were asked during issue #1 execution.
- Follow-up action logged only: rerun live delivery smoke once docker is functional; no new issue/PR created in this run.

## Issue #4 — Implement real JWT authentication with role-based access control

- **Session file:** [Dev Pipeline -- Implement real JWT authentication with role-based access control](sessions/4-dev.md)
- **Branch:** `4-jwt-auth-rbac`
- **Implementation commit:** `aac42a4437bb77458c37f079fafa1652730a0b99` (`feat: implement JWT auth and role-based access control`)
- **PR:** #13 (`https://github.com/open-horizon-labs/northwoods/pull/13`)
- **Merged:** yes (merge commit `6c6cd69ec6fd9f784e66a4873ab043240d78bb12`)
- **Issue status:** closed (`2026-03-27T02:33:43Z`)

### Major decisions
- Replaced header-based tenant/role identity flow with JWT bearer authentication and signed token issuance at `/auth/login`.
- Tenant scope now comes from JWT claim (`tenant_id`) and is propagated into DB RLS session scoping; removed dependence on `X-Tenant-Id` and `X-User-Role`.
- Enforced RBAC at API boundary:
  - `POST /intakes`: allowed for `IntakeWorker`, optional for `Reviewer` via `Auth__ReviewerCanUpload=false` default.
  - `POST /reviews/{id}/finalize`: `Reviewer` only.
- Updated web client to send `Authorization: Bearer <accessToken>` for all protected calls and removed role from login request payload.
- Added JWT configuration defaults to `.env.example` and `docker-compose.yml` API service env.

### Blockers
- Delivery smoke verification blocked by environment: docker runtime unresponsive (`docker compose up` and `docker compose ps` timed out).
- Follow-up required when docker runtime is available: run live intake/review smoke using bearer token-authenticated upload flow.

### Risks / temporary concessions
- Password verification remains seed/dev-grade (plaintext-equivalent fixed-time comparison), not salted hash storage/verification.
- JWT authn/authz path is now real and enforced, but password hashing hardening should be prioritized in a follow-up security task.

### Code review findings and resolutions
- Post-merge audit fetched both endpoints:
  - `gh api repos/open-horizon-labs/northwoods/pulls/13/comments --paginate` → no inline review comments.
  - `gh api repos/open-horizon-labs/northwoods/issues/13/comments --paginate` → one CodeRabbit status comment (`review in progress`) with no actionable findings.
- No Critical/Major/Minor findings were posted; no follow-up fix PR required.

### Follow-up/salvage status
- **Follow-up PR needed:** no (no unaddressed external review findings).
- **Salvage required:** no.
- **Tomorrow follow-up:** rerun end-to-end smoke once docker daemon is healthy.

### Issue #2 — Add search and case aggregate view
- **Attempted:** `dev-pipeline-oversight 2`
- **Status:** aborted by agent with no commit/PR lifecycle completed.
- **Observed:** partial backend/frontend implementation edits were generated locally but lifecycle steps blocked (`no branch/commit/PR`, no session finalization, no phase-5 audit).
- **Risk:** partial edits in working tree are inconsistent with completed pipeline requirements.
- **Action:** deferred to tomorrow with explicit salvage note; no commit created.
- **Next step:** restart issue #2 from a clean tree with full dev-pipeline-oversight flow, or convert partial edits into one clean WIP implementation branch before resuming.

## Issue #3 — Add four distinct intake form templates with view/download

- **Session file:** [Dev Pipeline -- Add four distinct intake form templates with view/download](sessions/3-dev.md)
- **Branch:** `3-templates-browse-download`
- **Implementation commits:**
  - `4e507df64e744705bc908d8a73c81947b133c33f` (`feat: add tenant template catalog and blank form download`)
  - `73836d1d50a5b569935d765f826e0c6cedf9d378` (`fix: address template auth and binding review findings`)
  - `6ddf1c566aa56d15dc429127d17e79c8250f09ce` (`fix: refine template selection and blank form accessibility`)
- **PR:** #14 (`https://github.com/open-horizon-labs/northwoods/pull/14`)
- **Merged:** yes (merge commit `24a04b6d6e5630a8dc8fa6b192c6eb14c508f6c3`)
- **Issue status:** closed (`2026-03-27T04:15:15Z`)

### Post-merge oversight audit outcome
- Audited PR #14 inline comments, issue comments, and review states after merge.
- Detected previously unmerged CodeRabbit **Major** findings in audit-event JSON interpolation.
- Opened and merged focused follow-up PR #15 (`https://github.com/open-horizon-labs/northwoods/pull/15`, merge commit `7a541d7de5cdc19e2bc8d88cad2c71c849a1c7ad`) with serialized audit detail payloads.
- All external findings are now addressed; no remaining follow-up PR needed for #14.

### Blockers
- Delivery smoke validation still blocked by local runtime availability:
  - `curl -sS http://localhost:5100/healthz` timed out.
  - `docker compose up -d postgres minio api worker` timed out.
- Tomorrow follow-up: rerun live intake flow once docker runtime is responsive.

## Issue #5 — Add structured logging, correlation IDs, metrics, and retry resilience

- **Session file:** `.oh/sessions/5-dev.md`
- **Branch:** `issue-5-observability-resilience`
- **Implementation commits:**
  - `f0148b9c16a80220274e9089dcc2bbf8bb0223ac` (`feat: add observability and retry resilience`)
  - `a2d991bb8bcc6dd2086102bafcec9385855c1b33` (`fix: enforce tenant filters and cancel-safe retries`)
- **PR:** #16 (`https://github.com/open-horizon-labs/northwoods/pull/16`)
- **Merged:** yes (merge commit `3fe3de3159b726cec09f9f3f4a122278e56e2d0e`)
- **Issue status:** closed (`2026-03-27T05:46:48Z`)

### Major decisions
- Enabled structured JSON logging (scoped, timestamped) in API and extraction worker.
- Added request correlation middleware (`X-Correlation-Id`) and persisted `correlation_id` into upload/extraction/finalize audit events.
- Added authenticated `/metrics` endpoint exposing request count, review finalization count, and extraction success/failure counts.
- Implemented worker transient retry with configurable backoff (`Extraction__MaxRetryAttempts`, `Extraction__RetryDelayMs`) and explicit cancellation-safe behavior.
- Tightened tenant isolation consistency by adding explicit tenant predicates/params to metrics and document/review/finalize queries in API.

### Code review findings and resolutions
- Post-merge audit endpoints:
  - `gh api repos/open-horizon-labs/northwoods/pulls/16/comments --paginate` → two CodeRabbit findings (1 Critical, 1 Minor), both marked `✅ Addressed in commit a2d991b`.
  - `gh api repos/open-horizon-labs/northwoods/issues/16/comments --paginate` → CodeRabbit run-status note plus maintainer status note; no additional actionable findings.
- Unaddressed external findings: none.
- Follow-up PR required: no.

### Blockers / deferred verification
- Runtime delivery smoke remains blocked in this environment:
  - `docker compose up -d postgres minio api worker` timed out.
  - `docker compose ps` timed out.
  - `curl -sS http://localhost:5100/healthz` timed out.
  - `curl -sS -F 'file=@samples/intakes/chatgpt-sample-general-intake.pdf' -F 'templateId=general-assistance' -H 'X-Tenant-Id: tenant-a' http://localhost:5100/intakes` timed out.
- Deferred item for next run: perform live intake -> extraction -> review/finalize smoke and confirm correlation/metrics behavior.

### Next-step gate
- Issue #5 is complete (merged with post-merge audit clean). Move to issue #6 next.

## Issue #6 — Add representative test suite: unit, API integration, worker, UI smoke

- **Session file:** [Issue #6 — Representative test suite](sessions/6-dev.md)
- **Branch:** `6-representative-test-suite`
- **Implementation commit:** `479e216a83c104046e7da97a514ff8e6655ae7d0` (`test: add representative trust-boundary suite`)
- **PR:** #17 (`https://github.com/open-horizon-labs/northwoods/pull/17`)
- **Merged:** yes (merge commit `7142ce5e7cbf06e55bf474bff501e5264f78fdc2`)
- **Issue status:** closed (`2026-03-27T06:16:28Z`)

### Major decisions
- Added worker unit tests for confidence threshold gates and consensus merge behavior using internal test hooks (`Worker.Testing.cs`).
- Added tenant context tests for `DbConnectionFactory.OpenSessionAsync` validating `app.tenant_id` scoping, `app_user` role restriction, and document-level RLS isolation by tenant.
- Added API runtime integration tests for upload -> worker terminal transition and review/finalize workflow with cross-tenant access checks and tenant-scoped metrics assertions.
- Added unified test entrypoints in root scripts: `pnpm test:unit`, `pnpm test:runtime`, and `pnpm test`.
- Deferred Playwright UI smoke automation explicitly via `test:web-smoke` placeholder to keep trust-boundary coverage prioritized in run window.

### Verification run
- `pnpm test`
- `pnpm check`

### Code review findings and resolutions
- Post-merge audit endpoints:
  - `gh api repos/open-horizon-labs/northwoods/pulls/17/comments --paginate` → `[]` (no inline review findings).
  - `gh api repos/open-horizon-labs/northwoods/issues/17/comments --paginate` → `[]` (no issue-thread findings).
- Unaddressed external findings: none.
- Follow-up PR required: no.

### Blockers / deferred verification
- Delivery spot-check commands requiring a live runtime were blocked in this environment:
  - `curl -sS --max-time 10 http://localhost:5100/healthz` timed out.
  - `docker compose ps` timed out.
  - `docker info` timed out.
  - `curl` auth+upload smoke (`/auth/login` then `POST /intakes`) timed out.
- Runtime-dependent UI smoke remains deferred pending stable local runtime + Playwright harness wiring.

### Next-step gate
- Issue #6 is complete and merged with post-merge audit clean; proceed to issue #7.

## Issue #7 — Write architecture rationale document with diagram

- **Session file:** `.oh/sessions/7-dev.md`
- **Branch:** `7-architecture-rationale`
- **Implementation commit:** `cbf652d6407f1d5c8a8af9302a18f856a3dd365e` (`docs: add unified architecture rationale`)
- **PR:** #18 (`https://github.com/open-horizon-labs/northwoods/pull/18`)
- **Merged:** yes (merge commit `71e3db54f0ce22cabc3d63b8f0a79c33a29c17f9`)
- **Issue status:** closed (`2026-03-27T06:48:16Z`)

### Major decisions
- Created [Northwoods Architecture Rationale](../docs/architecture.md) as a single CTO-readable architecture rationale source instead of spreading rationale across ADR fragments.
- Included Mermaid architecture diagram covering users, frontend, API, extraction worker, Postgres (RLS + retrieval), and MinIO boundaries.
- Documented component responsibilities and explicit non-responsibilities for frontend, API, worker, Postgres, and MinIO.
- Captured template/extraction model rationale with append-only attempt history and confidence-tier review gating.
- Captured hybrid RAG strategy (vector + FTS + trigram + structured boosts) and why retrieval remains Postgres-centered.
- Documented multi-tenancy enforcement path (JWT claim -> DB session tenant context -> RLS backstop) and operational implications.
- Added explicit trade-offs/intentional omissions (polling vs Temporal, Postgres-centered retrieval, human-in-the-loop invariant, institutional UX restraint, AI as assistive).

### Verification run
- `pnpm check`

### Code review findings and resolutions
- Mandatory RNA index readiness check completed via `mcp_rna_repo_map` before phase-5 audit (index current).
- Post-merge audit endpoints:
  - `gh api repos/open-horizon-labs/northwoods/pulls/18/comments --paginate` → `[]` (no inline findings).
  - `gh api repos/open-horizon-labs/northwoods/issues/18/comments --paginate` → `[]` (no issue-thread findings).
  - `gh api repos/open-horizon-labs/northwoods/pulls/18/reviews --paginate` → `[]` (no review findings).
- Unaddressed external findings: none.
- Follow-up PR required: no.

### Blockers / deferred verification
- None for this issue; deliverable is docs-only and was validated through repository inspection.

### Next-step gate
- Issue #7 is complete and merged with post-merge audit clean; proceed to issue #8 only now.

## Issue #8 — Write self-assessment: completed, missing, trade-offs, AI usage

- **Session file:** [Dev Pipeline -- Write self-assessment: completed, missing, trade-offs, AI usage](sessions/8-dev.md)
- **Branch:** `8-self-assessment`
- **Commit:** `a05ce41` (`docs: add issue 8 self-assessment`)
- **PR:** #19 (`https://github.com/open-horizon-labs/northwoods/pull/19`)
- **Merged:** yes (merge commit `a5cf16a1cd39ecb19bd33e27eb31c78ca9bf5781`)
- **Issue status:** closed (`2026-03-27T06:57:40Z`)

### Major decisions
- Deliverable is documentation-only but includes explicit completeness/deferment and rubric score calibration.
- Added [Northwoods Self-Assessment (Issue #8)](../docs/self-assessment.md) with:
  - Completed work summary and operational boundaries
  - Explicit missing/deferred items with rationale and blockers
  - Key design trade-offs and their consequences
  - Reviewer-rubric score reflections
  - Concrete AI usage examples with example prompts

### Verification outcomes
- PR #19 and issue #8 were merged; issue closed at `2026-03-27T06:57:40Z`.
- Runtime smoke is still not executable in this environment due platform responsiveness limits:
  - `docker compose ps` (timeout)
  - `docker info` (timeout)
  - `curl --max-time 10 -sS http://localhost:5100/healthz` (timeout)
  - `curl --max-time 10 -sS -F 'file=@samples/intakes/chatgpt-sample-general-intake.pdf' -F 'templateId=general-assessment' -H 'X-Tenant-Id: tenant-a' http://localhost:5100/intakes` (timeout)
- Post-merge comment audit for PR #19:
  - `gh api repos/open-horizon-labs/northwoods/pulls/19/comments --paginate` -> `[]`
  - `gh api repos/open-horizon-labs/northwoods/issues/19/comments --paginate` -> `[]`
  - `gh api repos/open-horizon-labs/northwoods/pulls/19/reviews --paginate` -> `[]`
- Unresolved external findings: none.

### Next-step gate
- Issue #8 is merged and verified by this pass. Remaining runway:
  - Execute live intake/review smoke when local docker/curl responsiveness returns.
  - Complete remaining open issues #9–#11.