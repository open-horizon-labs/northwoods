import { useCallback, useEffect, useState } from 'react'

import { api, userMessage } from '../api'
import type { LoginResponse, SearchResultItem, SimilarCase } from '../types'

// ── Ground truth from scripts/corpus/narrative.py ────────────────────────────

type ArcQuery = {
  label: string
  personKey: string
  searchName: string
  /** Person keys expected in the top-5 similar cases. */
  expectedPersonKeys: string[]
  /** Human description of the expectation. */
  expectation: string
  /** If true, any cross-tenant result is a failure regardless of expectedPersonKeys. */
  tenantIsolationCheck?: boolean
}

const ARC_QUERIES: ArcQuery[] = [
  {
    label: 'P019 Raymond Castillo (same-person identity)',
    personKey: 'P019',
    searchName: 'Raymond Castillo',
    expectedPersonKeys: ['P019'],
    expectation: 'Other P019 documents (v2 crisis -> v1 stable arc)',
  },
  {
    label: 'P039 Gloria Navarro (same-person identity)',
    personKey: 'P039',
    searchName: 'Gloria Navarro',
    expectedPersonKeys: ['P039'],
    expectation: 'Other P039 documents (v2 DV/overcrowded -> v1 apartment/stable)',
  },
  {
    label: 'P017 Carlton Hughes (cross-facility transfer)',
    personKey: 'P017',
    searchName: 'Carlton Hughes',
    expectedPersonKeys: ['P017'],
    expectation: 'P017 sunrise doc appears alongside P017 lakewood doc',
  },
  {
    label: 'P004 Brianna Kowalski (tenant isolation)',
    personKey: 'P004',
    searchName: 'Brianna Kowalski',
    expectedPersonKeys: [],
    expectation: 'No cross-tenant matches (P004 is v1-only, sunrise)',
    tenantIsolationCheck: true,
  },
]

// Sunrise people should never see lakewood results, and vice versa.
// P004 is sunrise-only. If we see lakewood-tenant person keys, that's a tenant isolation violation.
const SUNRISE_PERSON_KEYS = new Set([
  'P001', 'P002', 'P003', 'P004', 'P005', 'P006', 'P007', 'P008', 'P009', 'P010',
  'P011', 'P012', 'P013', 'P014', 'P015', 'P016', 'P017', 'P018', 'P019', 'P020',
])
const LAKEWOOD_PERSON_KEYS = new Set([
  'P021', 'P022', 'P023', 'P024', 'P025', 'P026', 'P027', 'P028', 'P029', 'P030',
  'P031', 'P032', 'P033', 'P034', 'P035', 'P036', 'P037', 'P038', 'P039', 'P040',
])

// ── Types ────────────────────────────────────────────────────────────────────

