import type { CaseAggregateResponse, CaseDocumentItem } from '../types'
import { statusBadge } from '../types'
import { btnSecondary, FOCUS_RING } from './reviewStyles'

export type CaseTimelineProps = {
  caseData: CaseAggregateResponse
  caseBusy: boolean
  caseError: string | null
  onClose: () => void
  onOpenReview: (intakeId: string) => void
}

export function CaseTimeline({ caseData, caseBusy, caseError, onClose, onOpenReview }: CaseTimelineProps) {
  if (caseBusy) {
    return (
      <div className="flex flex-1 items-center justify-center p-8">
        <p className="text-sm text-slate-500" role="status" aria-live="polite">Loading case...</p>
      </div>
    )
  }

  if (caseError) {
    return (
      <div className="p-6">
        <p role="alert" className="rounded border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
          {caseError}
        </p>
      </div>
    )
  }

  return (
    <div className="flex flex-1 flex-col overflow-y-auto">
      {/* Header */}
      <div className="shrink-0 border-b border-slate-200 bg-white px-6 py-4">
        <div className="flex items-center justify-between gap-4">
          <div>
            <h2 className="text-base font-semibold text-slate-900">{caseData.personKey}</h2>
            <p className="mt-0.5 text-xs text-slate-500">
              {caseData.documents.length} document{caseData.documents.length === 1 ? '' : 's'}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className={`${btnSecondary} px-3 py-1.5 text-xs`}
          >
            Close
          </button>
        </div>
      </div>

      {/* Timeline */}
      <div className="flex-1 overflow-y-auto px-6 py-6">
        {caseData.documents.length === 0 ? (
          <p className="text-sm text-slate-500">No documents found for this person.</p>
        ) : (
          <ol className="relative border-l-2 border-slate-200 ml-2" aria-label="Document timeline">
            {caseData.documents.map((doc: CaseDocumentItem) => {
              const badge = statusBadge(doc.status)
              // Prefer interviewDate extracted field over uploadedAt; fall back to createdAt
              const interviewDateValue = doc.fields.find(
                (f) => f.fieldKey.toLowerCase() === 'interviewdate',
              )?.value
              const parsedInterviewDate = interviewDateValue ? new Date(interviewDateValue) : null
              const usingInterviewDate = parsedInterviewDate !== null && !isNaN(parsedInterviewDate.getTime())
              const displayDate = usingInterviewDate ? parsedInterviewDate! : new Date(doc.createdAt)
              const dateLabel = displayDate.toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' })
              // Only show time when falling back to uploadedAt (interviewDate has no meaningful time)
              const timeLabel = usingInterviewDate
                ? null
                : displayDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
              const keyFields = doc.fields.slice(0, 3)
              return (
                <li key={doc.intakeId} className="relative mb-6 last:mb-0 pl-8">
                  {/* Timeline dot */}
                  <span
                    className={`absolute -left-[7px] top-1.5 h-3 w-3 rounded-full border-2 border-white ${
                      badge.badgeClass.includes('emerald') ? 'bg-emerald-500'
                        : badge.badgeClass.includes('amber') ? 'bg-amber-500'
                        : badge.badgeClass.includes('rose') ? 'bg-rose-500'
                        : 'bg-slate-400'
                    }`}
                    aria-hidden="true"
                  />
                  {/* Date label */}
                  <p className="text-xs font-medium text-slate-500">
                    {dateLabel}{timeLabel ? <> &middot; {timeLabel}</> : null}
                  </p>
                  {/* Card */}
                  <div className="mt-1.5 rounded border border-slate-200 bg-white p-3">
                    <div className="flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-slate-900">{doc.templateId}</p>
                      </div>
                      <span className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${badge.badgeClass}`}>
                        {badge.label}
                      </span>
                    </div>
                    {/* Key fields preview */}
                    {keyFields.length > 0 ? (
                      <dl className="mt-2 space-y-1">
                        {keyFields.map((f) => (
                          <div key={f.fieldKey} className="flex items-baseline gap-2 text-xs">
                            <dt className="shrink-0 text-slate-500">{f.fieldKey}:</dt>
                            <dd className="truncate font-medium text-slate-700">{f.value || '\u2014'}</dd>
                          </div>
                        ))}
                        {doc.fields.length > 3 ? (
                          <p className="text-xs text-slate-400">+{doc.fields.length - 3} more field{doc.fields.length - 3 === 1 ? '' : 's'}</p>
                        ) : null}
                      </dl>
                    ) : null}
                    {/* Open review button */}
                    <button
                      type="button"
                      onClick={() => onOpenReview(doc.intakeId)}
                      className={`mt-2 text-xs font-medium text-sky-700 underline underline-offset-2 hover:text-sky-900 ${FOCUS_RING} rounded px-1 py-0.5 -ml-1`}
                    >
                      Open review
                    </button>
                  </div>
                </li>
              )
            })}
          </ol>
        )}
      </div>
    </div>
  )
}
