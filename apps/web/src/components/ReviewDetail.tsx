import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { documentSourceUrl } from '../api'
import { PdfViewer } from './PdfViewer'
import { useResizablePanel } from './useResizablePanel'
import type {
  AuditEventItem,
  ConfidenceField,
  ReviewDetailResponse,
  ReviewField,
  SimilarCase,
} from '../types'
import { statusLabel } from '../types'
import {
  attemptConfidenceColor,
  btnSecondary,
  btnSuccess,
  confidencePercent,
  confidenceTone,
  consensusNoteColor,
  FOCUS_RING,
  formatAuditEventType,
  formatAuditTimestamp,
  inputInteractive,
} from './reviewStyles'

export type ReviewDetailProps = {
  accessToken: string
  review: ReviewDetailResponse
  applicantName: string | undefined
  editableFields: ConfidenceField[]
  reviewerNote: string
  finalizeBusy: boolean
  finalizeError: string | null
  finalizeSuccess: boolean
  uncertainCount: number
  retryBusy: boolean
  retryError: string | null
  onFieldChange: (index: number, value: string) => void
  onNoteChange: (note: string) => void
  onFinalize: () => void
  onRetry: () => void
  onSelectReview: (id: string) => void
  /** Called with the applicant's display name to open the case aggregate view.
   *  The /cases/{personKey} endpoint uses fuzzy name matching, so this IS an
   *  applicant name string -- there is no stable UUID-based case identifier. */
  onOpenCaseView: (applicantName: string) => void
}