type QueryResult = {
  query: ArcQuery
  /** Document we used as the query anchor (first search result). */
  anchorDoc: SearchResultItem | null
  /** Similar cases returned by the review endpoint. */
  similarCases: SimilarCase[]
  /** Whether expected person keys appear in the top-5. */
  passed: boolean
  /** Whether a tenant isolation violation was detected. */
  tenantViolation: boolean
  /** Error during fetch, if any. */
  error: string | null
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function extractPersonKeyFromName(name: string): string | null {
  // Full corpus roster from scripts/corpus/people.py — all 40 people.
  const known: Record<string, string> = {
    // Sunrise (tenant-a)
    'marcus delgado': 'P001',
    'tamara okonkwo': 'P002',
    'jesse huang': 'P003',
    'brianna kowalski': 'P004',
    'darnell foster': 'P005',
    'yolanda reyes': 'P006',
    'anton petrov': 'P007',
    'keisha williams': 'P008',
    'rodrigo vega': 'P009',
    'fatima al-rashid': 'P010',
    'dennis achebe': 'P011',
    'luz morales': 'P012',
    'trevor banks': 'P013',
    'nadia thornton': 'P014',
    'elijah santos': 'P015',
    'priya nair': 'P016',
    'carlton hughes': 'P017',
    'sonya blackwood': 'P018',
    'raymond castillo': 'P019',
    'ingrid larsson': 'P020',
    // Lakewood (tenant-b)
    'theo mbeki': 'P021',
    'angela drummond': 'P022',
    'victor pham': 'P023',
    'monique dupont': 'P024',
    'hakeem osei': 'P025',
    'camille bouchard': 'P026',
    'dmitri volkov': 'P027',
    'imani jefferson': 'P028',
    'sergio gutierrez': 'P029',
    'wanda korhonen': 'P030',
    'calvin pope': 'P031',
    'esperanza cruz': 'P032',
    'tobias stern': 'P033',
    'alicia monroe': 'P034',
    'patrick adeyemi': 'P035',
    'giselle fontaine': 'P036',
    'bernard oduya': 'P037',
    'tasha greenfield': 'P038',
    'gloria navarro': 'P039',
    'kofi asante': 'P040',
  }
  const key = name.toLowerCase().trim()
  return known[key] ?? null
}

/** Check if a similar case is from a different tenant than the query person. */
function isCrossTenant(queryPersonKey: string, caseApplicantName: string): boolean {
  const caseKey = extractPersonKeyFromName(caseApplicantName)
  if (!caseKey) {
    // Unknown applicant — cannot verify tenant; flag as potential violation for safety
    return true
  }
  const queryIsSunrise = SUNRISE_PERSON_KEYS.has(queryPersonKey)
  const caseIsSunrise = SUNRISE_PERSON_KEYS.has(caseKey)
  const caseIsLakewood = LAKEWOOD_PERSON_KEYS.has(caseKey)
  if (queryIsSunrise && caseIsLakewood) return true
  if (!queryIsSunrise && caseIsSunrise) return true
  return false
}

// ── Component ────────────────────────────────────────────────────────────────

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

type Props = {
  auth: LoginResponse
  onLogout: () => void
}

export default function RagReportPage({ auth, onLogout }: Props) {
  const [results, setResults] = useState<QueryResult[]>([])
  const [running, setRunning] = useState(false)
  const [hasRun, setHasRun] = useState(false)

  const runReport = useCallback(async () => {
    setRunning(true)
    setHasRun(true)
    const outcomes: QueryResult[] = []

    for (const query of ARC_QUERIES) {
      try {
        // 1. Search for the person by name
        const searchResp = await api.search(auth.accessToken, query.searchName)
        const docs = searchResp.results

        if (docs.length === 0) {
          outcomes.push({
            query,
            anchorDoc: null,
            similarCases: [],
            passed: false,
            tenantViolation: false,
            error: `No documents found for "${query.searchName}". Upload corpus first.`,
          })
          continue
        }

        // 2. Pick the most recent / first document as the query anchor
        const anchor = docs[0]

        // 3. Fetch review detail to get similarCases
        let similarCases: SimilarCase[] = []
        let error: string | null = null
        try {
          const review = await api.getReview(auth.accessToken, anchor.intakeId)
          similarCases = review.similarCases ?? []
        } catch (err) {
          error = userMessage(err, 'Unable to fetch review details.')
        }

        // 4. Evaluate pass/fail
        const top5 = similarCases.slice(0, 5)
        let passed: boolean
        let tenantViolation = false

        if (query.tenantIsolationCheck) {
          // For isolation checks: PASS if no cross-tenant results in the top-5
          tenantViolation = top5.some((c) => isCrossTenant(query.personKey, c.applicantName))
          passed = !tenantViolation
        } else {
          // For identity checks: PASS if at least one expected person key is in top-5
          const topNames = top5.map((c) => extractPersonKeyFromName(c.applicantName))
          passed = query.expectedPersonKeys.some((pk) => topNames.includes(pk))
          // Also check for tenant isolation violation opportunistically
          tenantViolation = top5.some((c) => isCrossTenant(query.personKey, c.applicantName))
        }

        outcomes.push({ query, anchorDoc: anchor, similarCases: top5, passed, tenantViolation, error })
      } catch (err) {
        outcomes.push({
          query,
          anchorDoc: null,
          similarCases: [],
          passed: false,
          tenantViolation: false,
          error: userMessage(err, 'An unexpected error occurred while running this query.'),
        })
      }
    }

    setResults(outcomes)
    setRunning(false)
  }, [auth.accessToken])

  // Auto-run on mount
  useEffect(() => {
    runReport()
  }, [runReport])

  const passCount = results.filter((r) => r.passed).length
  const failCount = results.filter((r) => !r.passed).length

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-slate-50">
      {/* Header */}
      <header className="shrink-0 border-b border-slate-200 bg-white">
        <div className="flex h-14 items-center justify-between px-4 sm:px-6">
          <div className="flex items-center gap-3">
            <p className="text-sm font-semibold text-slate-900">Northwoods</p>
          </div>
          <div className="flex items-center gap-4">
            <span className="hidden text-xs text-slate-500 sm:block">{auth.tenantId}</span>
            <button
              type="button"
              onClick={onLogout}
              className={`text-xs text-slate-600 underline underline-offset-2 hover:text-slate-900 ${FOCUS_RING}`}
            >
              Sign out
            </button>
          </div>
        </div>
        {/* Reviewer page nav */}
        <nav className="border-t border-slate-100 px-4 sm:px-6" aria-label="Reviewer navigation">
          <div className="flex gap-1">
            <a
              href="#"
              className={`border-b-2 border-transparent px-3 py-2 text-xs font-medium text-slate-500 hover:text-slate-700 ${FOCUS_RING}`}
            >
              Queue
            </a>
            <a
              href="#submissions"
              className={`border-b-2 border-transparent px-3 py-2 text-xs font-medium text-slate-500 hover:text-slate-700 ${FOCUS_RING}`}
            >
              All Documents
            </a>
            <span
              className="border-b-2 border-sky-700 px-3 py-2 text-xs font-semibold text-sky-900"
              aria-current="page"
            >
              RAG Report
            </span>
          </div>
        </nav>
      </header>

      {/* Content */}
      <main className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-4xl px-4 py-8 sm:px-6">
          <div className="mb-6">
            <h1 className="text-lg font-semibold text-slate-900">RAG Pipeline Report</h1>
            <p className="mt-1 text-sm text-slate-600">
              Compares expected similar-case relationships from the narrative corpus against live retrieval results.
            </p>
          </div>

          {/* Summary bar */}
          {hasRun && !running && (
            <div className="mb-6 flex items-center gap-4">
              <div className="flex items-center gap-2">
                <span className="inline-flex items-center rounded border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs font-medium text-emerald-700">
                  {passCount} passed
                </span>
                {failCount > 0 && (
                  <span className="inline-flex items-center rounded border border-rose-200 bg-rose-50 px-2.5 py-1 text-xs font-medium text-rose-700">
                    {failCount} failed
                  </span>
                )}
              </div>
              <button
                type="button"
                onClick={runReport}
                disabled={running}
                className={`inline-flex items-center rounded border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-60 ${FOCUS_RING}`}
              >
                Re-run
              </button>
            </div>
          )}

          {/* Loading state */}
          {running && (
            <div className="rounded border border-slate-200 bg-white p-8 text-center">
              <p className="text-sm text-slate-500" role="status" aria-live="polite">
                Running arc queries against live system{'\u2026'}
              </p>
              <p className="mt-1 text-xs text-slate-400">
                Searching for corpus documents and fetching similar cases.
              </p>
            </div>
          )}

          {/* Results */}
          {!running && hasRun && (
            <div className="space-y-4">
              {results.map((r, i) => (
                <ArcResultCard key={i} result={r} />
              ))}
            </div>
          )}
        </div>
      </main>
    </div>
  )
}

