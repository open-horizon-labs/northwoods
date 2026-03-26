import type {
  CreateIntakeResponse,
  FinalizeReviewRequest,
  FinalizeReviewResponse,
  IntakeStatusResponse,
  LoginRequest,
  LoginResponse,
  ReviewDetailResponse,
  ReviewQueueItem,
} from './types'

const API_BASE = '/api'

const jsonHeaders = (tenantId?: string) => ({
  'Content-Type': 'application/json',
  ...(tenantId ? { 'X-Tenant-Id': tenantId } : {}),
})

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const text = await response.text()
    throw new Error(text || `${response.status} ${response.statusText}`)
  }

  return (await response.json()) as T
}

export const api = {
  login: async (payload: LoginRequest) => {
    const response = await fetch(`${API_BASE}/auth/login`, {
      method: 'POST',
      headers: jsonHeaders(),
      body: JSON.stringify(payload),
    })
    return handleResponse<LoginResponse>(response)
  },

  createIntake: async (tenantId: string, templateId: string, file: File) => {
    const form = new FormData()
    form.append('templateId', templateId)
    form.append('file', file)

    const response = await fetch(`${API_BASE}/intakes`, {
      method: 'POST',
      headers: tenantId ? { 'X-Tenant-Id': tenantId } : undefined,
      body: form,
    })
    return handleResponse<CreateIntakeResponse>(response)
  },

  getIntake: async (tenantId: string, intakeId: string) => {
    const response = await fetch(`${API_BASE}/intakes/${intakeId}`, {
      headers: tenantId ? { 'X-Tenant-Id': tenantId } : undefined,
    })
    return handleResponse<IntakeStatusResponse>(response)
  },

  getReviewQueue: async (tenantId: string) => {
    const response = await fetch(`${API_BASE}/review-queue`, {
      headers: tenantId ? { 'X-Tenant-Id': tenantId } : undefined,
    })
    return handleResponse<ReviewQueueItem[]>(response)
  },

  getReview: async (tenantId: string, reviewId: string) => {
    const response = await fetch(`${API_BASE}/reviews/${reviewId}`, {
      headers: tenantId ? { 'X-Tenant-Id': tenantId } : undefined,
    })
    return handleResponse<ReviewDetailResponse>(response)
  },

  finalizeReview: async (tenantId: string, reviewId: string, payload: FinalizeReviewRequest) => {
    const response = await fetch(`${API_BASE}/reviews/${reviewId}/finalize`, {
      method: 'POST',
      headers: jsonHeaders(tenantId),
      body: JSON.stringify(payload),
    })
    return handleResponse<FinalizeReviewResponse>(response)
  },
}
