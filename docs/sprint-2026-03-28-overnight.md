# Sprint: 2026-03-28 Overnight

## Overview
Two rounds of automated review, issue creation, and dev-pipeline fixes. All work driven by parallel research agents (adversarial review, code quality audit, docs clarity, friction testing) followed by sequential dev-pipeline-oversight agents fixing each issue through PR merge.

---

## Round 1 — Pre-submission quality pass

### Research findings
- **Adversarial review**: 5 recommended issues. CRITICAL: review queue returned all documents regardless of status. HIGH: LoginValidationTests tested non-existent behavior. HIGH: Missing HNSW vector index.
- **Code quality audit**: 19 items (H1-H7 high, M1-M9 medium, L1-L4 low). Key: dead code (SubmissionsPage, EscalateThreshold, pg_class checks), status label conflation (Completed shown as Finalized), provider ordering non-deterministic.
- **ADR compliance**: ADR 002 (Temporal) correctly marked deferred. ADR 004 worker RLS exemption documented. ADR 005 stage names outdated.
- **Docs clarity**: 10 factual errors across README, self-assessment, ADRs, architecture doc. Wrong template names, misleading OpenAI setup instructions, stale Temporal references.
- **Playwright CSS check**: v0.4.2 CSS fix confirmed working. Site fully styled, zero console errors.

### Issues created and fixed

| Issue | Title | PR | Status |
|-------|-------|-----|--------|
| #144 | CRITICAL: Review queue shows all documents | #150 | Merged |
| #145 | Fix LoginValidationTests | #151 | Merged |
| #146 | HNSW index + remove seed_corpus.sql + RAG cold-start docs | #152 | Merged |
| #147 | Dead code cleanup batch (-425 lines) | #153 | Merged |
| #148 | Status labels, provider ordering, dead ternary, ADR 005 | #154 | Merged |
| #149 | Playwright e2e smoke tests in CI | #155 | Merged |

### Docs improvements (PR #143)
- Fixed template names in self-assessment
- README: .env not .env.local, clarified extraction behavior
- ADR 002/004 updates for accuracy
- Architecture diagram label corrected
- Fuzz reports marked as historical
- User guides created (intake worker, reviewer, admin)

**Tagged: v0.5.0**

---

## Round 2 — Deep quality pass

### Research findings

#### Friction testing (every page, every role, both tenants)
- 0 blockers, 1 HIGH (transient wrong-password hang), 5 MEDIUM, 7 LOW
- Key: All blank forms showed "No printable form available". Search had no empty state. Template slugs shown instead of names. Mobile layout overlap. Admin session lost on refresh.

#### Code audit (22 findings)
- 5 HIGH: ReviewerDashboard 1515 lines, ReviewEndpoints 130+ line functions, ExtractDocument 200 lines, Admin role excluded from auth restore, worker bypasses RLS
- 6 MEDIUM: Duplicate OpenAI response parsers, duplicate embedding generation, no HttpClient timeouts
- 11 LOW: Various naming, stale comments, dead types

#### Docs clarity (28 findings)
- 1 HIGH: architecture.md wrong escalation threshold (0.82 → 0.75)
- 10 MEDIUM: Incomplete API table, stale Playwright claims in 3 docs, missing audit event, wrong seed data reference, outdated model names in ADR 005
- 17 LOW: Minor consistency gaps

#### Adversarial review (exercise.md requirements)
- All 10 requirements: **PASS**
- Minor gaps: Blank PDFs not seeded (fixed), no Swagger UI (fixed), no structured search filters (acceptable)
- Strongest areas: Multi-tenancy with RLS, hybrid 5-signal RAG, extraction pipeline depth

### Issues created and fixed

| Issue | Title | PR | Status |
|-------|-------|-----|--------|
| #156 | Admin auth restore + seed blank PDFs | #160 | Merged |
| #157 | Docs accuracy batch (threshold, Playwright, API table, models) | #161 | Merged |
| #158 | Swagger/Scalar UI + search empty state + queue name fallback | #162 | Merged |
| #159 | Extract components + consolidate duplicates + HttpClient timeouts | #163 | Merged |

**Tagged: v0.6.0**

---

## Metrics

| Metric | Value |
|--------|-------|
| Issues created | 10 (Round 1: 6, Round 2: 4) |
| PRs merged | 11 (Round 1: 7, Round 2: 4) |
| Lines removed (net) | ~1,800+ (seed_corpus.sql alone: -1,600; dead code: -425; component dedup: -15) |
| Lines added (net) | ~1,200 (tests, guides, components, blank PDF seeding) |
| Research agents run | 9 (5 Round 1, 4 Round 2) |
| Dev-pipeline-oversight agents run | 10 |
| CI checks | All green (Build, Test 32/32, Playwright Smoke, ADR Compliance, Secret Scan) |

## Open items not addressed
- #133: Surface document ID in extraction failure notifications (deferred)
- #134: Resizable PDF/form panel split (deferred)
- CQ-023: Worker bypasses RLS for polling query (known, documented in ADR 004)
- Mobile responsive layout needs work (detail panel overlap on narrow screens)
- No structured search filters (date range, template type) — acceptable for exercise scope
