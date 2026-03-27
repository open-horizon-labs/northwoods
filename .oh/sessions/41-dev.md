# Dev Pipeline -- Camera Capture for Mobile Intake Workers
**Issue:** #41
**PR:** #46 (merged)
**Started:** 2026-03-27T21:25:00Z

## Phase 1: Problem Statement
The mobile upload UX in WorkerDashboard forces intake workers to open a file picker to find a saved photo. Social workers photograph forms at point of service — the camera should open directly from the upload screen.

**Acceptance Criteria (from issue):**
- On mobile (375px+): two upload options — "Take Photo" (primary) and "Upload File" (secondary)
- "Take Photo" uses `capture="environment"` to open rear camera directly
- On desktop: file picker only, no camera option
- Touch targets ≥44px
- Accepted file types unchanged: images + PDFs
- Existing upload flow unchanged
- `pnpm check` passes

## Phase 2: Solution Space
**Selected:** Dual inputs with JS pointer detection (local optimum)
- useIsTouchDevice() hook via matchMedia('(pointer: coarse)') with live listener
- Touch: "Take Photo" (primary, capture=environment) + "Upload File" (secondary)
- Desktop: existing file input unchanged
- Both paths feed same selectedFile state

## Phase 3: Execute
- Implemented in `apps/web/src/pages/WorkerDashboard.tsx`
- Added btnSecondary style constant
- Fixed a11y regression: added aria-labelledby to desktop input after label->p change
- pnpm check passes (TypeScript + Vite build + .NET build)

## Phase 4: Ship
- /review posted: PASS (1 finding fixed during impl)
- /dissent posted: 4 challenges evaluated, all acceptable
- CodeRabbit: APPROVED, no actionable findings
- Merged via squash, branch deleted

## Phase 5: Post-Merge Comment Audit
- CodeRabbit: 0 actionable findings (approved)
- /review gate: present
- /dissent gate: present
- Human comments: 0
- Inline review comments: 0
- **Unaddressed findings: 0**
- No followup PR needed

## RNA Tool Friction Log
| Phase | Tool | What happened | Workaround | Severity |
|-------|------|---------------|------------|----------|
