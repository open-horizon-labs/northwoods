import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { api } from '../api'
import type {
  ConfidenceField,
  FinalizeReviewRequest,
  LoginResponse,
  ReviewDetailResponse,
  ReviewQueueItem,
  SimilarCase,
} from '../types'
import { statusLabel } from '../types'

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

const btn =
  'inline-flex min-h-11 items-center justify-center rounded border px-4 py-2.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-60'

const btnSuccess = `${btn} border-emerald-700 bg-emerald-700 text-white hover:bg-emerald-800 ${FOCUS_RING}`
const btnSecondary = `${btn} border-slate-300 bg-white text-slate-700 hover:bg-slate-50 ${FOCUS_RING}`

const inputStyle =
  'w-full rounded border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600'
const inputInteractive = `${inputStyle} ${FOCUS_RING}`

type ConfidenceTier = {
  label: string
  className: string
  badgeClass: string
}

const confidenceTone = (confidence: number): ConfidenceTier => {
  if (confidence >= 0.9)
    return {
      label: 'High',
      className: 'border-emerald-200 bg-emerald-50',
      badgeClass: 'border-emerald-200 bg-emerald-50 text-emerald-700',
    }
  if (confidence >= 0.8)
    return {
      label: 'Good',
      className: 'border-sky-200 bg-sky-50',
      badgeClass: 'border-sky-200 bg-sky-50 text-sky-700',
    }
  if (confidence >= 0.65)
    return {
      label: 'Needs review',
      className: 'border-amber-200 bg-amber-50',
      badgeClass: 'border-amber-200 bg-amber-50 text-amber-700',
    }
  return {
    label: 'Review required',
    className: 'border-rose-200 bg-rose-50',
    badgeClass: 'border-rose-200 bg-rose-50 text-rose-700',
  }
}

const confidencePercent = (c: number) => `${Math.round(c * 100)}%`

const formatAuditEvent = (name: string) =>
  name
    .split(/[_-]/)
    .filter(Boolean)
    .map((s) => {
      const lower = s.toLowerCase()
      return lower === 'id' ? 'ID' : lower.charAt(0).toUpperCase() + lower.slice(1)
    })
    .join(' ')

type Props = {
  auth: LoginResponse
  onLogout: () => void
}

