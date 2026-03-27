import type {
  CaseAggregateResponse,
  CreateIntakeResponse,
  CreateTemplateRequest,
  FinalizeReviewRequest,
  FinalizeReviewResponse,
  IntakeStatusResponse,
  LoginRequest,
  LoginResponse,
  ReviewDetailResponse,
  ReviewQueueItem,
  SearchResponse,
  TemplateDescriptor,
  UpdateTemplateRequest,
} from './types'

const API_BASE = import.meta.env.VITE_API_URL || '/api'

const jsonHeaders = (accessToken?: string) => ({
  'Content-Type': 'application/json',
  ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
})

const authHeader = (accessToken?: string) =>
  accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined

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

  getTemplates: async (accessToken: string) => {
    const response = await fetch(`${API_BASE}/templates`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<TemplateDescriptor[]>(response)
  },

  getTemplateBlank: async (accessToken: string, templateId: string, download: boolean) => {
    const mode = download ? '?download=true' : '?download=false'
    const response = await fetch(`${API_BASE}/templates/${encodeURIComponent(templateId)}/blank${mode}`, {
      headers: {
        ...(authHeader(accessToken) ?? {}),
        Accept: 'text/html',
      },
    })

    if (!response.ok) {
      const text = await response.text()
      throw new Error(text || `${response.status} ${response.statusText}`)
    }

    return response.blob()
  },

  createIntake: async (accessToken: string, templateId: string, file: File) => {
    const form = new FormData()
    form.append('templateId', templateId)
    form.append('file', file)

    const response = await fetch(`${API_BASE}/intakes`, {
      method: 'POST',
      headers: authHeader(accessToken),
      body: form,
    })
    return handleResponse<CreateIntakeResponse>(response)
  },

  getIntake: async (accessToken: string, intakeId: string) => {
    const response = await fetch(`${API_BASE}/intakes/${intakeId}`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<IntakeStatusResponse>(response)
  },

  getReviewQueue: async (accessToken: string) => {
    const response = await fetch(`${API_BASE}/review-queue`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<ReviewQueueItem[]>(response)
  },

  getReview: async (accessToken: string, reviewId: string) => {
    const response = await fetch(`${API_BASE}/reviews/${reviewId}`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<ReviewDetailResponse>(response)
  },

  finalizeReview: async (accessToken: string, reviewId: string, payload: FinalizeReviewRequest) => {
    const response = await fetch(`${API_BASE}/reviews/${reviewId}/finalize`, {
      method: 'POST',
      headers: jsonHeaders(accessToken),
      body: JSON.stringify(payload),
    })
    return handleResponse<FinalizeReviewResponse>(response)
  },

  search: async (accessToken: string, query: string) => {
    const response = await fetch(`${API_BASE}/search?q=${encodeURIComponent(query)}`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<SearchResponse>(response)
  },

  getCaseAggregate: async (accessToken: string, personKey: string) => {
    const response = await fetch(`${API_BASE}/cases/${encodeURIComponent(personKey)}`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<CaseAggregateResponse>(response)
  },

  createTemplate: async (accessToken: string, payload: CreateTemplateRequest) => {
    const response = await fetch(`${API_BASE}/templates`, {
      method: 'POST',
      headers: jsonHeaders(accessToken),
      body: JSON.stringify(payload),
    })
    return handleResponse<TemplateDescriptor>(response)
  },

  updateTemplate: async (accessToken: string, templateId: string, payload: UpdateTemplateRequest) => {
    const response = await fetch(`${API_BASE}/templates/${encodeURIComponent(templateId)}`, {
      method: 'PUT',
      headers: jsonHeaders(accessToken),
      body: JSON.stringify(payload),
    })
    return handleResponse<TemplateDescriptor>(response)
  },

  archiveTemplate: async (accessToken: string, templateId: string) => {
    const response = await fetch(`${API_BASE}/templates/${encodeURIComponent(templateId)}`, {
      method: 'DELETE',
      headers: authHeader(accessToken),
    })
    if (!response.ok) {
      const text = await response.text()
      throw new Error(text || `${response.status} ${response.statusText}`)
    }
  },

  uploadTemplateBlankPdf: async (accessToken: string, templateId: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    const response = await fetch(`${API_BASE}/templates/${encodeURIComponent(templateId)}/blank-pdf`, {
      method: 'POST',
      headers: authHeader(accessToken),
      body: form,
    })
    return handleResponse<{ templateId: string; blankPdfKey: string }>(response)
  },

  getTemplatesIncludingArchived: async (accessToken: string) => {
    const response = await fetch(`${API_BASE}/templates?includeArchived`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<TemplateDescriptor[]>(response)
  },
}
