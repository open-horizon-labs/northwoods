# Browser Fuzz Report — Northwoods Web App

**Date:** 2026-03-27  
**Target:** http://127.0.0.1:4173 (Vite dev server proxying to http://localhost:5100)  
**Tool:** playwright-cli (headless Edge)  
**Scope:** All user-facing flows on the single-page intake review console  

---

## Summary

| ID | Area | Severity | Title |
|----|------|----------|-------|
| B-01 | Auth | Critical | Auth token lost on page reload / back navigation |
| B-02 | Auth | High | No client-side input validation on login form |
| B-03 | Auth | High | Authenticated endpoints all return 401 after valid login (F-01 propagation) |
| B-04 | Upload | Medium | `.exe` accepted by file chooser — `accept` attribute bypass |
| B-05 | Upload | Medium | Zero-byte file selected without client-side validation |
| B-06 | Search | Medium | No client-side length limit on search query — 414 surfaced raw |
| B-07 | Search | Low | Search query sent as GET query param (no length cap) |
| B-08 | Nav | Low | Back navigation destroys auth state without warning |
| B-09 | Console | Info | All authenticated API calls produce console errors |

---

## Findings

### B-01 — Auth token not persisted (Critical)

**Test:** After successful login (200 OK from `/api/auth/login`), checked `localStorage`, `sessionStorage`, and cookies.

**Observed:** All storage mechanisms empty. No token persisted anywhere. The JWT (if returned) lives only in React component state.

**Impact:** 
- Any page reload, browser restart, or back navigation destroys the session silently.
- In a production workflow, workers processing many intake forms daily would lose their session on accidental reload with no warning.
- The "Signed in to tenant-a as Intake Worker" confirmation message is shown even though subsequent API calls fail immediately — misleading the user.

**Reproduction:**
```
1. Fill credentials and click Sign in
2. Confirm "Signed in to tenant-a as Intake Worker" message appears
3. Run: playwright-cli localstorage-list  → "No localStorage items found"
4. playwright-cli cookie-list             → "No cookies found"
5. playwright-cli sessionstorage-list     → "No sessionStorage items found"
```

**Expected:** Token stored in `sessionStorage` or `httpOnly` cookie with appropriate expiry. Session survives reload.

---

### B-02 — No client-side validation on login form (High)

**Tests and results:**

| Input | Tenant | Email | Password | Result |
|-------|--------|-------|----------|--------|
| Empty all fields | `` | `` | `` | Submitted to server → 401 |
| XSS in email | `tenant-a` | `<script>alert(1)</script>` | `dev` | Submitted → 401. No XSS execution. |
| SQL injection in tenant | `' OR '1'='1` | `worker@sunrise.example` | `dev` | Submitted → 401 |
| 10K char email | `tenant-a` | `aaaa...@example.com` | `dev` | Submitted → 401 |
| Unicode/emoji | `🏠テナント` | `用户@例子.测试` | `пароль🔑` | Submitted → 401 |

**Observed:** No client-side rejection for any of the above. Every malformed input is submitted to the server. The server returns 401, which the UI displays. No HTML rendering of injected content observed (React escapes correctly).

**Impact:** Missing `required` HTML5 constraints and format validation mean unnecessary API traffic. The email field lacks `type="email"` format enforcement. No `maxlength` on any input.

**Note:** XSS payloads are not executed — React's JSX escaping prevents rendering. This is correct behaviour. The finding is about missing validation, not injection risk at the UI layer.

---

### B-03 — All authenticated endpoints return 401 after valid login (High)

**This is a direct browser-observable consequence of API finding F-01 (JWT claim mapping broken).**

**Observed:** After login returns 200 OK:
- `GET /api/templates` → 401 Unauthorized (alert shown: "No templates are available")
- `GET /api/review-queue` → 401 Unauthorized (alert shown in queue section)
- `GET /api/search?q=...` → 401 Unauthorized for every search query

**Network log entry:**
```
[POST] /api/auth/login => [200] OK
[GET] /api/templates => [401] Unauthorized
[GET] /api/review-queue => [401] Unauthorized
```

**UI behaviour:** The app shows "Signed in to tenant-a as Intake Worker" and "401 Unauthorized" alert boxes simultaneously — contradictory state visible to the user. The upload button, template dropdown, and refresh queue button remain disabled, making the entire intake workflow inaccessible.

**Impact:** Complete functional blockage. No upload, no review, no search is possible.

---

### B-04 — File type restriction bypassable via programmatic file injection (Medium)

**Test:** The file input has `accept=".pdf,image/*"`. Uploaded `test.exe` (MIME type `application/octet-stream`) programmatically via `fileChooser.setFiles(["test.exe"])`.

**Observed:** The `.exe` file was accepted by the file chooser without rejection. The button label remained active (`[active]` state). No client-side error was shown in alert `e68`. Upload button remained disabled (due to B-03, not due to type rejection).

**Note:** The `accept` attribute provides OS-level filtering in the native file picker dialog, but Playwright bypasses the dialog entirely. In production, a malicious user could programmatically inject a file via JS. Server-side MIME validation is required to be the enforced boundary.