export function ReviewDetail({
  accessToken,
  review,
  applicantName,
  editableFields,
  reviewerNote,
  finalizeBusy,
  finalizeError,
  finalizeSuccess,
  uncertainCount,
  retryBusy,
  retryError,
  onFieldChange,
  onNoteChange,
  onFinalize,
  onRetry,
  onSelectReview,
  onOpenCaseView,
}: ReviewDetailProps) {
  const isFinalized = statusLabel(review.status) === 'Finalized'
  const isAutoAccepted = review.status === 3 || review.status === 'Completed'
  const isFailed = statusLabel(review.status) === 'Failed'

  const reviewFieldMap = useMemo(() => {
    const map = new Map<string, ReviewField>()
    for (const f of review.fields) map.set(f.fieldKey, f)
    return map
  }, [review.fields])

  const discoveredFields = useMemo(
    () => review.fields.filter((f) => f.isDiscovered),
    [review.fields],
  )

  // --- Resizable panel split ---
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [isWide, setIsWide] = useState(false)

  useEffect(() => {
    const mq = window.matchMedia('(min-width: 1280px)')
    const handler = (e: MediaQueryListEvent | MediaQueryList) => setIsWide(e.matches)
    handler(mq)
    mq.addEventListener('change', handler as (e: MediaQueryListEvent) => void)
    return () => mq.removeEventListener('change', handler as (e: MediaQueryListEvent) => void)
  }, [])

  const { ratio, handleRef, isDragging } = useResizablePanel(containerRef, isWide)

  const [openReasons, setOpenReasons] = useState<Set<string>>(new Set())
  const [discoveredOpen, setDiscoveredOpen] = useState(false)
  const [sourceAvailable, setSourceAvailable] = useState<boolean | null>(null)
  const [uuidCopied, setUuidCopied] = useState(false)

  const handleCopyUuid = useCallback(() => {
    void navigator.clipboard.writeText(review.reviewId).then(() => {
      setUuidCopied(true)
      setTimeout(() => setUuidCopied(false), 1500)
    })
  }, [review.reviewId])

  const handleAvailabilityChange = useCallback((available: boolean) => {
    setSourceAvailable(available)
  }, [])

  const toggleReason = (fieldKey: string) =>
    setOpenReasons((prev) => {
      const next = new Set(prev)
      if (next.has(fieldKey)) next.delete(fieldKey)
      else next.add(fieldKey)
      return next
    })

  const leftPercent = isWide ? `${(ratio * 100).toFixed(2)}%` : undefined
  const rightPercent = isWide ? `${((1 - ratio) * 100).toFixed(2)}%` : undefined

  return (
    <div
      ref={containerRef}
      className={`flex h-full ${isWide ? 'flex-row' : 'flex-col'}`}
      style={isDragging ? { cursor: 'col-resize' } : undefined}
    >
      {/* Document viewer */}
      <section
        className={`flex flex-col border-r border-slate-200 ${isWide ? '' : 'max-h-[60vh]'}`}
        style={isWide ? { width: leftPercent, minWidth: 0 } : undefined}
        aria-label="Source document"
      >
        <div className="flex shrink-0 items-center justify-between border-b border-slate-200 bg-white px-3 py-1">
          <p className="truncate text-xs text-slate-500">
            {review.templateId} &middot; {statusLabel(review.status)}
          </p>
          {sourceAvailable ? (
            <a
              href={documentSourceUrl(accessToken, review.reviewId)}
              target="_blank"
              rel="noreferrer"
              className={`${btnSecondary} ml-2 shrink-0 px-2.5 py-1 text-xs`}
            >
              Open in new tab
            </a>
          ) : null}
        </div>
        <PdfViewer
          accessToken={accessToken}
          documentId={review.reviewId}
          onAvailabilityChange={handleAvailabilityChange}
        />
        {sourceAvailable === false ? (
          <div className="flex flex-1 items-center justify-center bg-slate-50 p-8">
            <div className="text-center">
              <p className="text-sm font-medium text-slate-700">Source document not available</p>
              <p className="mt-1 text-xs text-slate-500">
                The original document file is not stored for seed data. Review the extracted fields on the right.
              </p>
            </div>
          </div>
        ) : null}
      </section>

      {/* Drag handle -- only visible at xl+ */}
      {isWide ? (
        <div
          ref={handleRef}
          role="separator"
          aria-orientation="vertical"
          aria-label="Resize panels"
          aria-valuenow={Math.round(ratio * 100)}
          aria-valuemin={25}
          aria-valuemax={75}
          className={`group relative z-10 w-2 shrink-0 cursor-col-resize touch-none select-none ${isDragging ? 'bg-sky-100' : 'bg-slate-100 hover:bg-slate-200'} transition-colors`}
        >
          {/* Visual grip dots */}
          <div className="absolute inset-y-0 left-1/2 flex -translate-x-1/2 flex-col items-center justify-center gap-1">
            <span className={`block h-1 w-1 rounded-full ${isDragging ? 'bg-sky-500' : 'bg-slate-400 group-hover:bg-slate-500'}`} />
            <span className={`block h-1 w-1 rounded-full ${isDragging ? 'bg-sky-500' : 'bg-slate-400 group-hover:bg-slate-500'}`} />
            <span className={`block h-1 w-1 rounded-full ${isDragging ? 'bg-sky-500' : 'bg-slate-400 group-hover:bg-slate-500'}`} />
          </div>
        </div>
      ) : null}

      {/* Fields + actions */}
      <section
        className="flex flex-col overflow-y-auto bg-white"
        style={isWide ? { width: rightPercent, minWidth: 0 } : undefined}
        aria-label="Extracted fields"
      >
        <div className="shrink-0 border-b border-slate-200 px-4 py-3">
          <div className="flex items-center justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-sm font-semibold text-slate-900">{applicantName ?? 'Extracted fields'}</h2>
              {uncertainCount > 0 ? (
                <p className="text-xs text-amber-700">
                  {uncertainCount} field{uncertainCount === 1 ? '' : 's'} require attention
                </p>
              ) : isAutoAccepted ? (
                <p className="text-xs text-emerald-700">Auto-accepted -- all fields high confidence</p>
              ) : (
                <p className="text-xs text-slate-500">All fields reviewed</p>
              )}
              {/* Document UUID -- 8-char prefix shown, full UUID in tooltip; click to copy */}
              <button
                type="button"
                onClick={handleCopyUuid}
                title={`Document ID: ${review.reviewId}\nClick to copy`}
                aria-label={uuidCopied ? 'Copied to clipboard' : `Copy document ID ${review.reviewId}`}
                className={`mt-1 inline-flex items-center gap-1 rounded px-1.5 py-0.5 font-mono text-[10px] transition ${FOCUS_RING} ${uuidCopied ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500 hover:bg-slate-200 hover:text-slate-700'}`}
              >
                {uuidCopied ? 'Copied!' : review.reviewId.slice(0, 8)}
                {!uuidCopied ? (
                  <svg className="h-2.5 w-2.5" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true">
                    <path d="M4 2a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2H4zm0 1h6a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"/>
                    <path d="M10 1H3a1 1 0 0 0-1 1v1h1V2h7v1h1V2a1 1 0 0 0-1-1z"/>
                  </svg>
                ) : null}
              </button>
            </div>
            {applicantName && applicantName !== '(unknown)' ? (
              <button
                type="button"
                onClick={() => onOpenCaseView(applicantName)}
                className={`${btnSecondary} shrink-0 px-2.5 py-1 text-xs`}
                title="View all documents for this applicant"
              >
                All cases
              </button>
            ) : null}
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {/* Fields */}
          <div className="space-y-2 p-4">
            {editableFields.map((field, index) => {
              const tier = confidenceTone(field.confidence)
              const fieldId = `field-${index}`
              const hintId = `${fieldId}-hint`
              const rf = reviewFieldMap.get(field.fieldKey)
              const hasAttempts = rf && rf.attempts.length > 0
              const isOpen = openReasons.has(field.fieldKey)
              return (
                <div
                  key={field.fieldKey}
                  className={`rounded border p-3 ${field.requiresReview ? tier.className : isFinalized ? 'border-slate-200 bg-slate-50' : 'border-slate-200 bg-white'}`}
                >
                  <div className="flex items-center justify-between gap-2">
                    {isFinalized ? (
                      <span className="text-xs font-medium text-slate-900">{field.fieldKey}</span>
                    ) : (
                      <label htmlFor={fieldId} className="text-xs font-medium text-slate-900">
                        {field.fieldKey}
                      </label>
                    )}
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
                  {hasAttempts ? (
                    <div className="mt-1.5">
                      <button
                        type="button"
                        onClick={() => toggleReason(field.fieldKey)}
                        aria-expanded={isOpen}
                        className={`inline-flex items-center gap-1 text-xs text-slate-600 hover:text-slate-900 ${FOCUS_RING} rounded px-1 py-0.5 -ml-1`}
                      >
                        <svg
                          className={`h-3 w-3 transition-transform ${isOpen ? 'rotate-90' : ''}`}
                          fill="none"
                          viewBox="0 0 24 24"
                          stroke="currentColor"
                          strokeWidth={2}
                          aria-hidden="true"
                        >
                          <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                        </svg>
                        Why this confidence?
                      </button>
                      {isOpen ? (
                        <div className="mt-1.5 space-y-1 rounded border border-slate-200 bg-slate-50 p-2">
                          <p className={`text-xs font-medium ${consensusNoteColor(rf.consensusNote)}`}>
                            {rf.consensusNote}
                          </p>
                          {rf.attempts.map((attempt, ai) => (
                            <div
                              key={`${attempt.provider}-${ai}`}
                              className="flex items-baseline justify-between gap-2 text-xs"
                            >
                              <span className="font-medium text-slate-700">{attempt.provider}:</span>
                              <span className="flex-1 truncate text-slate-600" title={attempt.value}>
                                {attempt.value || '\u2014'}
                              </span>
                              <span className={`shrink-0 tabular-nums ${attemptConfidenceColor(attempt.confidence)}`}>
                                {confidencePercent(attempt.confidence)}
                              </span>
                            </div>
                          ))}
                        </div>
                      ) : null}
                    </div>
                  ) : null}
                  {isFinalized ? (
                    <p
                      id={fieldId}
                      aria-describedby={hintId}
                      className="mt-2 min-h-[2rem] whitespace-pre-wrap break-words text-sm text-slate-800"
                    >
                      {field.value || <span className="text-slate-400 italic">{'\u2014'}</span>}
                    </p>
                  ) : (
                    <textarea
                      id={fieldId}
                      value={field.value}
                      onChange={(e) => onFieldChange(index, e.target.value)}
                      rows={Math.max(1, Math.ceil((field.value.length + 1) / 35))}
                      aria-describedby={hintId}
                      className={`${inputInteractive} mt-2 resize-none`}
                    />
                  )}
                </div>
              )
            })}
          </div>

          {/* Discovered fields (collapsible) */}
          {discoveredFields.length > 0 ? (
            <div className="border-t border-slate-100 p-4">
              <button
                type="button"
                onClick={() => setDiscoveredOpen((o) => !o)}
                className={`flex w-full items-center justify-between gap-2 text-left ${FOCUS_RING} rounded px-1 py-0.5 -ml-1`}
                aria-expanded={discoveredOpen}
              >
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">
                  Additional fields ({discoveredFields.length})
                </p>
                <svg
                  className={`h-3.5 w-3.5 shrink-0 text-slate-400 transition-transform ${discoveredOpen ? 'rotate-90' : ''}`}
                  fill="none"
                  viewBox="0 0 24 24"
                  stroke="currentColor"
                  strokeWidth={2}
                  aria-hidden="true"
                >
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                </svg>
              </button>
              {discoveredOpen ? (
                <dl className="mt-2 space-y-1.5">
                  {discoveredFields.map((f) => {
                    const tier = confidenceTone(f.confidence)
                    return (
                      <div key={f.fieldKey} className="rounded border border-slate-200 bg-slate-50 px-3 py-2">
                        <div className="flex items-center justify-between gap-2">
                          <dt className="text-xs font-medium text-slate-600">{f.fieldKey}</dt>
                          <span
                            className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${tier.badgeClass}`}
                            aria-label={`${confidencePercent(f.confidence)} confidence`}
                          >
                            {confidencePercent(f.confidence)}
                          </span>
                        </div>
                        <dd className="mt-0.5 text-xs text-slate-800 break-words">{f.value || '\u2014'}</dd>
                      </div>
                    )
                  })}
                </dl>
              ) : null}
            </div>
          ) : null}

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
                      {sc.signals?.length > 0 ? (
                        <div className="mt-1.5 flex flex-wrap gap-1">
                          {sc.signals.map((sig) => (
                            <span key={sig} className="rounded-full border border-slate-200 bg-white px-2 py-0.5 text-xs text-slate-500">
                              {sig}
                            </span>
                          ))}
                        </div>
                      ) : null}
                      {sc.summary ? (
                        <p className="mt-1 text-xs text-slate-500 line-clamp-2">{sc.summary}</p>
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
                {review.auditEvents.map((ev: AuditEventItem, i) => (
                  <li
                    key={`${ev.eventType}-${i}`}
                    className="rounded border border-slate-100 bg-slate-50 px-3 py-2 text-xs"
                  >
                    <div className="flex items-start justify-between gap-2">
                      <span className="font-medium text-slate-700">{formatAuditEventType(ev.eventType)}</span>
                      <span className="shrink-0 tabular-nums text-slate-400">{formatAuditTimestamp(ev.createdAt)}</span>
                    </div>
                    <span className="text-slate-500">{ev.actorEmail ?? 'system'}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}

          {/* Failed state: show error and retry */}
          {isFailed ? (
            <div className="border-t border-slate-100 p-4">
              <div className="rounded border border-rose-200 bg-rose-50 px-3 py-2">
                <p className="text-xs font-medium text-rose-700">Extraction failed</p>
                {review.failureReason ? (
                  <p className="mt-1 text-xs text-rose-600">{review.failureReason}</p>
                ) : null}
              </div>
              {retryError ? (
                <p role="alert" aria-live="assertive" className="mt-2 text-xs text-rose-700">
                  {retryError}
                </p>
              ) : null}
              <button
                type="button"
                onClick={onRetry}
                disabled={retryBusy}
                className={`${btnSecondary} mt-3 w-full`}
              >
                {retryBusy ? 'Retrying...' : 'Retry extraction'}
              </button>
            </div>
          ) : !isFinalized ? (
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
                {finalizeBusy ? 'Finalizing...' : 'Finalize review'}
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
