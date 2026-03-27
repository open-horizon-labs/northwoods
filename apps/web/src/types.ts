export type UserRole = 0 | 1 | 'IntakeWorker' | 'Reviewer'
export type ProcessingStatus = 0 | 1 | 2 | 3 | 4 | 'Uploaded' | 'Extracting' | 'ReviewReady' | 'Finalized' | 'Failed'

export type LoginRequest = {
  email: string
  password: string
  tenantId: string
  role: UserRole
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
}

export type SimilarCase = {
  reviewId: string
  intakeId: string
  applicantName: string
  templateId: string
  matchScore: number
  summary: string
}

export type ReviewDetailResponse = {
  reviewId: string
  intakeId: string
  tenantId: string
  templateId: string
  sourceDocumentUrl: string
  status: ProcessingStatus
  fields: ConfidenceField[]
  similarCases: SimilarCase[]
  auditEvents: string[]
}

export type FinalizeReviewRequest = {
  fields: ConfidenceField[]
  reviewerNote: string
}

export type FinalizeReviewResponse = {
  reviewId: string
  status: ProcessingStatus
}

export const statusLabel = (status: ProcessingStatus) => {
  switch (status) {
    case 0:
    case 'Uploaded':
      return 'Uploaded'
    case 1:
    case 'Extracting':
      return 'Extracting'
    case 2:
    case 'ReviewReady':
      return 'Review Ready'
    case 3:
    case 'Finalized':
      return 'Finalized'
    case 4:
    case 'Failed':
      return 'Failed'
    default:
      return String(status)
  }
}

export const roleLabel = (role: UserRole) => {
  switch (role) {
    case 0:
    case 'IntakeWorker':
      return 'Intake Worker'
    case 1:
    case 'Reviewer':
      return 'Reviewer'
    default:
      return String(role)
  }
}