// ── Result card ──────────────────────────────────────────────────────────────

function ArcResultCard({ result }: { result: QueryResult }) {
  const { query, anchorDoc, similarCases, passed, tenantViolation, error } = result

  return (
    <div
      className={`rounded border bg-white ${
        tenantViolation
          ? 'border-rose-300'
          : passed
            ? 'border-emerald-200'
            : 'border-amber-200'
      }`}
    >
      {/* Card header */}
      <div className="flex items-start justify-between border-b border-slate-100 px-4 py-3">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <h2 className="text-sm font-semibold text-slate-900">{query.label}</h2>
            <StatusBadge passed={passed} tenantViolation={tenantViolation} />
          </div>
          <p className="mt-0.5 text-xs text-slate-500">
            Expected: {query.expectation}
          </p>
        </div>
      </div>

      {/* Error state */}
      {error && (
        <div className="border-b border-slate-100 bg-amber-50 px-4 py-3">
          <p className="text-xs text-amber-800">{error}</p>
          {!anchorDoc && (
            <p className="mt-1 text-xs text-amber-600">
              Upload the corpus via{' '}
              <a href="#dev" className="font-medium underline underline-offset-2">
                Dev page
              </a>{' '}
              to populate test documents.
            </p>
          )}
        </div>
      )}

      {/* Tenant violation flag */}
      {tenantViolation && (
        <div className="border-b border-rose-200 bg-rose-50 px-4 py-3">
          <p className="text-xs font-semibold text-rose-800">
            Tenant isolation violation detected
          </p>
          <p className="mt-0.5 text-xs text-rose-700">
            Cross-tenant documents appeared in similar case results. This must be investigated.
          </p>
        </div>
      )}

      {/* Anchor document */}
      {anchorDoc && (
        <div className="border-b border-slate-100 px-4 py-2.5">
          <p className="text-xs font-medium text-slate-500">Query document</p>
          <p className="mt-0.5 text-sm text-slate-800">
            {anchorDoc.applicantName}{' '}
            <span className="text-xs text-slate-400">
              ({anchorDoc.templateId} &middot; {anchorDoc.intakeId.slice(0, 8)})
            </span>
          </p>
        </div>
      )}

      {/* Similar cases table */}
      {anchorDoc && similarCases.length > 0 && (
        <div className="px-4 py-3">
          <p className="mb-2 text-xs font-medium text-slate-500">
            Top {similarCases.length} similar cases
          </p>
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="border-b border-slate-100 text-slate-500">
                <th className="pb-1.5 pr-3 font-medium">#</th>
                <th className="pb-1.5 pr-3 font-medium">Applicant</th>
                <th className="pb-1.5 pr-3 font-medium">Template</th>
                <th className="pb-1.5 pr-3 font-medium">Score</th>
                <th className="pb-1.5 font-medium">Match</th>
              </tr>
            </thead>
            <tbody>
              {similarCases.map((sc, idx) => {
                const personKey = extractPersonKeyFromName(sc.applicantName)
                const isExpected = personKey != null && query.expectedPersonKeys.includes(personKey)
                const isCrossTenantMatch = isCrossTenant(query.personKey, sc.applicantName)
                return (
                  <tr key={sc.reviewId ?? idx} className="border-b border-slate-50 last:border-0">
                    <td className="py-1.5 pr-3 tabular-nums text-slate-400">{idx + 1}</td>
                    <td className="py-1.5 pr-3 text-slate-800">{sc.applicantName}</td>
                    <td className="py-1.5 pr-3 text-slate-600">{sc.templateId}</td>
                    <td className="py-1.5 pr-3 tabular-nums text-slate-700">
                      {(sc.matchScore * 100).toFixed(0)}%
                    </td>
                    <td className="py-1.5">
                      {isCrossTenantMatch ? (
                        <span className="inline-flex items-center rounded border border-rose-200 bg-rose-50 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-rose-700">
                          Tenant violation
                        </span>
                      ) : isExpected ? (
                        <span className="inline-flex items-center rounded border border-emerald-200 bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-emerald-700">
                          Expected
                        </span>
                      ) : (
                        <span className="text-slate-400">&mdash;</span>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* No similar cases */}
      {anchorDoc && similarCases.length === 0 && !error && (
        <div className="px-4 py-3">
          <p className="text-xs text-slate-500">No similar cases returned.</p>
          {query.tenantIsolationCheck && (
            <p className="mt-0.5 text-xs text-emerald-600">
              This is expected for tenant isolation checks.
            </p>
          )}
        </div>
      )}
    </div>
  )
}

// ── Status badge ─────────────────────────────────────────────────────────────

function StatusBadge({ passed, tenantViolation }: { passed: boolean; tenantViolation: boolean }) {
  if (tenantViolation) {
    return (
      <span
        role="status"
        className="inline-flex items-center rounded border border-rose-300 bg-rose-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-rose-800"
      >
        Tenant violation
      </span>
    )
  }
  if (passed) {
    return (
      <span
        role="status"
        className="inline-flex items-center rounded border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-emerald-700"
      >
        Pass
      </span>
    )
  }
  return (
    <span
      role="status"
      className="inline-flex items-center rounded border border-amber-200 bg-amber-50 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-700"
    >
      Fail
    </span>
  )
}
