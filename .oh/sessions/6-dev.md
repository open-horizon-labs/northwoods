# Issue #6 — Representative test suite

## Scope
- Add representative tests focused on trust boundaries and workflow transitions.
- Keep runtime-dependent coverage guarded so the suite still runs when services are unavailable.

## Implemented coverage
- Worker unit tests for confidence thresholds and consensus merge behavior.
- Tenancy tests for `DbConnectionFactory.OpenSessionAsync` tenant context + RLS isolation checks.
- API runtime integration tests for:
  - upload -> worker transition into terminal processing status,
  - review queue + finalize flow,
  - cross-tenant access isolation and tenant-scoped metrics divergence.
- Unified root scripts for `test:unit`, `test:runtime`, and `test`.

## Deferred
- UI smoke automation (Playwright) deferred; root script `test:web-smoke` documents follow-up requirement.

## Verification
- `pnpm test`
- `pnpm check`
