/**
 * Shared style constants and utility functions used across review components.
 */

export const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

export const btn =
  'inline-flex min-h-11 items-center justify-center rounded border px-4 py-2.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-60'

export const btnSuccess = `${btn} border-emerald-700 bg-emerald-700 text-white hover:bg-emerald-800 ${FOCUS_RING}`
export const btnSecondary = `${btn} border-slate-300 bg-white text-slate-700 hover:bg-slate-50 ${FOCUS_RING}`

export const inputStyle =
  'w-full rounded border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600'
export const inputInteractive = `${inputStyle} ${FOCUS_RING}`

export type ConfidenceTier = {
  label: string
  className: string
  badgeClass: string
}

export const confidenceTone = (confidence: number): ConfidenceTier => {
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

export const confidencePercent = (c: number) => `${Math.round(c * 100)}%`

export const consensusNoteColor = (note: string): string => {
  if (note.includes('agree')) return 'text-emerald-700'
  if (note.includes('Single')) return 'text-amber-700'
  if (note.includes('disagree')) return 'text-rose-700'
  return 'text-slate-600'
}

export const attemptConfidenceColor = (confidence: number): string => {
  if (confidence >= 0.8) return 'text-emerald-700'
  if (confidence >= 0.65) return 'text-amber-700'
  return 'text-rose-700'
}

export const formatAuditEventType = (name: string) =>
  name
    .split(/[_-]/)
    .filter(Boolean)
    .map((s) => {
      const lower = s.toLowerCase()
      return lower === 'id' ? 'ID' : lower.charAt(0).toUpperCase() + lower.slice(1)
    })
    .join(' ')

export const formatAuditTimestamp = (iso: string): string => {
  try {
    const d = new Date(iso)
    return d.toLocaleString([], {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return iso
  }
}
