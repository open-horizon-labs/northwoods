import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { api, userMessage } from '../api'
import { CaseTimeline } from '../components/CaseTimeline'
import { ReviewDetail } from '../components/ReviewDetail'
import { btnSecondary, FOCUS_RING, inputInteractive } from '../components/reviewStyles'
import type {
  CaseAggregateResponse,
  ConfidenceField,
  DocumentListItem,
  FinalizeReviewRequest,
  LoginResponse,
  ReviewDetailResponse,
  ReviewQueueItem,
  SearchResultItem,
} from '../types'
import { statusBadge } from '../types'

export type SidebarMode = 'queue' | 'documents' | 'search'

type Props = {
  auth: LoginResponse
  onLogout: () => void
  initialDocumentId?: string
  initialMode?: SidebarMode
}

export default function ReviewerDashboard({ auth, onLogout, initialDocumentId, initialMode }: Props) {
  const [queue, setQueue] = useState<ReviewQueueItem[]>([])
  const [queueSearch, setQueueSearch] = useState('')
  const [queueBusy, setQueueBusy] = useState(false)
  const [queueError, setQueueError] = useState<string | null>(null)
  const [selectedReviewId, setSelectedReviewId] = useState<string | null>(initialDocumentId ?? null)
  const [selectedApplicantName, setSelectedApplicantName] = useState<string | undefined>(undefined)

  const [reviewDetail, setReviewDetail] = useState<ReviewDetailResponse | null>(null)
  const [editableFields, setEditableFields] = useState<ConfidenceField[]>([])
  const [reviewerNote, setReviewerNote] = useState('')
  const [reviewLoading, setReviewLoading] = useState(false)
  const [reviewLoadError, setReviewLoadError] = useState<string | null>(null)
  const [finalizeBusy, setFinalizeBusy] = useState(false)
  const [finalizeError, setFinalizeError] = useState<string | null>(null)
  const [finalizeSuccess, setFinalizeSuccess] = useState(false)
  const [retryBusy, setRetryBusy] = useState(false)
  const [retryError, setRetryError] = useState<string | null>(null)

  // Sidebar mode: queue vs documents vs search
  const [sidebarMode, setSidebarMode] = useState<SidebarMode>(initialMode ?? 'queue')

  // Documents list state (loaded lazily when the user first switches to documents mode)
  const [documents, setDocuments] = useState<DocumentListItem[]>([])
  const [docsBusy, setDocsBusy] = useState(false)
  const [docsError, setDocsError] = useState<string | null>(null)
  const [docsLoaded, setDocsLoaded] = useState(false)
  const [docsFilter, setDocsFilter] = useState('')

  // Global search state
  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<SearchResultItem[]>([])
  const [searchBusy, setSearchBusy] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [searchPerformed, setSearchPerformed] = useState(false)

  // Case aggregate state
  const [caseView, setCaseView] = useState<CaseAggregateResponse | null>(null)
  const [caseBusy, setCaseBusy] = useState(false)
  const [caseError, setCaseError] = useState<string | null>(null)

  const detailPaneRef = useRef<HTMLDivElement>(null)
  // Ref to the global search input -- used for "/" keyboard shortcut focus
  const searchInputRef = useRef<HTMLInputElement>(null)

  const filteredQueue = useMemo(() => {
    const q = queueSearch.trim().toLowerCase()
    if (!q) return queue
    return queue.filter((item) =>
      `${item.applicantName} ${item.templateId} ${item.reviewId}`.toLowerCase().includes(q),
    )
  }, [queue, queueSearch])

  const filteredDocuments = useMemo(() => {
    const q = docsFilter.trim().toLowerCase()
    if (!q) return documents
    return documents.filter((d) =>
      `${d.applicantName} ${d.templateId} ${d.status}`.toLowerCase().includes(q),
    )
  }, [documents, docsFilter])

  const refreshQueue = useCallback(async () => {
    setQueueBusy(true)
    setQueueError(null)
    try {
      const items = await api.getReviewQueue(auth.accessToken)
      setQueue(items)
      // Auto-select first item if current selection was removed, but preserve deep-linked docs
      setSelectedReviewId((current) => {
        // Don't auto-select a queue item when starting in documents mode
        if (!current) return (initialDocumentId ?? (initialMode === 'documents' ? null : (items[0]?.reviewId ?? null)))
        if (items.some((i) => i.reviewId === current)) return current
        return current === initialDocumentId ? current : (items[0]?.reviewId ?? null)
      })
    } catch (err) {
      setQueueError(userMessage(err, 'Unable to load review queue. Please try again.'))
      setQueue([])
    } finally {
      setQueueBusy(false)
    }
  }, [auth.accessToken, initialDocumentId])

  const loadDocuments = useCallback(async () => {
    setDocsBusy(true)
    setDocsError(null)
    try {
      const items = await api.getDocuments(auth.accessToken)
      setDocuments(items)
      setDocsLoaded(true)
    } catch (err) {
      setDocsError(userMessage(err, 'Unable to load documents. Please try again.'))
      setDocuments([])
    } finally {
      setDocsBusy(false)
    }
  }, [auth.accessToken])

  // request seq prevents stale async responses from a previous review overwriting a newer one
  const reviewReqSeq = useRef(0)
  const loadReview = useCallback(
    async (reviewId: string) => {
      const seq = ++reviewReqSeq.current
      setReviewLoading(true)
      setReviewLoadError(null)
      setFinalizeSuccess(false)
      setFinalizeError(null)
      try {
        const detail = await api.getReview(auth.accessToken, reviewId)
        if (seq !== reviewReqSeq.current) return
        setReviewDetail(detail)
        setEditableFields(
          detail.fields
            .filter((f) => !f.isDiscovered)
            .map((f): ConfidenceField => ({
              fieldKey: f.fieldKey,
              value: f.value,
              confidence: f.confidence,
              requiresReview: f.requiresReview,
            }))
            .sort((a, b) => a.confidence - b.confidence),
        )
        setReviewerNote('')
      } catch (err) {
        if (seq !== reviewReqSeq.current) return
        setReviewDetail(null)
        setEditableFields([])
        setReviewLoadError(userMessage(err, 'Unable to load review details. Please try again.'))
      } finally {
        if (seq === reviewReqSeq.current) setReviewLoading(false)
      }
    },
    [auth.accessToken],
  )

  // Load queue on mount
  useEffect(() => {
    void refreshQueue()
  }, [refreshQueue])

  // Load documents lazily when the user first switches to documents mode
  useEffect(() => {
    if (sidebarMode === 'documents' && !docsLoaded && !docsBusy) {
      void loadDocuments()
    }
  }, [sidebarMode, docsLoaded, docsBusy, loadDocuments])

  // Auto-refresh queue and documents every 12 s while the page is visible.
  // Polling stops when every document is in a terminal state (nothing left to
  // transition) so we don't hammer the API when there's nothing to update.
  // If the currently-open review's status changed server-side, reload it too.
  const selectedReviewIdRef = useRef(selectedReviewId)
  const reviewDetailRef = useRef(reviewDetail)
  const docsLoadedRef = useRef(docsLoaded)

  // Keep refs current without adding them to any effect's dependency array
  useEffect(() => { selectedReviewIdRef.current = selectedReviewId }, [selectedReviewId])
  useEffect(() => { reviewDetailRef.current = reviewDetail }, [reviewDetail])
  useEffect(() => { docsLoadedRef.current = docsLoaded }, [docsLoaded])

  useEffect(() => {
    const POLL_INTERVAL_MS = 12_000

    const isNonTerminal = (status: string | number): boolean => {
      const s = typeof status === 'number' ? status : status.toLowerCase().replace(/_/g, '')
      return s === 0 || s === 1 || s === 'uploaded' || s === 'extracting'
    }

    const tick = async () => {
      if (document.visibilityState !== 'visible') return

      // Silent queue refresh -- refreshQueue guards its own busy flag so the
      // "Refreshing..." label won't flash when we already have data in the list.
      const freshQueueResult = await api.getReviewQueue(auth.accessToken).catch(() => null)
      if (!freshQueueResult) return
      const freshQueue = freshQueueResult
      setQueue(freshQueue)

      // Refresh documents list if it has been loaded at least once
      let freshDocs: DocumentListItem[] = []
      if (docsLoadedRef.current) {
        try {
          freshDocs = await api.getDocuments(auth.accessToken)
          setDocuments(freshDocs)
        } catch {
          // best-effort; stale list is acceptable
        }
      }

      // Check if all known documents are terminal -- if the queue is empty and
      // every doc has a terminal status, skip further polls for this interval.
      if (docsLoadedRef.current && freshQueue.length === 0 && freshDocs.length > 0) {
        const allTerminal = freshDocs.every((d) => !isNonTerminal(d.status))
        if (allTerminal) return
      }

      // If the currently-open review is in the queue and its status changed,
      // silently reload the detail so the reviewer sees updated fields.
      const openId = selectedReviewIdRef.current
      const openDetail = reviewDetailRef.current
      if (openId && openDetail) {
        const inQueue = freshQueue.some((q) => q.reviewId === openId)
        // The queue item doesn't carry status directly, but if the review was
        // previously non-terminal, re-fetch to pick up any status change.
        const wasNonTerminal = isNonTerminal(openDetail.status)
        if (wasNonTerminal || inQueue) {
          try {
            const fresh = await api.getReview(auth.accessToken, openId)
            if (fresh.status !== openDetail.status) {
              setReviewDetail(fresh)
            }
          } catch {
            // best-effort
          }
        }
      }
    }

    const id = window.setInterval(() => { void tick() }, POLL_INTERVAL_MS)

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        void tick()
      }
    }
    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      window.clearInterval(id)
      document.removeEventListener('visibilitychange', handleVisibilityChange)
    }
  }, [auth.accessToken])

  // Load review when selection changes
  useEffect(() => {
    if (!selectedReviewId) {
      setReviewDetail(null)
      setEditableFields([])
      return
    }
    void loadReview(selectedReviewId)
  }, [selectedReviewId, loadReview])

  // Keep selectedApplicantName in sync with selectedReviewId.
  // Queue then documents list are authoritative; click-time state is a fallback
  // for docs not yet in either list (e.g. opened via search before documents
  // list loads). When selectedReviewId clears, reset the name.
  useEffect(() => {
    if (!selectedReviewId) {
      setSelectedApplicantName(undefined)
      return
    }
    const fromQueue = queue.find((i) => i.reviewId === selectedReviewId)?.applicantName
    if (fromQueue) { setSelectedApplicantName(fromQueue); return }
    const fromDocs = documents.find((d) => d.documentId === selectedReviewId)?.applicantName
    if (fromDocs) setSelectedApplicantName(fromDocs)
    // If not in queue or documents (opened via search before documents load),
    // the name was already set in the click handler -- don't override it.
  }, [selectedReviewId, queue, documents])

  // Scroll detail pane to top when review changes
  useEffect(() => {
    const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    detailPaneRef.current?.scrollTo({ top: 0, behavior: prefersReduced ? 'instant' : 'smooth' })
  }, [selectedReviewId])

  // Keyboard shortcut: "/" focuses the global search input (Gmail-style).
  // Does nothing when focus is already inside an input/textarea/select.
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key !== '/') return
      const tag = (e.target as HTMLElement).tagName.toLowerCase()
      if (tag === 'input' || tag === 'textarea' || tag === 'select') return
      if ((e.target as HTMLElement).isContentEditable) return
      e.preventDefault()
      setSidebarMode('search')
      // Defer focus until after the sidebar re-renders with the search tab active
      requestAnimationFrame(() => searchInputRef.current?.focus())
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [])

  const handleFieldChange = (index: number, value: string) => {
    setEditableFields((prev) =>
      prev.map((f, i) =>
        i === index ? { ...f, value, requiresReview: false } : f,
      ),
    )
  }

  const handleFinalize = async () => {
    if (!selectedReviewId || !reviewDetail) return
    setFinalizeBusy(true)
    setFinalizeError(null)
    setFinalizeSuccess(false)

    const payload: FinalizeReviewRequest = {
      fields: editableFields,
      reviewerNote,
    }

    try {
      await api.finalizeReview(auth.accessToken, selectedReviewId, payload)
      setFinalizeSuccess(true)
      await refreshQueue()
      // Reload review to show updated status
      await loadReview(selectedReviewId)
    } catch (err) {
      setFinalizeError(userMessage(err, 'Unable to finalize review. Please try again.'))
    } finally {
      setFinalizeBusy(false)
    }
  }

  const handleRetry = async () => {
    if (!selectedReviewId) return
    setRetryBusy(true)
    setRetryError(null)
    try {
      await api.retryIntake(auth.accessToken, selectedReviewId)
      await refreshQueue()
      await loadReview(selectedReviewId)
    } catch (err) {
      setRetryError(userMessage(err, 'Unable to retry intake. Please try again.'))
    } finally {
      setRetryBusy(false)
    }
  }

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault()
    const q = searchQuery.trim()
    if (!q) return

    // UUID direct-nav: if the query looks like a full UUID or an 8-char hex prefix,
    // navigate straight to that document without hitting the search API.
    const fullUuidRe = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
    const prefixRe = /^[0-9a-f]{8}$/i
    if (fullUuidRe.test(q)) {
      closeCaseView()
      setSelectedReviewId(q.toLowerCase())
      setSelectedApplicantName(undefined)
      return
    }
    if (prefixRe.test(q)) {
      // Try to find an exact prefix match in the already-loaded documents list first
      const match = documents.find((d) => d.documentId.startsWith(q.toLowerCase()))
      if (match) {
        closeCaseView()
        setSelectedReviewId(match.documentId)
        setSelectedApplicantName(match.applicantName)
        return
      }
      // Also check the queue
      const queueMatch = queue.find((i) => i.reviewId.startsWith(q.toLowerCase()))
      if (queueMatch) {
        closeCaseView()
        setSelectedReviewId(queueMatch.reviewId)
        setSelectedApplicantName(queueMatch.applicantName)
        return
      }
    }

    setSearchBusy(true)
    setSearchError(null)
    setSearchPerformed(true)
    try {
      const response = await api.search(auth.accessToken, q)
      setSearchResults(response.results)
    } catch (err) {
      setSearchError(userMessage(err, 'Search failed. Please try again.'))
      setSearchResults([])
    } finally {
      setSearchBusy(false)
    }
  }

  const openCaseView = async (personKey: string) => {
    setCaseBusy(true)
    setCaseError(null)
    // Switch detail pane to case timeline
    setSelectedReviewId(null)
    setReviewDetail(null)
    try {
      const response = await api.getCaseAggregate(auth.accessToken, personKey)
      setCaseView(response)
    } catch (err) {
      setCaseError(userMessage(err, 'Unable to load case details. Please try again.'))
      setCaseView(null)
    } finally {
      setCaseBusy(false)
    }
  }

  const closeCaseView = () => {
    setCaseView(null)
    setCaseError(null)
  }

  const uncertainCount = editableFields.filter((f) => f.requiresReview).length

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-slate-50">
      <a
        href="#queue"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:bg-slate-900 focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-white"
      >
        Skip to review queue
      </a>

      {/* Nav */}
      <header className="shrink-0 border-b border-slate-200 bg-white">
        <div className="flex h-10 items-center justify-between px-4 sm:px-6">
          <div className="flex items-center gap-3">
            <p className="text-sm font-semibold text-slate-900">Northwoods</p>
            {queue.length > 0 ? (
              <span className="rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                {queue.length} pending
              </span>
            ) : null}
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
      </header>

      {/* Two-pane layout */}
      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar pane */}
        <aside
          id="queue"
          className="flex w-72 shrink-0 flex-col overflow-hidden border-r border-slate-200 bg-white xl:w-80"
          aria-label="Document sidebar"
        >
          {/* Tab bar */}
          <div className="shrink-0 border-b border-slate-200">
            <nav className="flex" aria-label="Sidebar sections">
              <button
                type="button"
                onClick={() => setSidebarMode('queue')}
                className={`flex-1 px-3 py-2.5 text-xs font-semibold transition ${
                  sidebarMode === 'queue'
                    ? 'border-b-2 border-sky-700 text-sky-900'
                    : 'text-slate-500 hover:text-slate-700'
                } ${FOCUS_RING}`}
                aria-current={sidebarMode === 'queue' ? 'page' : undefined}
              >
                Queue{queue.length > 0 ? ` (${queue.length})` : ''}
              </button>
              <button
                type="button"
                onClick={() => setSidebarMode('documents')}
                className={`flex-1 px-3 py-2.5 text-xs font-semibold transition ${
                  sidebarMode === 'documents'
                    ? 'border-b-2 border-sky-700 text-sky-900'
                    : 'text-slate-500 hover:text-slate-700'
                } ${FOCUS_RING}`}
                aria-current={sidebarMode === 'documents' ? 'page' : undefined}
              >
                All Docs
              </button>
              <button
                type="button"
                onClick={() => setSidebarMode('search')}
                className={`flex-1 px-3 py-2.5 text-xs font-semibold transition ${
                  sidebarMode === 'search'
                    ? 'border-b-2 border-sky-700 text-sky-900'
                    : 'text-slate-500 hover:text-slate-700'
                } ${FOCUS_RING}`}
                aria-current={sidebarMode === 'search' ? 'page' : undefined}
              >
                Search
              </button>
            </nav>
          </div>

          {sidebarMode === 'queue' ? (
            <>
              {/* Queue controls */}
              <div className="shrink-0 border-b border-slate-200 p-4">
                <div className="flex items-center justify-between gap-2">
                  <h1 className="text-sm font-semibold text-slate-900">Review queue</h1>
                  <button
                    type="button"
                    onClick={() => void refreshQueue()}
                    disabled={queueBusy}
                    className={`${btnSecondary} px-2.5 py-1.5 text-xs`}
                    aria-label="Refresh queue"
                  >
                    {queueBusy ? 'Refreshing...' : 'Refresh'}
                  </button>
                </div>

                <label htmlFor="queue-search" className="mt-3 block">
                  <span className="sr-only">Filter queue</span>
                  <input
                    id="queue-search"
                    type="search"
                    value={queueSearch}
                    onChange={(e) => setQueueSearch(e.target.value)}
                    placeholder="Filter current queue..."
                    className={`${inputInteractive} text-xs`}
                  />
                </label>

                {filteredQueue.length > 0 ? (
                  <p className="mt-1.5 text-xs text-slate-500" role="status" aria-live="polite">
                    {filteredQueue.length} document{filteredQueue.length === 1 ? '' : 's'}
                  </p>
                ) : null}
              </div>

              {/* Queue list */}
              <div className="flex-1 overflow-y-auto">
                {queueError ? (
                  <p role="alert" className="px-4 py-3 text-xs text-rose-700">
                    {queueError}
                  </p>
                ) : queueBusy && queue.length === 0 ? (
                  <p className="px-4 py-3 text-xs text-slate-500" role="status">
                    Loading queue...
                  </p>
                ) : filteredQueue.length === 0 ? (
                  <div className="p-4 text-center">
                    <p className="text-xs text-slate-500">
                      {queueSearch ? 'No matches for your filter.' : 'No documents pending review.'}
                    </p>
                  </div>
                ) : (
                  <ul role="list" aria-live="polite" aria-busy={queueBusy} className="divide-y divide-slate-100">
                    {filteredQueue.map((item) => {
                      const isSelected = selectedReviewId === item.reviewId && !caseView
                      return (
                        <li key={item.reviewId}>
                          <button
                            type="button"
                            onClick={() => { closeCaseView(); setSelectedReviewId(item.reviewId); setSelectedApplicantName(item.applicantName) }}
                            aria-pressed={isSelected}
                            aria-current={isSelected ? 'true' : undefined}
                            aria-label={`Review ${item.applicantName}, ${item.uncertainFieldCount} uncertain field${item.uncertainFieldCount === 1 ? '' : 's'}`}
                            className={`w-full px-4 py-3 text-left transition ${
                              isSelected ? 'bg-sky-50' : 'hover:bg-slate-50'
                            } ${FOCUS_RING}`}
                          >
                            <div className="flex items-start justify-between gap-2">
                              <div className="min-w-0">
                                <p className={`truncate text-sm font-medium ${isSelected ? 'text-sky-900' : 'text-slate-900'}`}>
                                  {item.applicantName}
                                </p>
                                <p className="mt-0.5 truncate text-xs text-slate-500">
                                  {item.templateId}
                                </p>
                              </div>
                              {item.uncertainFieldCount > 0 ? (
                                <span className="shrink-0 rounded-full border border-amber-200 bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700">
                                  {item.uncertainFieldCount}
                                </span>
                              ) : null}
                            </div>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </div>
            </>
          ) : sidebarMode === 'documents' ? (
            <>
              {/* Documents controls */}
              <div className="shrink-0 border-b border-slate-200 p-4">
                <div className="flex items-center justify-between gap-2">
                  <h2 className="text-sm font-semibold text-slate-900">All Documents</h2>
                  <button
                    type="button"
                    onClick={() => void loadDocuments()}
                    disabled={docsBusy}
                    className={`${btnSecondary} px-2.5 py-1.5 text-xs`}
                    aria-label="Refresh documents"
                  >
                    {docsBusy ? 'Loading...' : 'Refresh'}
                  </button>
                </div>
                <label htmlFor="docs-filter" className="mt-3 block">
                  <span className="sr-only">Filter documents</span>
                  <input
                    id="docs-filter"
                    type="search"
                    value={docsFilter}
                    onChange={(e) => setDocsFilter(e.target.value)}
                    placeholder="Filter by name, template, or status..."
                    className={`${inputInteractive} text-xs`}
                  />
                </label>
                {filteredDocuments.length > 0 ? (
                  <p className="mt-1.5 text-xs text-slate-500" role="status" aria-live="polite">
                    {filteredDocuments.length} document{filteredDocuments.length === 1 ? '' : 's'}
                    {docsFilter ? ' matching filter' : ''}
                  </p>
                ) : null}
              </div>

              {/* Documents list */}
              <div className="flex-1 overflow-y-auto">
                {docsError ? (
                  <p role="alert" className="px-4 py-3 text-xs text-rose-700">
                    {docsError}
                  </p>
                ) : docsBusy && documents.length === 0 ? (
                  <p className="px-4 py-3 text-xs text-slate-500" role="status">
                    Loading documents...
                  </p>
                ) : filteredDocuments.length === 0 ? (
                  <div className="p-4 text-center">
                    <p className="text-xs text-slate-500">
                      {docsFilter ? 'No matches for your filter.' : 'No documents found.'}
                    </p>
                  </div>
                ) : (
                  <ul role="list" aria-live="polite" aria-busy={docsBusy} className="divide-y divide-slate-100">
                    {filteredDocuments.map((doc) => {
                      const badge = statusBadge(doc.status)
                      const isSelected = selectedReviewId === doc.documentId && !caseView
                      return (
                        <li key={doc.documentId}>
                          <button
                            type="button"
                            onClick={() => { closeCaseView(); setSelectedReviewId(doc.documentId) }}
                            aria-pressed={isSelected}
                            aria-current={isSelected ? 'true' : undefined}
                            aria-label={`Open document for ${doc.applicantName}`}
                            className={`w-full px-4 py-3 text-left transition ${
                              isSelected ? 'bg-sky-50' : 'hover:bg-slate-50'
                            } ${FOCUS_RING}`}
                          >
                            <div className="flex items-start justify-between gap-2">
                              <div className="min-w-0">
                                <p className={`truncate text-sm font-medium ${isSelected ? 'text-sky-900' : 'text-slate-900'}`}>
                                  {doc.applicantName}
                                </p>
                                <p className="mt-0.5 truncate text-xs text-slate-500">
                                  {doc.templateId}
                                </p>
                              </div>
                              <span className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${badge.badgeClass}`}>
                                {badge.label}
                              </span>
                            </div>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </div>
            </>
          ) : (
            <>
              {/* Search controls */}
              <div className="shrink-0 border-b border-slate-200 p-4">
                <form onSubmit={handleSearch} noValidate>
                  <label htmlFor="global-search" className="sr-only">Search intakes</label>
                  <div className="flex gap-2">
                    <input
                      ref={searchInputRef}
                      id="global-search"
                      type="search"
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      onKeyDown={(e) => { if (e.key === 'Escape') { e.currentTarget.blur() } }}
                      placeholder="Search by name, UUID, or field... (/)"
                      className={`${inputInteractive} flex-1 text-xs`}
                      required
                      aria-required="true"
                    />
                    <button
                      type="submit"
                      disabled={searchBusy || !searchQuery.trim()}
                      className={`${btnSecondary} shrink-0 px-3 py-1.5 text-xs`}
                    >
                      {searchBusy ? 'Searching...' : 'Search'}
                    </button>
                  </div>
                </form>
                {searchPerformed && !searchBusy && searchResults.length > 0 ? (
                  <p className="mt-1.5 text-xs text-slate-500" role="status" aria-live="polite">
                    {searchResults.length} result{searchResults.length === 1 ? '' : 's'}
                  </p>
                ) : null}
              </div>

              {/* Search results */}
              <div className="flex-1 overflow-y-auto">
                {searchError ? (
                  <p role="alert" className="px-4 py-3 text-xs text-rose-700">
                    {searchError}
                  </p>
                ) : searchBusy ? (
                  <p className="px-4 py-3 text-xs text-slate-500" role="status" aria-live="polite">
                    Searching...
                  </p>
                ) : searchResults.length > 0 ? (
                  <ul role="list" className="divide-y divide-slate-100">
                    {searchResults.map((item) => {
                      const badge = statusBadge(item.status)
                      return (
                        <li key={item.intakeId}>
                          <button
                            type="button"
                            onClick={() => { closeCaseView(); setSelectedReviewId(item.intakeId); setSelectedApplicantName(item.applicantName) }}
                            className={`w-full px-4 py-3 text-left transition hover:bg-slate-50 ${FOCUS_RING}`}
                            aria-label={`Open review for ${item.applicantName}`}
                          >
                            <div className="flex items-start justify-between gap-2">
                              <div className="min-w-0">
                                <p className="truncate text-sm font-medium text-slate-900">
                                  {item.applicantName}
                                </p>
                                <p className="mt-0.5 truncate text-xs text-slate-500">
                                  {item.templateId}
                                </p>
                              </div>
                              <span className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${badge.badgeClass}`}>
                                {badge.label}
                              </span>
                            </div>
                            {item.snippet ? (
                              <p className="mt-1 text-xs leading-relaxed text-slate-600 line-clamp-2">
                                {item.snippet.replace(/\*\*/g, '')}
                              </p>
                            ) : null}
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                ) : searchPerformed ? (
                  <div className="p-6 text-center" role="status">
                    <p className="text-sm font-medium text-slate-600">No results found</p>
                    <p className="mt-1 text-xs text-slate-500">Try different keywords or check the spelling.</p>
                  </div>
                ) : (
                  <div className="p-4 text-center">
                    <p className="text-xs text-slate-500">Search across all processed intakes within your tenant.</p>
                  </div>
                )}
              </div>
            </>
          )}
        </aside>

        {/* Detail pane */}
        <main
          ref={detailPaneRef}
          className="flex flex-1 flex-col overflow-y-auto"
          aria-label="Review detail"
        >
          {caseView ? (
            <CaseTimeline
              caseData={caseView}
              caseBusy={caseBusy}
              caseError={caseError}
              onClose={closeCaseView}
              onOpenReview={(intakeId) => { closeCaseView(); setSelectedReviewId(intakeId); setSelectedApplicantName(undefined) }}
            />
          ) : !selectedReviewId ? (
            <div className="flex flex-1 items-center justify-center p-8 text-center">
              <div>
                <p className="text-sm font-medium text-slate-700">Select a document to review</p>
                <p className="mt-1 text-xs text-slate-500">
                  Choose from the queue or search for an applicant.
                </p>
              </div>
            </div>
          ) : reviewLoading ? (
            <div className="flex flex-1 items-center justify-center p-8">
              <p className="text-sm text-slate-500" role="status" aria-live="polite">
                Loading review...
              </p>
            </div>
          ) : reviewLoadError ? (
            <div className="p-6">
              <p
                role="alert"
                aria-live="assertive"
                className="rounded border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
              >
                {reviewLoadError}
              </p>
            </div>
          ) : reviewDetail ? (
            <ReviewDetail
              accessToken={auth.accessToken}
              review={reviewDetail}
              applicantName={selectedApplicantName}
              editableFields={editableFields}
              reviewerNote={reviewerNote}
              finalizeBusy={finalizeBusy}
              finalizeError={finalizeError}
              finalizeSuccess={finalizeSuccess}
              uncertainCount={uncertainCount}
              retryBusy={retryBusy}
              retryError={retryError}
              onFieldChange={handleFieldChange}
              onNoteChange={setReviewerNote}
              onFinalize={handleFinalize}
              onRetry={handleRetry}
              onSelectReview={setSelectedReviewId}
              onOpenCaseView={openCaseView}
            />
          ) : null}
        </main>
      </div>
    </div>
  )
}