**Impact (conditional):** If B-03 is fixed and upload becomes functional, server must validate MIME type server-side. `accept=".pdf,image/*"` alone is not a security boundary.

---

### B-05 — Zero-byte file accepted without client-side validation (Medium)

**Test:** Uploaded `zero.pdf` (0 bytes) via file chooser.

**Observed:** No client-side error. Button showed `[active]`. Alert `e68` remained empty. Upload button stayed disabled (blocked by B-03). No "file is empty" validation message.

**Impact (conditional):** A zero-byte upload would fail at the extraction worker but without client-side rejection, the user would receive no immediate feedback. Downstream worker error handling for empty files needs verification.

---

### B-06 — No client-side length limit on search query — raw 414 surfaced (Medium)

**Test:** Filled search field with 10,000-character string and submitted.

**Observed:** Search button enabled (no `maxlength` attribute). Request sent as `GET /api/search?q=aaa...` (10K chars). Server returned `414 URI Too Long`. UI shows raw "414 URI Too Long" alert in the search results area.

**Network log:**
```
[GET] /api/search?q=aaa...(10000 chars) => [414] URI Too Long
```

**UI shows:** `alert [ref=e127]: 414 URI Too Long`

**Impact:** Exposes raw HTTP status codes to users. A 10K-char search query should be rejected client-side with a useful message (e.g., "Search query too long — max 500 characters"). Additionally, the search is implemented as a GET with a query parameter rather than a POST body, which limits maximum query length at the HTTP protocol level.

**Recommendation:** Add `maxlength="500"` (or reasonable limit) to search input. Show a human-readable error message rather than surfacing raw HTTP status.

---

### B-07 — Search query sent as GET query parameter (Low)

**Observed:** All search queries including XSS payloads (`<script>alert(1)</script>`) and SQL injection (`'; DROP TABLE intakes; --`) are URL-encoded and sent as:
```
GET /api/search?q=<encoded-payload>
```

**XSS encoding verified:** `%3Cscript%3Ealert(1)%3C%2Fscript%3E` — correct URL encoding.  
**SQLi encoding verified:** `%27%3B%20DROP%20TABLE%20intakes%3B%20--` — correct URL encoding.

**UI:** Payloads shown in the searchbox value. React renders them as plain text — no XSS execution.

**Impact (Low):** Search queries appear in browser history, server access logs, and proxy logs. For a system handling sensitive personal data (intake forms for vulnerable populations), query strings containing names or identifiers are logged in plain text. A POST body or `enctype` approach would reduce exposure. This is a design-level concern, not an acute injection risk.

---

### B-08 — Back navigation silently destroys auth state (Low)

**Test:** After login, clicked "go-back" in browser.

**Observed:**
- Page navigated to `about:blank`
- Auth state (in React memory) destroyed
- `goForward` returned to the app URL but session not restored
- User must sign in again with no warning

**Impact:** For a single-page app, this is expected behaviour when state is in-memory only (B-01). The combined finding is: persistent session storage would eliminate this. Standalone, this is Low — browser back-navigation leaving a SPA is a UX issue, not a security one.

---

### B-09 — Console errors on every page load and interaction (Informational)

**Observed:** 7 console errors accumulated during the initial load and login flow, all relating to the 401 auth breakdown. No unhandled React errors, no `undefined` exceptions, no render failures.

```
[ERROR] Failed to load resource: 401 Unauthorized @ /api/auth/login
[ERROR] Failed to load resource: 401 Unauthorized @ /api/templates
[ERROR] Failed to load resource: 401 Unauthorized @ /api/review-queue
```

The React DevTools suggestion appears twice in info logs (normal for development builds).

**Impact:** No crashes or React error boundaries triggered. Console noise is entirely from B-03.

---

## Tests with No Findings

| Test | Input | Result |
|------|-------|--------|
| XSS in review queue search | `<script>alert(1)</script>` | Rendered as plain text. No execution. |
| SQL injection in search | `'; DROP TABLE intakes; --` | URL-encoded. Server returns 401. |
| Unicode in login fields | Emoji + CJK + Cyrillic | Accepted and submitted. Server returns 401. No crashes. |
| Unicode in search | `🔍 こんにちは résumé Ñoño` | URL-encoded correctly. Server returns 401. |
| Empty search | `""` | Search button disabled. Correct behaviour. |
| Direct URL access to review section | `/#section-review` | Section visible but gated: "Sign in and choose a review-ready document". Correct behaviour. |
| Rapid tenant switching (4 rapid clicks) | Switching through all 4 presets | Each triggers a login attempt. No crashes or mixed-state errors. |

---

## Coverage Notes

- **Review interaction abuse** (XSS in review fields, rapid double-click finalize, large field values): Unable to test. The review section requires a document in the review queue, which requires a functional upload + worker extraction pipeline. This is blocked by B-03. Tests are deferred until auth is fixed.

- **Upload form**: Upload button is disabled due to missing templates (B-03). Server-side file type and size validation could not be exercised.

---

## Environment

- Stack health at test time: postgres, minio, api, worker containers healthy
- API fuzz report F-01 (JWT claim mapping) is confirmed to cascade into all browser-facing flows
- Screenshots saved: `fuzz-initial-state.png`, `fuzz-post-login.png`
