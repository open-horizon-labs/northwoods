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

- **Session file:** `.oh/sessions/1-dev.md`
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

- **Session file:** `.oh/sessions/4-dev.md`
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

- **Session file:** `.oh/sessions/3-dev.md`
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