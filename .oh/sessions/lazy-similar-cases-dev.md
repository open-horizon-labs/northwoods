# Dev Pipeline -- Lazy-load similar cases in review detail

**Issue:** (no GH issue -- description provided)
**PR:** (filled in Phase 3)
**Started:** 2026-03-28

## Phase 1: Problem Statement

**Problem:** `GET /reviews/{id}` runs `FindSimilarCasesAsync` (which may call OpenAI) before responding, causing multi-second load times. The UI shows "Loading review…" for several seconds, blocking the reviewer from seeing extracted fields.

**Outcome alignment:** `bring-similar-cases-into-review`

**Acceptance criteria:**
- `GET /reviews/{id}` responds quickly; `similarCases` is always `[]`
- New `GET /reviews/{id}/similar-cases` runs `FindSimilarCasesAsync` and returns `IReadOnlyList<SimilarCaseItem>` (same auth: Reviewer only)
- Frontend adds `api.getSimilarCases(accessToken, reviewId)` in `api.ts`
- `ReviewerDashboard` fires second fetch after review detail renders; shows "Loading..." placeholder in similar cases panel while in-flight; on error shows nothing (doesn't block main review)
- Existing `SimilarCaseItem` type and UI rendering unchanged

## Phase 2: Solution Space

**Selected approach:** Split endpoint (Reframe)

The problem is architectural: the heavy operation is chained to the fast one. The fix is to separate concerns cleanly:
- Backend: extract the `FindSimilarCasesAsync` call into its own `GET /reviews/{id}/similar-cases` endpoint
- Backend: `GET /reviews/{id}` returns `similarCases: []` always (fast path)
- Frontend: fire second fetch after detail renders; update state when it resolves

**Why not band-aid (timeout / background task):** A timeout still blocks the response. A background task with polling is more complex with no benefit over a second fetch.
**Why not redesign (websockets / SSE):** Overkill. A second fetch is idiomatic REST and keeps the client simple.

## Phase 3: Execute

### Changes

**Backend** (`src/Services/Northwoods.Api/Endpoints/ReviewEndpoints.cs`):
1. In `GET /reviews/{id}`: remove `FindSimilarCasesAsync` call, pass `Array.Empty<SimilarCaseItem>()` to `ReviewDetailResponse`
2. Add `GET /reviews/{id}/similar-cases`: same auth guard, open session, load fields, call `FindSimilarCasesAsync`, return `Results.Ok(similarCases)`

**Frontend** (`apps/web/src/api.ts`):
- Add `getSimilarCases(accessToken, reviewId)` returning `SimilarCase[]`

**Frontend** (`apps/web/src/pages/ReviewerDashboard.tsx`):
- Add `similarCases` and `similarCasesLoading` state in `ReviewerDashboard`
- After `setReviewDetail(detail)` in `loadReview`, fire async fetch for similar cases
- Pass `similarCases` and `similarCasesLoading` to `ReviewDetail`
- In `ReviewDetail`: replace `review.similarCases.length > 0` guard with prop-based similar cases; show "Loading..." while in-flight

## Phase 4: Ship

(filled after execution)

## RNA Tool Friction Log
| Phase | Tool | What happened | Workaround | Severity |
|-------|------|---------------|------------|----------|
