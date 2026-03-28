export type UserRole = 0 | 1 | 2 | 'IntakeWorker' | 'Reviewer' | 'Admin'
export type ProcessingStatus = 0 | 1 | 2 | 3 | 4 | 5 | 'Uploaded' | 'Extracting' | 'ReviewReady' | 'Completed' | 'Finalized' | 'Failed'

export type LoginRequest = {
  email: string
  password: string
  tenantId?: string
}

export type LoginResponse = {
  accessToken: string
  tenantId: string
  role: UserRole
}

export type ConfidenceField = {
  fieldKey: string
  value: string
  confidence: number
  requiresReview: boolean
}

export type FieldAttempt = {
  provider: string
  value: string
  confidence: number
}

export type ReviewField = {
  fieldKey: string
  value: string
  confidence: number
  requiresReview: boolean
  attempts: FieldAttempt[]
  consensusNote: string
}

export type CreateIntakeResponse = {
  intakeId: string
  status: ProcessingStatus
}

export type IntakeStatusResponse = {
  intakeId: string
  tenantId: string
  templateId: string
  status: ProcessingStatus
  fields: ConfidenceField[]
}

export type ReviewQueueItem = {
  reviewId: string
  intakeId: string
  applicantName: string
  templateId: string
  uncertainFieldCount: number
  uploadDate: string
}
export type SimilarCase = {
  reviewId: string
  intakeId: string
  applicantName: string
  templateId: string
  matchScore: number
  summary: string
  signals: string[]
}

export type AuditEventItem = {
  eventType: string
  createdAt: string
  actorEmail: string | null
}

export type ReviewDetailResponse = {
  reviewId: string
  intakeId: string
  tenantId: string
  templateId: string
  sourceDocumentUrl: string
  status: ProcessingStatus
  fields: ReviewField[]
  similarCases: SimilarCase[]
  auditEvents: AuditEventItem[]
  failureReason?: string | null
}

export type FinalizeReviewRequest = {
  fields: ConfidenceField[]
  reviewerNote: string
}

export type FinalizeReviewResponse = {
  reviewId: string
  status: ProcessingStatus
}

export type TemplateField = {
  key: string
  type: string
  required: boolean
}

export type TemplateDescriptor = {
  id: string
  name: string
  fields: TemplateField[]
  isArchived?: boolean
  blankPdfKey?: string | null
}

export const statusLabel = (status: ProcessingStatus) => {
  switch (status) {
    case 0:
    case 'Uploaded':
      return 'Queued'
    case 1:
    case 'Extracting':
      return 'Processing'
    case 2:
    case 'ReviewReady':
      return 'Ready for Review'
    case 3:
    case 'Completed':
      return 'Finalized'
    case 4:
    case 'Finalized':
      return 'Finalized'
    case 5:
    case 'Failed':
      return 'Failed'
    default:
      return String(status)
  }
}

export type StatusBadgeConfig = {
  label: string
  badgeClass: string
}

/**
 * Maps a document lifecycle status string to a display label and badge styling.
 * Colors follow AGENTS.md palette: amber=review, green=finalized, red=failed, gray=processing/queued.
 */
export const statusBadge = (status: string): StatusBadgeConfig => {
  const s = status.toLowerCase().replace(/_/g, '')
  if (s === 'reviewready' || s === '2')
    return { label: 'Ready for Review', badgeClass: 'border-amber-200 bg-amber-50 text-amber-700' }
  if (s === 'completed' || s === 'finalized' || s === '3' || s === '4')
    return { label: 'Finalized', badgeClass: 'border-emerald-200 bg-emerald-50 text-emerald-700' }
  if (s === 'extracting' || s === '1')
    return { label: 'Processing', badgeClass: 'border-slate-200 bg-slate-100 text-slate-600' }
  if (s === 'failed' || s === '5')
    return { label: 'Failed', badgeClass: 'border-rose-200 bg-rose-50 text-rose-700' }
  if (s === 'uploaded' || s === '0')
    return { label: 'Queued', badgeClass: 'border-slate-200 bg-slate-100 text-slate-600' }
  return { label: status, badgeClass: 'border-slate-200 bg-slate-100 text-slate-600' }
}

export const roleLabel = (role: UserRole): string => {
  switch (role) {
    case 0:
    case 'IntakeWorker':
      return 'Intake Worker'
    case 1:
    case 'Reviewer':
      return 'Reviewer'
    case 2:
    case 'Admin':
      return 'Admin'
    default:
      return String(role)
  }
}


export type SearchResultItem = {
  intakeId: string
  templateId: string
  applicantName: string
  status: string
  confidence: number
  snippet: string
}

export type SearchResponse = {
  query: string
  results: SearchResultItem[]
}

export type CaseDocumentItem = {
  intakeId: string
  templateId: string
  status: string
  createdAt: string
  fields: ConfidenceField[]
}

export type CaseAggregateResponse = {
  personKey: string
  documents: CaseDocumentItem[]
}

export type CreateTemplateRequest = {
  id: string
  name: string
  fields: TemplateField[]
}

export type UpdateTemplateRequest = {
  name?: string
  fields?: TemplateField[]
}

export type DocumentListItem = {
  documentId: string
  templateId: string
  applicantName: string
  status: string
  createdAt: string
}