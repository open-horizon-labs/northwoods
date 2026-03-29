import type {
  CaseAggregateResponse,
  CreateIntakeResponse,
  CreateTemplateRequest,
  DocumentListItem,
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

type RetryIntakeResponse = {
  intakeId: string
  status: string
}

const API_BASE = import.meta.env.VITE_API_URL || '/api'

// Global 401 handler — set once at app startup. Decouples api.ts from the
// React component tree so any fetch can trigger redirect-to-login without
// importing App or Router.
let _onUnauthorized: (() => void) | null = null

export function setUnauthorizedHandler(handler: () => void): void {
  _onUnauthorized = handler
}

const jsonHeaders = (accessToken?: string) => ({
  'Content-Type': 'application/json',
  ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
})

const authHeader = (accessToken?: string) =>
  accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined

export class ApiError extends Error {
  readonly status: number
  readonly statusText: string
  readonly body: string

  constructor(status: number, statusText: string, body: string) {
    super(`${status} ${statusText}`)
    this.name = 'ApiError'
    this.status = status
    this.statusText = statusText
    this.body = body
  }
}

function isNetworkError(err: unknown): boolean {
  return err instanceof TypeError && (err.message.includes('fetch') || err.message.includes('network') || err.message === 'Failed to fetch')
}

/**
 * Translate an API or network error into a human-readable message.
 *
 * @param err  - The caught error (ApiError, TypeError from fetch, or unknown)
 * @param fallback - Context-specific fallback shown when we can't be more specific
 */
export function userMessage(err: unknown, fallback: string): string {
  if (isNetworkError(err)) {
    return 'Unable to reach the server. Please check your connection and try again.'
  }

  if (err instanceof ApiError) {
    if (err.status === 401) return 'Your session has expired. Please sign in again.'
    if (err.status === 403) return 'You do not have permission to perform this action.'
    if (err.status === 404) return 'The requested resource was not found.'
    if (err.status >= 500) return `${fallback} The service may be temporarily unavailable.`
  }

  return fallback
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.text()
    const err = new ApiError(response.status, response.statusText, body)
    if (response.status === 401 && _onUnauthorized) {
      _onUnauthorized()
    }
    throw err
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

  getTemplateBlank: async (accessToken: string, templateId: string) => {
    const response = await fetch(`${API_BASE}/templates/${encodeURIComponent(templateId)}/blank`, {
      headers: {
        ...(authHeader(accessToken) ?? {}),
        Accept: 'application/pdf',
      },
    })

    if (!response.ok) {
      const body = await response.text()
      throw new ApiError(response.status, response.statusText, body)
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
      const body = await response.text()
      throw new ApiError(response.status, response.statusText, body)
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

  getDocuments: async (accessToken: string) => {
    const response = await fetch(`${API_BASE}/documents`, {
      headers: authHeader(accessToken),
    })
    return handleResponse<DocumentListItem[]>(response)
  },

  retryIntake: async (accessToken: string, intakeId: string) => {
    const response = await fetch(`${API_BASE}/intakes/${intakeId}/retry`, {
      method: 'POST',
      headers: authHeader(accessToken),
    })
    return handleResponse<RetryIntakeResponse>(response)
  },

  reprocessDocuments: async (accessToken: string) => {
    const response = await fetch(`${API_BASE}/admin/reprocess`, {
      method: 'POST',
      headers: authHeader(accessToken),
    })
    return handleResponse<{ queued: number; tenantId: string }>(response)
  },
}


/** Build an authenticated URL for streaming a document source (PDF) in iframes/links. */
export function documentSourceUrl(accessToken: string, documentId: string): string {
  return `${API_BASE}/documents/${encodeURIComponent(documentId)}/source?access_token=${encodeURIComponent(accessToken)}`
}

/**
 * Fetch a document PDF as an ArrayBuffer using the Authorization header.
 * Avoids embedding the token in the URL (which triggers Edge SmartScreen).
 */
export async function fetchDocumentPdfBuffer(accessToken: string, documentId: string): Promise<ArrayBuffer> {
  const res = await fetch(`${API_BASE}/documents/${encodeURIComponent(documentId)}/source`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
  if (!res.ok) {
    const body = await res.text()
    throw new ApiError(res.status, res.statusText, body)
  }
  return res.arrayBuffer()
}
