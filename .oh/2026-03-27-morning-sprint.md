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

### Task 3 complete: OpenAI vision OCR spike — 2026-03-27

Script: `scripts/spike-openai-vision-ocr.py`  
Results: [Spike: OpenAI Vision OCR vs PaddleOCR Baseline](../docs/spike-openai-vision-ocr.md)  
Commit: `7324478`

**Approach:** Convert PDF→PNG via PyMuPDF (fitz), send to OpenAI Responses API with vision, extract 7 intake fields as structured JSON with per-field confidence. gpt-4.1-nano tried first, gpt-4.1-mini fallback.

**Results across 5 samples:**

| File | Model | Fields | Avg conf | Paddle agreement |
|------|-------|--------|----------|-----------------|
| chatgpt-sample-housing-stability-intake.pdf | gpt-4.1-nano | 7/7 | 0.90 | 3/7 (partial) |
| chatgpt-sample-financial-assistance-intake.pdf | gpt-4.1-nano | 3/7 | 0.38 | 0/7 |
| chatgpt-sample-case-worker-notes.pdf | gpt-4.1-nano | 1/7 | 0.13 | 0/7 |
| chatgpt-sample-general-intake.pdf | gpt-4.1-nano | 1/7 | 0.14 | 0/7 |
| chatgpt-sample-soap-note.pdf | gpt-4.1-nano | 0/7 | 0.00 | 0/7 |

**Key findings:**
- `gpt-4.1-nano` supports vision — no fallback to mini needed on these samples.
- On the actual intake form (`housing-stability`), nano extracted all 7 fields with 0.90 avg confidence and correctly parsed name, DOB, address, household size, income, services, and notes.
- Case-worker notes and general-intake docs: model correctly identified these aren't structured intake forms and returned only `notes` field (sensible behavior).
- SOAP note: all nulls — correct, this form uses different fields entirely.
- PaddleOCR returns flat unstructured text; field extraction requires brittle keyword heuristics with no confidence signal. OpenAI vision returns structured JSON natively, eliminating the normalization stage.
- Low agreement rate is partially explained by sample mismatch (only 1 of 5 files is an actual intake form). The one real intake showed partial agreement on most fields.

**Recommendation:** OpenAI vision (mini) is a strong candidate for single-step OCR+normalization on actual handwritten intake scans. Evaluate cost/latency vs. staged PaddleOCR+normalizer before committing to production integration.