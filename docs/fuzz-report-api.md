# API Fuzz Report — Northwoods API

**Date:** 2026-03-27  
**Target:** `http://localhost:5100`  
**Stack:** Docker Compose — `northwoods-api-1` (ASP.NET Core / .NET 10-preview), Postgres 18 with pgvector, MinIO  
**Method:** Black-box boundary and injection testing using `curl` and crafted HTTP requests, supplemented by white-box source analysis

---

## Executive Summary

Eleven findings were identified across six categories. One finding is Critical and blocks all authenticated functionality. Several High findings in the login endpoint allow denial-of-service via unhandled exceptions. Medium and Low findings affect information exposure and missing hardening controls.

All authenticated endpoints (`/templates`, `/intakes/{id}`, `/review-queue`, `/reviews/{id}`, `/reviews/{id}/finalize`, `/search`, `/cases/{personKey}`, `/metrics`) returned HTTP 401 throughout testing. This is traced to a claim-mapping incompatibility in `GetAuthContext` (see Finding 1). As a result, tenant isolation, input validation for authenticated paths, and file upload abuse testing could not be exercised at runtime. Source analysis was used to evaluate those categories where live testing was blocked.

---

## Findings

### F-01 — CRITICAL: All Authenticated Endpoints Return 401 (Complete Auth Breakdown)

**Category:** Auth Boundary  
**Endpoints:** All `.RequireAuthorization()` routes  
**Severity:** Critical

**Observed:** `POST /auth/login` issues a syntactically and cryptographically valid JWT. The JWT bearer middleware logs `"Successfully validated the token"` and `"Bearer was successfully authenticated"` for every request to a protected endpoint. Despite this, every protected handler immediately returns HTTP 401.