export default function ReviewerDashboard({ auth, onLogout }: Props) {
  const [queue, setQueue] = useState<ReviewQueueItem[]>([])
  const [queueSearch, setQueueSearch] = useState('')
  const [queueBusy, setQueueBusy] = useState(false)
  const [queueError, setQueueError] = useState<string | null>(null)
  const [selectedReviewId, setSelectedReviewId] = useState<string | null>(null)

  const [reviewDetail, setReviewDetail] = useState<ReviewDetailResponse | null>(null)
  const [editableFields, setEditableFields] = useState<ConfidenceField[]>([])
  const [reviewerNote, setReviewerNote] = useState('')
  const [reviewLoading, setReviewLoading] = useState(false)
  const [reviewLoadError, setReviewLoadError] = useState<string | null>(null)
  const [finalizeBusy, setFinalizeBusy] = useState(false)
  const [finalizeError, setFinalizeError] = useState<string | null>(null)
  const [finalizeSuccess, setFinalizeSuccess] = useState(false)

  const detailPaneRef = useRef<HTMLDivElement>(null)

  const filteredQueue = useMemo(() => {
    const q = queueSearch.trim().toLowerCase()
    if (!q) return queue
    return queue.filter((item) =>
      `${item.applicantName} ${item.templateId} ${item.reviewId}`.toLowerCase().includes(q),
    )
  }, [queue, queueSearch])

  const refreshQueue = useCallback(async () => {
    setQueueBusy(true)
    setQueueError(null)
    try {
      const items = await api.getReviewQueue(auth.accessToken)
      setQueue(items)
      // Auto-select first item if current selection was removed
      setSelectedReviewId((current) => {
        if (!current) return items[0]?.reviewId ?? null
        return items.some((i) => i.reviewId === current) ? current : (items[0]?.reviewId ?? null)
      })
    } catch (err) {
      setQueueError(err instanceof Error ? err.message : 'Failed to load review queue.')
      setQueue([])
    } finally {
      setQueueBusy(false)
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
          detail.fields.map((f) => ({ ...f })).sort((a, b) => a.confidence - b.confidence),
        )
        setReviewerNote('')
      } catch (err) {
        if (seq !== reviewReqSeq.current) return
        setReviewDetail(null)
        setEditableFields([])
        setReviewLoadError(err instanceof Error ? err.message : 'Unable to load review details.')
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

  // Load review when selection changes
  useEffect(() => {
    if (!selectedReviewId) {
      setReviewDetail(null)
      setEditableFields([])
      return
    }
    void loadReview(selectedReviewId)
  }, [selectedReviewId, loadReview])

  // Scroll detail pane to top when review changes
  useEffect(() => {
    const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    detailPaneRef.current?.scrollTo({ top: 0, behavior: prefersReduced ? 'instant' : 'smooth' })
  }, [selectedReviewId])

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
      setFinalizeError(err instanceof Error ? err.message : 'Failed to finalize review.')
    } finally {
      setFinalizeBusy(false)
    }
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
        <div className="flex h-14 items-center justify-between px-4 sm:px-6">
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
        {/* Queue pane */}
        <aside
          id="queue"
          className="flex w-72 shrink-0 flex-col overflow-hidden border-r border-slate-200 bg-white xl:w-80"
          aria-label="Review queue"
        >
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
                {queueBusy ? 'Refreshing\u2026' : 'Refresh'}
              </button>
            </div>

            <label htmlFor="queue-search" className="mt-3 block">
              <span className="sr-only">Search queue</span>
              <input
                id="queue-search"
                type="search"
                value={queueSearch}
                onChange={(e) => setQueueSearch(e.target.value)}
                placeholder="Search applicant or template\u2026"
                className={`${inputInteractive} text-xs`}
              />
            </label>

            {filteredQueue.length > 0 ? (
              <p className="mt-1.5 text-xs text-slate-500" role="status" aria-live="polite">
                {filteredQueue.length} document{filteredQueue.length === 1 ? '' : 's'}
              </p>
            ) : null}
          </div>

          <div className="flex-1 overflow-y-auto">
            {queueError ? (
              <p role="alert" className="px-4 py-3 text-xs text-rose-700">
                {queueError}
              </p>
            ) : queueBusy && queue.length === 0 ? (
              <p className="px-4 py-3 text-xs text-slate-500" role="status">
                Loading queue\u2026
              </p>
            ) : filteredQueue.length === 0 ? (
              <div className="p-4 text-center">
                <p className="text-xs text-slate-500">
                  {queueSearch ? 'No matches for your search.' : 'No documents pending review.'}
                </p>
              </div>
            ) : (
              <ul role="list" aria-live="polite" aria-busy={queueBusy} className="divide-y divide-slate-100">
                {filteredQueue.map((item) => {
                  const isSelected = selectedReviewId === item.reviewId
                  return (
                    <li key={item.reviewId}>
                      <button
                        type="button"
                        onClick={() => setSelectedReviewId(item.reviewId)}
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
                              {item.uploadDate ? (
                                <>
                                  {' · '}
                                  {new Date(item.uploadDate).toLocaleDateString([], {
                                    month: 'short',
                                    day: 'numeric',
                                  })}
                                </>
                              ) : null}
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
        </aside>

        {/* Detail pane */}
        <main
          ref={detailPaneRef}
          className="flex flex-1 flex-col overflow-y-auto"
          aria-label="Review detail"
        >
          {!selectedReviewId ? (
            <div className="flex flex-1 items-center justify-center p-8 text-center">
              <div>
                <p className="text-sm font-medium text-slate-700">Select a document to review</p>
                <p className="mt-1 text-xs text-slate-500">
                  Choose from the queue on the left to open the review detail.
                </p>
              </div>
            </div>
          ) : reviewLoading ? (
            <div className="flex flex-1 items-center justify-center p-8">
              <p className="text-sm text-slate-500" role="status" aria-live="polite">
                Loading review\u2026
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
              review={reviewDetail}
              applicantName={queue.find((i) => i.reviewId === selectedReviewId)?.applicantName}
              editableFields={editableFields}
              reviewerNote={reviewerNote}
              finalizeBusy={finalizeBusy}
              finalizeError={finalizeError}
              finalizeSuccess={finalizeSuccess}
              uncertainCount={uncertainCount}
              onFieldChange={handleFieldChange}
              onNoteChange={setReviewerNote}
              onFinalize={handleFinalize}
              onSelectReview={setSelectedReviewId}
            />
          ) : null}
        </main>
      </div>
    </div>
  )
}

// --- Review Detail Sub-component ---

type ReviewDetailProps = {
  review: ReviewDetailResponse
  applicantName: string | undefined
  editableFields: ConfidenceField[]
  reviewerNote: string
  finalizeBusy: boolean
  finalizeError: string | null
  finalizeSuccess: boolean
  uncertainCount: number
  onFieldChange: (index: number, value: string) => void
  onNoteChange: (note: string) => void
  onFinalize: () => void
  onSelectReview: (id: string) => void
}

function ReviewDetail({
  review,
  applicantName,
  editableFields,
  reviewerNote,
  finalizeBusy,
  finalizeError,
  finalizeSuccess,
  uncertainCount,
  onFieldChange,
  onNoteChange,
  onFinalize,
  onSelectReview,
}: ReviewDetailProps) {
  const isFinalized = statusLabel(review.status) === 'Finalized'

  return (
    <div className="grid h-full grid-cols-1 xl:grid-cols-[1fr_420px]">
      {/* Document viewer */}
      <section className="flex flex-col border-r border-slate-200" aria-label="Source document">
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200 bg-white px-4 py-2">
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold text-slate-900">{review.intakeId}</p>
            <p className="text-xs text-slate-500">
              Template {review.templateId} &middot; {statusLabel(review.status)}
            </p>
          </div>
          <a
            href={review.sourceDocumentUrl}
            target="_blank"
            rel="noreferrer"
            className={`${btnSecondary} px-3 py-1.5 text-xs`}
          >
            Open in new tab
          </a>
        </div>
        <iframe
          src={review.sourceDocumentUrl}
          title="Source intake document"
          className="flex-1 border-0"
          sandbox="allow-same-origin"
        />
      </section>

      {/* Fields + actions */}
      <section className="flex flex-col overflow-y-auto bg-white" aria-label="Extracted fields">
        <div className="shrink-0 border-b border-slate-200 px-4 py-3">
          <div className="flex items-center justify-between gap-3">
            <div>
              <h2 className="text-sm font-semibold text-slate-900">{applicantName ?? 'Extracted fields'}</h2>
              {uncertainCount > 0 ? (
                <p className="text-xs text-amber-700">
                  {uncertainCount} field{uncertainCount === 1 ? '' : 's'} require attention
                </p>
              ) : (
                <p className="text-xs text-slate-500">All fields reviewed</p>
              )}
            </div>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {/* Fields */}
          <div className="space-y-2 p-4">
            {editableFields.map((field, index) => {
              const tier = confidenceTone(field.confidence)
              const fieldId = `field-${index}`
              const hintId = `${fieldId}-hint`
              return (
                <div
                  key={field.fieldKey}
                  className={`rounded border p-3 ${field.requiresReview ? tier.className : 'border-slate-200 bg-white'}`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <label htmlFor={fieldId} className="text-xs font-medium text-slate-900">
                      {field.fieldKey}
                    </label>
                    <span
                      className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${tier.badgeClass}`}
                      aria-live="polite"
                    >
                      {tier.label}
                    </span>
                  </div>
                  <p id={hintId} className="mt-0.5 text-xs text-slate-500">
                    {confidencePercent(field.confidence)} confidence
                  </p>
                  <textarea
                    id={fieldId}
                    value={field.value}
                    onChange={(e) => onFieldChange(index, e.target.value)}
                    rows={Math.max(1, Math.ceil((field.value.length + 1) / 35))}
                    aria-describedby={hintId}
                    disabled={isFinalized}
                    className={`${inputInteractive} mt-2 resize-none disabled:bg-slate-50 disabled:text-slate-500`}
                  />
                </div>
              )
            })}
          </div>

          {/* Similar cases */}
          {review.similarCases.length > 0 ? (
            <div className="border-t border-slate-100 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Similar cases</p>
              <ul className="mt-2 space-y-2" role="list">
                {review.similarCases.map((sc: SimilarCase) => (
                  <li key={sc.intakeId}>
                    <button
                      type="button"
                      onClick={() => onSelectReview(sc.reviewId)}
                      className={`w-full rounded border border-slate-200 bg-slate-50 px-3 py-2 text-left hover:border-slate-300 hover:bg-slate-100 ${FOCUS_RING}`}
                    >
                      <div className="flex items-center justify-between gap-2">
                        <p className="text-xs font-medium text-slate-900">{sc.applicantName}</p>
                        <span className="shrink-0 rounded-full border border-sky-200 bg-sky-50 px-2 py-0.5 text-xs font-medium text-sky-700">
                          {(sc.matchScore * 100).toFixed(0)}% match
                        </span>
                      </div>
                      {sc.summary ? (
                        <p className="mt-1 text-xs text-slate-600 line-clamp-2">{sc.summary}</p>
                      ) : null}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {/* Audit trail */}
          {review.auditEvents.length > 0 ? (
            <div className="border-t border-slate-100 p-4">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Audit trail</p>
              <ul className="mt-2 space-y-1" aria-live="polite">
                {review.auditEvents.map((ev, i) => (
                  <li
                    key={`${ev}-${i}`}
                    className="rounded border border-slate-100 bg-slate-50 px-3 py-1.5 text-xs text-slate-700"
                  >
                    {formatAuditEvent(ev)}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {/* Finalize */}
          {!isFinalized ? (
            <div className="border-t border-slate-100 p-4">
              <label htmlFor="reviewer-note" className="block text-xs font-medium text-slate-700">
                Reviewer note
              </label>
              <textarea
                id="reviewer-note"
                value={reviewerNote}
                onChange={(e) => onNoteChange(e.target.value)}
                rows={2}
                placeholder="Record the decision or correction rationale (optional)"
                className={`${inputInteractive} mt-1.5 resize-none text-xs`}
              />

              {finalizeError ? (
                <p role="alert" aria-live="assertive" className="mt-2 text-xs text-rose-700">
                  {finalizeError}
                </p>
              ) : null}

              {finalizeSuccess ? (
                <p role="status" className="mt-2 text-xs text-emerald-700">
                  Review finalized successfully.
                </p>
              ) : null}

              <button
                type="button"
                onClick={onFinalize}
                disabled={finalizeBusy}
                className={`${btnSuccess} mt-3 w-full`}
              >
                {finalizeBusy ? 'Finalizing\u2026' : 'Finalize review'}
              </button>
            </div>
          ) : (
            <div className="border-t border-slate-100 p-4">
              <p className="rounded border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs font-medium text-emerald-700">
                This review has been finalized.
              </p>
            </div>
          )}
        </div>
      </section>
    </div>
  )
}
