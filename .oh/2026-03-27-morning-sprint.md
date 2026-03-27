# Morning Sprint — 2026-03-27

## Status at start
- All implementation issues (#1-#8, #11) merged
- Issue #2 (search/case view) merged as PR #22
- API fuzz report committed ([API Fuzz Report — Northwoods API](../docs/fuzz-report-api.md)) with 11 findings including critical auth breakdown (F-01)
- Open issues: #9 (review fix loop round 1), #10 (review fix loop round 2)
- Docker restarted after OrbStack crash; stack confirmed healthy

## Queued tasks (serial)
1. Browser fuzz testing via Playwright
2. Markdown wiki-link conversion across all docs
3. OpenAI vision OCR spike (nano then mini, compare against Paddle baseline)
4. AST/linter constraint checks for RLS and ADR compliance, wrapped in CI jobs

## Running Notes

(appended as tasks complete)

### Task 1 complete: Browser fuzz testing — 2026-03-27

Report: [Browser Fuzz Report — Northwoods Web App](../docs/fuzz-report-browser.md)

9 findings across 5 categories:

| ID | Severity | Title |
|----|----------|-------|
| B-01 | Critical | Auth token not persisted — lost on reload/back navigation |
| B-02 | High | No client-side input validation on login form |
| B-03 | High | All authenticated endpoints return 401 after valid login (F-01 cascade) |
| B-04 | Medium | `.exe` file type restriction bypassable via programmatic injection |
| B-05 | Medium | Zero-byte file accepted without client-side validation |
| B-06 | Medium | No length limit on search query — raw 414 surfaced to user |
| B-07 | Low | Search queries sent as GET params (logged in access logs/history) |
| B-08 | Low | Back navigation silently destroys auth state |
| B-09 | Info | Console errors on every page load (all from B-03/F-01) |

**Blocked tests:** Review interaction abuse and upload flow could not be exercised — blocked by B-03 (F-01 cascade). Deferred until auth is fixed.

**No XSS execution observed** in any field — React escaping works correctly throughout.


### Task 2 complete: Markdown wiki-link conversion — 2026-03-27

Converted all bare file path references in `.md` files to proper markdown hyperlinks.

**Files modified:**
- `README.md`: linked ADR 005, `docs/ai-tooling.md`, `.oh/ocr-confidence-tiering.md`
- `docs/ADRs/004-shared-postgres-tenancy-with-rls-backstop.md`: linked ADR 001–003 cross-references
- `docs/architecture.md`: linked all 5 ADRs, `reviewer-rubric.md`, `ai-tooling.md`
- `docs/self-assessment.md`: linked `architecture.md`, `ai-tooling.md`, `reviewer-rubric.md`
- `.oh/ocr-confidence-tiering.md`: linked ADR 005
- `.oh/2026-03-27-morning-sprint.md`: linked fuzz report files
- `.oh/2026-03-26-overnight-session.md`: linked session files and docs refs

**Skipped:** `.oh/sessions/5-dev.md` and `.oh/sessions/7-dev.md` — target files do not exist; linking would introduce broken links.

**Commit:** `e48dd58` (`docs: convert bare file path references to markdown links across all docs`)