**Root cause (source analysis):** `GetAuthContext(ClaimsPrincipal principal)` calls `principal.FindFirstValue("role")`. In .NET 8+ the `JwtBearerHandler` has `MapInboundClaims = true` by default, which remaps the `"role"` JWT claim to `ClaimTypes.Role` (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`) in the resulting `ClaimsPrincipal`. The lookup `FindFirstValue("role")` therefore returns `null`. `GetAuthContext` treats a null role as an invalid context and returns `null`. Every handler returns `Results.Unauthorized()` before any logic executes.

The issued token (confirmed by debug logging):
```
"role": "IntakeWorker"  -- issued correctly
```
The effective lookup in ClaimsPrincipal returns null because the claim type name has been remapped.

**Effect:** The entire API is operationally non-functional for authenticated users. No document can be uploaded, reviewed, searched, or finalized.

**Note:** `GET /intakes` is a non-existent route (the endpoint is `POST /intakes` only) and returns HTTP 405 regardless of authentication. This is a separate but related observation (see F-09).

---

### F-02 — High: Unhandled Exception (500) on Missing `password` Field in Login

**Category:** Input Validation  
**Endpoint:** `POST /auth/login`  
**Severity:** High

**Payload:** `{"email":"worker@sunrise.example","tenantId":"tenant-a"}` (no `password`)  
**Status returned:** `500 Internal Server Error`  
**Expected:** `400 Bad Request`

**Root cause:** When `password` is absent from the JSON body, `LoginRequest.Password` binds to `null`. The code calls `VerifyPassword(null, passwordHash)`, which executes `Encoding.UTF8.GetBytes(null)`, throwing `System.ArgumentNullException: Value cannot be null. (Parameter 's')`. This unhandled exception crashes the request context and surfaces as a 500 with no body.

**Log evidence:**
```
System.ArgumentNullException: Value cannot be null. (Parameter 's')
  at Program.VerifyPassword|0_19(String requestPassword, String passwordHash)
  at Program.cs:line 653
```

---

### F-03 — High: Unhandled Exception (500) on Missing `tenantId` Field in Login

**Category:** Input Validation  
**Endpoint:** `POST /auth/login`  
**Severity:** High

**Payload:** `{"email":"worker@sunrise.example","password":"dev"}` (no `tenantId`)  
**Status returned:** `500 Internal Server Error`  
**Expected:** `400 Bad Request`

**Root cause:** `LoginRequest.TenantId` binds to `null`. `OpenSessionAsync(null)` calls `cmd.Parameters.AddWithValue("@tid", null)`. Npgsql cannot infer the PostgreSQL type for a .NET null value and throws:
```
System.InvalidOperationException: Parameter '@tid' must have either its DbType,
NpgsqlDbType, DataTypeName or its Value set.
  at DbConnectionFactory.OpenSessionAsync(String tenantId) in DbConnectionFactory.cs:line 49
```

The same failure occurs for an empty JSON body `{}`.

---

### F-04 — High: Unhandled Exception (500) on Null Byte in Login Email

**Category:** Input Validation  
**Endpoint:** `POST /auth/login`  
**Severity:** High

**Payload:** `{"email":"worker@sunrise.example\u0000evil","password":"dev","tenantId":"tenant-a"}`  
**Status returned:** `500 Internal Server Error`  
**Expected:** `400 Bad Request` or `401 Unauthorized`

**Root cause:** The null byte is passed directly to the SQL query as a parameter value. PostgreSQL rejects the UTF-8 encoding and throws:
```
Npgsql.PostgresException: 22021: invalid byte sequence for encoding "UTF8": 0x00
  Where: unnamed portal parameter $1
  at Program.cs:line 149
```
The exception propagates unhandled. An attacker can trigger this reliably to generate 500 responses, which is exploitable for service disruption and to generate noisy log entries masking other activity.

---

### F-05 — High: Passwords Stored and Compared as Plaintext

**Category:** Input Validation / Auth Boundary  
**Source:** `Program.cs` — `VerifyPassword`, `infra/postgres/init.sql`  
**Severity:** High

**Observation (source analysis):** The seed SQL stores `password_hash TEXT NOT NULL DEFAULT 'dev'` — the literal string `'dev'` is the stored credential. `VerifyPassword` compares `Encoding.UTF8.GetBytes(requestPassword)` byte-for-byte against `Encoding.UTF8.GetBytes(passwordHash)`. No hash function is used. Any party with read access to the `users` table retrieves usable credentials immediately.

This is consistent with an exercise environment but constitutes a High finding since the column is named `password_hash`, implying hashing was intended, and a migration to production with this schema in place would expose all credentials.

---

### F-06 — Medium: Login Returns 401 (not 400) for Missing `email` Field

**Category:** Input Validation  
**Endpoint:** `POST /auth/login`  
**Severity:** Medium

**Payload:** `{"password":"dev","tenantId":"tenant-a"}` (no `email`)  
**Status returned:** `401 Unauthorized`  
**Expected:** `400 Bad Request`

**Observation:** Missing `email` results in `LoginRequest.Email` being null. The SQL query `WHERE email = @Email AND tenant_id = @TenantId` finds no user (null match), so `user == default` → `Results.Unauthorized()`. This is semantically incorrect: a missing required field should be a client error (400), not an authentication failure (401). An attacker could enumerate which fields are required by observing the difference (400 vs 401) between field-absent and field-present-wrong-value cases.

---

### F-07 — Medium: No Rate Limiting on Login Endpoint

**Category:** Rate/Resource  
**Endpoint:** `POST /auth/login`  
**Severity:** Medium

**Test:** 20 sequential failed login attempts (`"password":"wrong"`) completed in 0.2 seconds. All returned 401. No 429 responses were observed.

**Observation:** No rate limiting, account lockout, or exponential backoff is applied to failed login attempts. This permits unrestricted password brute-force against any known email/tenant combination.

---

### F-08 — Medium: TRACE Method Accepted on `/healthz`

**Category:** Auth Boundary / Route Confusion  
**Endpoint:** `TRACE /healthz`  
**Severity:** Medium

**Payload:** HTTP TRACE method, no body  
**Status returned:** `200 OK`, body: `Healthy`

**Observation:** The TRACE method is not disabled. TRACE is associated with cross-site tracing (XST) attacks, where session cookies or auth headers can be reflected via TRACE when combined with a cross-domain request. Modern browsers block this but application servers should reject TRACE explicitly. More practically, TRACE exposing `Healthy` leaks internal health status to unauthenticated callers, confirming the server is running.

---

### F-09 — Low: `GET /intakes` Returns 405 Without Authentication (Route Leakage)

**Category:** Auth Boundary  
**Endpoint:** `GET /intakes`  
**Severity:** Low

**Payload:** None, no auth header  
**Status returned:** `405 Method Not Allowed`  
**Expected:** `401 Unauthorized` (consistent with all other unauthenticated attempts)

**Observation:** The router matches the `/intakes` path but rejects the GET method, returning 405 before the authentication middleware can return 401. This reveals to an unauthenticated caller that the path `/intakes` exists and only supports non-GET methods, enabling route enumeration without credentials. All other non-existent paths return 404. The difference (405 vs 404) is an information leak.

---

### F-10 — Low: `POST /reviews/{id}/finalize` Returns 400 on Missing Body Without Authentication

**Category:** Auth Boundary  
**Endpoint:** `POST /reviews/{id}/finalize`  
**Severity:** Low

**Payload:** No request body, valid JWT in Authorization header  
**Status returned:** `400 Bad Request`  
**Expected:** `401 Unauthorized`

**Observation:** When no body is provided, ASP.NET Core's minimal-API parameter binding rejects the request with 400 before the handler executes (and before `GetAuthContext` is checked). This reveals that the endpoint exists, accepts POST, and requires a JSON body — without needing a valid session. Combined with F-09, a caller can enumerate the full route map without credentials.

---

### F-11 — Low: MinIO File Key Uses Unsanitized Filename (Source Analysis)

**Category:** File Upload Abuse  
**Source:** `Program.cs` line 263  
**Severity:** Low

**Observation (source analysis):** The MinIO object key is constructed as:
```csharp
var fileKey = $"{authContext.TenantId}/{docId}/{file.FileName}";
```
`file.FileName` is taken directly from the multipart form without path normalization. A filename such as `../../secrets.pdf` or `../../../admin/config.json` would produce a key like `tenant-a/{guid}/../../secrets.pdf`. Whether this traverses outside the bucket depends on MinIO's path resolution behavior. The intake code could not be live-tested due to the auth breakdown (F-01), but the pattern is exploitable under correct auth. The risk is limited to the MinIO bucket (not the host filesystem), but cross-tenant key collisions or access to other tenants' objects under a shared bucket are possible if MinIO resolves `..` segments.

---

## Code-Analysis-Only Notes (Untestable Due to F-01)

The following observations are based on source analysis and could not be confirmed live due to the complete auth failure in F-01. They should be re-tested after F-01 is resolved.

**Tenant isolation design (good):** Each request opens a DB session via `OpenSessionAsync(tenantId)` which executes `SET LOCAL app.tenant_id = @tid` and `SET LOCAL ROLE app_user`. Postgres RLS policies on all tables enforce `tenant_id = current_setting('app.tenant_id', true)`. The design is sound. Cross-tenant access via JWT manipulation is blocked because the tenant_id in the JWT is bound to the user's tenant at login.

**IntakeWorker access to review-queue (concern):** `GET /review-queue` has no role check in the handler body — only `.RequireAuthorization()`. Any authenticated user (both IntakeWorker and Reviewer) would be able to list review queue items. This is probably not intended.

**VerifyPassword fixed-time comparison (good):** `CryptographicOperations.FixedTimeEquals` is used, preventing timing attacks. However, the comparison is against a plaintext stored password (F-05), so this protection is moot until actual hashing is implemented.

**SQL injection resistance (good):** All DB queries use parameterized Dapper calls. No string interpolation into SQL was observed. The null-byte crash (F-04) confirms the parameters are being passed to Postgres, not interpolated, since the error comes from Postgres rejecting the encoding — not from a parse failure.

---

## Test Matrix

| # | Category | Endpoint | Test | Status | Finding |
|---|----------|----------|------|--------|---------|
| 1 | Auth Boundary | GET /templates | No auth header | 401 | — |
| 2 | Auth Boundary | GET /intakes | No auth header | 405 | F-09 |
| 3 | Auth Boundary | GET /review-queue | No auth header | 401 | — |
| 4 | Auth Boundary | GET /search | No auth header | 401 | — |
| 5 | Auth Boundary | GET /templates | Invalid JWT signature | 401 | — |
| 6 | Auth Boundary | GET /templates | Expired JWT | 401 | — |
| 7 | Auth Boundary | GET /templates | Empty bearer value | 401 | — |
| 8 | Auth Boundary | GET /templates | Basic auth scheme | 401 | — |
| 9 | Auth Boundary | GET /templates | Token without Bearer prefix | 401 | — |
| 10 | Auth Boundary | GET /review-queue | IntakeWorker role | 401 | F-01 |
| 11 | Auth Boundary | POST /reviews/{id}/finalize | No body, valid JWT | 400 | F-10 |
| 12 | Tenant Isolation | POST /auth/login | Tenant-A user, tenant-B claim | 401 | — |
| 13 | Tenant Isolation | POST /auth/login | Non-existent tenant | 401 | — |
| 14 | Tenant Isolation | POST /auth/login | Empty tenantId | 401 | — |
| 15 | Input Validation | POST /auth/login | Valid login | 200 | — |
| 16 | Input Validation | POST /auth/login | Wrong password | 401 | — |
| 17 | Input Validation | POST /auth/login | SQL injection in email | 401 | — |
| 18 | Input Validation | POST /auth/login | SQL injection in tenantId | 401 | — |
| 19 | Input Validation | POST /auth/login | SQL injection in password | 401 | — |
| 20 | Input Validation | POST /auth/login | Null byte in email | 500 | F-04 |
| 21 | Input Validation | POST /auth/login | Unicode in email | 401 | — |
| 22 | Input Validation | POST /auth/login | Missing email field | 401 | F-06 |
| 23 | Input Validation | POST /auth/login | Missing password field | 500 | F-02 |
| 24 | Input Validation | POST /auth/login | Missing tenantId | 500 | F-03 |
| 25 | Input Validation | POST /auth/login | Empty JSON body `{}` | 500 | F-03 |
| 26 | Input Validation | POST /auth/login | Non-JSON body | 400 | — |
| 27 | Input Validation | POST /auth/login | Oversized email (500KB) | 401 | — |
| 28 | Input Validation | POST /auth/login | XSS in email | 401 | — |
| 29 | Type Confusion | POST /auth/login | Integer as email | 400 | — |
| 30 | Type Confusion | POST /auth/login | Array as email | 400 | — |
| 31 | Type Confusion | POST /auth/login | All null values | 500 | F-03 (same root) |
| 32 | Route Confusion | GET /auth/login | GET on POST-only route | 405 | — |
| 33 | Route Confusion | DELETE /intakes | DELETE on intakes | 405 | — |
| 34 | Route Confusion | GET /intakes/not-a-guid | Non-GUID in UUID path | 404 | — |
| 35 | Route Confusion | GET /intakes/12345 | Integer in UUID path | 404 | — |
| 36 | Route Confusion | GET /cases/../../etc/passwd | Literal path traversal | 404 | — |
| 37 | Route Confusion | GET /cases/..%2F..%2Fetc%2Fpasswd | URL-encoded path traversal | 401 | — |
| 38 | Route Confusion | TRACE /healthz | TRACE method | 200 | F-08 |
| 39 | Info Leak | GET /healthz | Unauthenticated health check | 200 | — |
| 40 | Info Leak | GET /metrics | No auth | 401 | — |
| 41 | Headers | POST /auth/login | CRLF in correlation ID | 200 | — |
| 42 | Headers | POST /auth/login | SQL in correlation ID | 200 | — |
| 43 | Headers | POST /auth/login | XSS in correlation ID | 200 | — |
| 44 | Rate Limit | POST /auth/login | 20 rapid failed logins | all 401 | F-07 |
| 45 | Auth Bug | All protected endpoints | Valid JWT | all 401 | F-01 |

---

## Findings Summary by Severity

| Severity | Count | IDs |
|----------|-------|-----|
| Critical | 1 | F-01 |
| High | 4 | F-02, F-03, F-04, F-05 |
| Medium | 3 | F-06, F-07, F-08 |
| Low | 3 | F-09, F-10, F-11 |

---

## Recommended Fix Order

1. **F-01 (Critical):** Set `MapInboundClaims = false` in `JwtBearerOptions`, or change `GetAuthContext` to use `ClaimTypes.Role` (`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`) for the role lookup. After fixing, re-run the full test suite since tenant isolation, file upload, and all other authenticated behaviors were untestable.

2. **F-02 / F-03 / F-04 (High, unhandled 500s):** Validate `LoginRequest` fields for null/empty before executing any DB or string operations. Add guard clauses at the top of the login handler: `if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.TenantId)) return Results.BadRequest(...)`. Use a data annotation or `[Required]` binding to reject malformed input before reaching handler code.

3. **F-05 (High):** Replace plaintext password storage and comparison with a proper password hashing scheme (Argon2 or BCrypt). This is an exercise constraint but must be resolved before any production use.

4. **F-07 (Medium):** Add rate limiting to `POST /auth/login` using ASP.NET Core's built-in `RateLimiter` middleware, keyed on client IP and/or email+tenant combination. Enforce a lockout policy after N failures within a time window.

5. **F-08 (Medium):** Disable TRACE and OPTIONS on routes that don't need them. For ASP.NET Core minimal APIs, map a catch-all TRACE handler that returns 405 or configure middleware to reject TRACE early.

6. **F-09 / F-10 (Low):** Accept as design reality or add auth middleware before route matching. The information leak is minor but consistent routing behavior (401 before any method or body check) is cleaner.

7. **F-11 (Low):** Normalize `file.FileName` using `Path.GetFileName()` before constructing the MinIO key to strip any directory components.
