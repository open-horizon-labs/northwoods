# Dev Pipeline -- Fix extraction confidence: remove mock provider defaults, implement ADR 005 confidence tiers, escalate on low confidence
**Issue:** #42
**PR:** (pending)
**Started:** 2026-03-27T21:30:00Z

## Phase 1: Problem Statement
Issue #42 has a clear problem statement with 4 sub-problems and acceptance criteria. No reframing needed.

Key problems:
- P1: MockTesseractProvider runs by default and fabricates data
- P2: No escalation on low confidence (only on exceptions)
- P3: ADR 005 confidence tiers not implemented (everything goes to review_ready)
- P4: Provider agreement/disagreement test coverage thin

## Phase 2: Solution Space
**Selected:** Direct implementation of all 4 fixes (redesign level)

Changes required:
1. `appsettings.json` - flip `UseOpenAiVision` default to `true`
2. `Worker.cs` - BuildProviders: add `UseMockProvider` config, MockTesseract only runs when explicitly enabled
3. `Worker.cs` - CallWithFallback: parse nano response, compute avg confidence, escalate to mini if < 0.75
4. `Worker.cs` - CallWithFallback: separate transient/capability/hard error handling
5. `Worker.cs` - ExtractDocument: after consensus, compute min field confidence, set status per ADR 005 tiers
6. `init.sql` - add `completed` to status CHECK, add `requires_attention` column
7. `Models.cs` - add `Completed` to ProcessingStatus enum
8. `Program.cs` - handle `completed` in ParseStatus
9. `types.ts` / `statusLabel` - handle Completed status in frontend
10. Tests - 6+ new test cases
11. ADR 005 - update to reflect shipped behavior

## Phase 3: Execute
(in progress)

## Phase 4: Ship
(pending)

## RNA Tool Friction Log
| Phase | Tool | What happened | Workaround | Severity |
|-------|------|---------------|------------|----------|
