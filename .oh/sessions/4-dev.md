# Dev Pipeline -- Implement real JWT authentication with role-based access control
**Issue:** #4
**PR:** #13 (`https://github.com/open-horizon-labs/northwoods/pull/13`)
**Merge commit:** `6c6cd69ec6fd9f784e66a4873ab043240d78bb12`
**Started:** 2026-03-27T02:00:00Z
**Completed:** 2026-03-27T03:10:00Z

## Phase 1: Problem Statement
- Source issue: Replace header-based dev-token flow with real JWT auth and enforce role-based access control.
- Target behavior: login returns signed JWT with tenant/user/role claims; protected endpoints derive tenant identity from claims; authorization rules enforce intake/review permissions.

## Phase 2: Solution Space
- Implemented HS256 signed JWT issuance at `/auth/login` with explicit issuer/audience/lifetime config.
- Added ASP.NET Core JWT bearer authentication and endpoint-level authorization.
- Replaced request header identity extraction with claims parsing (`tenant_id`, `user_id`, `role`) to preserve tenant-safe DB session scoping.
- Frontend updated to store/use `accessToken` on every protected API call via `Authorization: Bearer`.

## Phase 3: Execute
- API:
  - Added `Microsoft.AspNetCore.Authentication.JwtBearer` package.
  - Added token validation + claim parsing helpers and removed `X-Tenant-Id`/`X-User-Role` dependency.
  - Enforced role checks:
    - Upload (`POST /intakes`): `IntakeWorker` always, `Reviewer` optionally by config (`Auth:ReviewerCanUpload`).
    - Finalize (`POST /reviews/{id}/finalize`): `Reviewer` only.
- Contracts:
  - `LoginRequest.Role` changed to optional for compatibility while server now resolves role from DB.
- Frontend:
  - Login request no longer sends role.
  - API client now sends bearer token for all protected endpoints.
  - App flow switched from tenant header usage to token-based calls.
- Config:
  - Added JWT env defaults to `.env.example` and `docker-compose.yml` API service env.

## Phase 4: Ship
- Branch: `4-jwt-auth-rbac`
- Implementation commit: `aac42a4437bb77458c37f079fafa1652730a0b99` (`feat: implement JWT auth and role-based access control`)
- PR created and merged: #13

### Validation run
- `dotnet build src/Northwoods.slnx` ✅
- `pnpm check` ✅
- `docker compose up -d postgres minio api worker` ❌ timeout (docker daemon unresponsive)
- `docker compose ps` ❌ timeout (environmental)

## Phase 5: Oversight (post-merge comment audit)
- PR review comments (`pulls/13/comments`): none.
- PR issue comments (`issues/13/comments`): one CodeRabbit "review in progress" status note only; no actionable findings.
- PR reviews: none.
- Unaddressed external findings: none.
- Follow-up PR required: no.

## Delivery Verification
- Feature touches intake/review workflow. Live smoke test was attempted but blocked by unavailable docker runtime.
- Deferred verification command for next available runtime:
  - `curl -sS -F 'file=@samples/intakes/chatgpt-sample-general-intake.pdf' -F 'templateId=general-assistance' -H 'Authorization: Bearer <token>' http://localhost:5100/intakes`

## Security concessions / risks
- Existing user seed model still stores/dev-checks plaintext-equivalent password values (`VerifyPassword` fixed-time compare). JWT transport/authz is now real; password storage remains non-production grade and should be migrated to salted hash in a follow-up.

## RNA Tool Friction Log
| Phase | Tool | What happened | What you did instead | Severity |
|-------|------|---------------|----------------------|----------|
| Post-merge audit | GH API | No external findings beyond bot status note. | Verified all comment endpoints and PR review state directly. | low |

## Code Review Findings
- No actionable external findings were posted on PR #13 before/after merge.
