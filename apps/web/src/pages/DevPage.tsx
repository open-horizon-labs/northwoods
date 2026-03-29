import { type FormEvent, useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { api, ApiError, documentSourceUrl, userMessage } from '../api'
import { clearStoredAuth, readStoredAuth, storeAuth } from '../lib/auth'
import type {
  CaseAggregateResponse,
  CaseDocumentItem,
  ConfidenceField,
  FinalizeReviewRequest,
  IntakeStatusResponse,
  LoginRequest,
  LoginResponse,
  ReviewDetailResponse,
  ReviewQueueItem,
  SearchResultItem,
  SimilarCase,
  TemplateDescriptor,
  UserRole,
} from '../types'
import { roleLabel, statusBadge, statusLabel } from '../types'

const FOCUS_RING = 'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50'

const focusableButton =
  'inline-flex min-h-11 items-center justify-center rounded-lg border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-900 transition disabled:cursor-not-allowed disabled:opacity-60 disabled:pointer-events-none hover:bg-slate-50'

const focusablePrimary = `${focusableButton} bg-sky-700 text-white border-sky-700 hover:bg-sky-800 ${FOCUS_RING}`
const focusableDanger = `${focusableButton} bg-emerald-700 text-white border-emerald-700 hover:bg-emerald-800 ${FOCUS_RING}`
const focusableSecondary = `${focusableButton} bg-white ${FOCUS_RING}`
const navLinkClass =
  'inline-flex min-h-11 items-center rounded-lg border border-transparent px-3 py-2 text-slate-600 hover:bg-slate-100 hover:text-slate-900 text-sm'

const inputStyle =
  'w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-500'
const inputInteractive = `${inputStyle} ${FOCUS_RING} focus:border-sky-700`

const panelClass = 'rounded-2xl border border-slate-200 bg-white'

const sectionClass = `${panelClass} p-5 sm:p-6`
const labelClass = 'text-sm font-medium text-slate-700'
const helperClass = 'text-xs text-slate-600'

export type LoginForm = {
  tenantId: string
  email: string
  password: string
}

type LoginPreset = LoginForm & { role: UserRole }

type ConfidenceTier = {
  tone: 'high' | 'good' | 'low' | 'review'
  label: string
  className: string
}

const loginPresets: Record<string, LoginPreset> = {
  'tenant-a:worker': {
    tenantId: 'tenant-a',
    email: 'worker@sunrise.example',
    password: 'password',
    role: 0 as UserRole,
  },
  'tenant-a:reviewer': {
    tenantId: 'tenant-a',
    email: 'reviewer@sunrise.example',
    password: 'password',
    role: 1 as UserRole,
  },
  'tenant-b:worker': {
    tenantId: 'tenant-b',
    email: 'worker@lakewood.example',
    password: 'password',
    role: 0 as UserRole,
  },
  'tenant-b:reviewer': {
    tenantId: 'tenant-b',
    email: 'reviewer@lakewood.example',
    password: 'password',
    role: 1 as UserRole,
  },
}

const initialLogin = loginPresets['tenant-a:worker']

const confidenceTone = (confidence: number): ConfidenceTier => {
  if (confidence >= 0.9) {
    return {
      tone: 'high',
      label: 'High',
      className: 'bg-emerald-50 text-emerald-700 border-emerald-200 ring-emerald-200/80',
    }
  }

  if (confidence >= 0.8) {
    return {
      tone: 'good',
      label: 'Good',
      className: 'bg-sky-50 text-sky-700 border-sky-200 ring-sky-200/80',
    }
  }

  if (confidence >= 0.65) {
    return {
      tone: 'low',
      label: 'Needs Review',
      className: 'bg-amber-50 text-amber-700 border-amber-200 ring-amber-200/80',
    }
  }

  return {
    tone: 'review',
    label: 'Review Required',
    className: 'bg-rose-50 text-rose-700 border-rose-200 ring-rose-200/80',
  }
}

const confidencePercent = (confidence: number) => `${Math.round(confidence * 100)}%`

const formatFieldConfidence = (confidence: number) => {
  const score = confidencePercent(confidence)
  const { label } = confidenceTone(confidence)
  return `${label} confidence (${score})`
}

const renderHighlightedSnippet = (snippet: string) => {
  const parts = snippet.split('**')
  return parts.map((part, index) => {
    if (index % 2 === 1) {
      return (
        <mark key={index} className="bg-amber-100 px-0.5 rounded">
          {part}
        </mark>
      )
    }
    return part
  })
}

const SectionTitle = ({
  eyebrow,
  title,
  description,
  titleId,
}: {
  eyebrow: string
  title: string
  description?: string
  titleId: string
}) => {
  return (
    <header className="space-y-2">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{eyebrow}</p>
      <div className="space-y-1">
        <h2 id={titleId} className="text-xl font-semibold text-slate-900">
          {title}
        </h2>
        {description ? <p className="text-sm text-slate-600">{description}</p> : null}
      </div>
    </header>
  )
}

const formatAuditEvent = (eventName: string) => {
  const pretty = eventName
    .split(/[_-]/)
    .filter(Boolean)
    .map((segment) => {
      const normalized = segment.toLowerCase()
      return normalized === 'id' ? 'ID' : normalized.charAt(0).toUpperCase() + normalized.slice(1)
    })

  return pretty.join(' ')
}


export default function DevPage() {
  const [loginForm, setLoginForm] = useState<LoginForm>(initialLogin)
  const [auth, setAuth] = useState<LoginResponse | null>(readStoredAuth)
  const [authError, setAuthError] = useState<string | null>(null)
  const [authBusy, setAuthBusy] = useState(false)

  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [uploadBusy, setUploadBusy] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [activeIntakeId, setActiveIntakeId] = useState<string | null>(null)
  const [intakeStatus, setIntakeStatus] = useState<IntakeStatusResponse | null>(null)

  const [templates, setTemplates] = useState<TemplateDescriptor[]>([])
  const [selectedTemplateId, setSelectedTemplateId] = useState('')
  const [templatesBusy, setTemplatesBusy] = useState(false)
  const [templatesError, setTemplatesError] = useState<string | null>(null)

  const [reviewQueue, setReviewQueue] = useState<ReviewQueueItem[]>([])
  const [reviewQueueSearch, setReviewQueueSearch] = useState('')
  const [queueBusy, setQueueBusy] = useState(false)
  const [queueError, setQueueError] = useState<string | null>(null)
  const [selectedReviewId, setSelectedReviewId] = useState<string | null>(null)

  const [reviewDetail, setReviewDetail] = useState<ReviewDetailResponse | null>(null)
  const [editableFields, setEditableFields] = useState<ConfidenceField[]>([])
  const [reviewerNote, setReviewerNote] = useState('')
  const [finalizeBusy, setFinalizeBusy] = useState(false)
  const [finalizeError, setFinalizeError] = useState<string | null>(null)
  const [reviewLoading, setReviewLoading] = useState(false)
  const [reviewLoadError, setReviewLoadError] = useState<string | null>(null)

  const [searchQuery, setSearchQuery] = useState('')
  const [searchResults, setSearchResults] = useState<SearchResultItem[]>([])
  const [searchBusy, setSearchBusy] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)
  const [searchPerformed, setSearchPerformed] = useState(false)

  const [caseView, setCaseView] = useState<CaseAggregateResponse | null>(null)
  const [caseBusy, setCaseBusy] = useState(false)
  const [caseError, setCaseError] = useState<string | null>(null)

  const [reprocessBusy, setReprocessBusy] = useState(false)
  const [reprocessResult, setReprocessResult] = useState<string | null>(null)
  const [reprocessError, setReprocessError] = useState<string | null>(null)

  const canReview = useMemo(() => auth?.role === 1 || auth?.role === 'Reviewer', [auth])

  const reviewSimilarCases = useMemo(() => reviewDetail?.similarCases ?? [], [reviewDetail])

  const filteredReviewQueue = useMemo(() => {
    const query = reviewQueueSearch.trim().toLowerCase()
    if (!query) {
      return reviewQueue
    }

    return reviewQueue.filter((item) => {
      const haystack = `${item.applicantName} ${item.templateId} ${item.reviewId}`.toLowerCase()
      return haystack.includes(query)
    })
  }, [reviewQueue, reviewQueueSearch])

  const downloadTemplateBlank = useCallback(async (templateId: string) => {
    if (!auth) {
      setTemplatesError('Log in before downloading templates.')
      return
    }

    setTemplatesError(null)

    try {
      const blob = await api.getTemplateBlank(auth.accessToken, templateId)
      const objectUrl = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = objectUrl
      link.download = `${templateId}-template.pdf`
      document.body.appendChild(link)
      link.click()
      document.body.removeChild(link)
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 30_000)
    } catch (error) {
      setTemplatesError(userMessage(error, 'Unable to download blank template. Please try again.'))
    }
  }, [auth])

  const setPreset = (key: keyof typeof loginPresets) => {
    const preset = loginPresets[key]
    setLoginForm({
      tenantId: preset.tenantId,
      email: preset.email,
      password: preset.password,
    })
    clearStoredAuth()
    setAuth(null)
    setAuthError(null)
    setTemplates([])
    setTemplatesBusy(false)
    setTemplatesError(null)
    setSelectedTemplateId('')
    setSelectedFile(null)
    setUploadError(null)
    setActiveIntakeId(null)
    setIntakeStatus(null)
    setReviewQueue([])
    setSelectedReviewId(null)
    setReviewDetail(null)
    setReviewLoadError(null)
    setEditableFields([])
    setReviewerNote('')
    setReviewQueueSearch('')
    setSearchQuery('')
    setSearchResults([])
    setSearchError(null)
    setSearchPerformed(false)
    setCaseView(null)
    setCaseError(null)
  }

  const refreshQueue = useCallback(async (accessToken: string) => {
    setQueueBusy(true)
    setQueueError(null)

    try {
      const queue = await api.getReviewQueue(accessToken)
      setReviewQueue(queue)
      setSelectedReviewId((current) => {
        const nextReview = queue[0]?.reviewId ?? null
        if (!current) {
          return nextReview
        }

        if (queue.some((item) => item.reviewId === current)) {
          return current
        }

        return nextReview
      })
    } catch (error) {
      setQueueError(userMessage(error, 'Unable to load review queue. Please try again.'))
      setReviewQueue([])
      setSelectedReviewId(null)
    } finally {
      setQueueBusy(false)
    }
  }, [])

  const refreshTemplates = useCallback(async (accessToken: string) => {
    setTemplatesBusy(true)
    setTemplatesError(null)

    try {
      const nextTemplates = await api.getTemplates(accessToken)
      setTemplates(nextTemplates)
      if (nextTemplates.length > 0) {
        setSelectedTemplateId((current) => nextTemplates.find((template) => template.id === current)?.id ?? nextTemplates[0].id)
      } else {
        setSelectedTemplateId('')
      }
    } catch (error) {
      setTemplatesError(userMessage(error, 'Unable to load form types. Please try again or contact support.'))
      setTemplates([])
      setSelectedTemplateId('')
    } finally {
      setTemplatesBusy(false)
    }
  }, [])

  const refreshReview = useCallback(async (accessToken: string, reviewId: string) => {
    setReviewLoading(true)
    setReviewLoadError(null)

    try {
      const detail = await api.getReview(accessToken, reviewId)
      setReviewDetail(detail)
      setEditableFields(detail.fields.map((field) => ({ ...field })).sort((a, b) => a.confidence - b.confidence))
    } catch (error) {
      setReviewDetail(null)
      setEditableFields([])
      setReviewLoadError(userMessage(error, 'Unable to load review details. Please try again.'))
    } finally {
      setReviewLoading(false)
    }
  }, [])

  const handleLogin = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setAuthBusy(true)
    setAuthError(null)

    try {
      const nextAuth = await api.login(loginForm as LoginRequest)
      storeAuth(nextAuth)
      setAuth(nextAuth)
      await Promise.all([refreshTemplates(nextAuth.accessToken), refreshQueue(nextAuth.accessToken)])
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) {
        setAuthError('Incorrect email or password. Please try again.')
      } else if (error instanceof ApiError && error.status >= 500) {
        setAuthError('The sign-in service is temporarily unavailable. Please try again later.')
      } else {
        setAuthError(userMessage(error, 'Sign in failed. Please try again.'))
      }
    } finally {
      setAuthBusy(false)
    }
  }

  const handleUpload = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!auth || !selectedFile) {
      return
    }

    setUploadBusy(true)
    setUploadError(null)

    try {
      const created = await api.createIntake(auth.accessToken, selectedTemplateId, selectedFile)
      setActiveIntakeId(created.intakeId)
      const status = await api.getIntake(auth.accessToken, created.intakeId)
      setIntakeStatus(status)
      await refreshQueue(auth.accessToken)
    } catch (error) {
      setUploadError(userMessage(error, 'Upload failed. Please try again.'))
    } finally {
      setUploadBusy(false)
    }
  }

  const restoredAuthOnMountRef = useRef(auth)

  // Restore session after page reload: if auth was recovered from localStorage,
  // fetch templates and review queue so the UI is ready without re-login.
  useEffect(() => {
    const restoredAuth = restoredAuthOnMountRef.current
    if (!restoredAuth) return
    restoredAuthOnMountRef.current = null
    void Promise.all([refreshTemplates(restoredAuth.accessToken), refreshQueue(restoredAuth.accessToken)])
  }, [refreshTemplates, refreshQueue])

  useEffect(() => {
    if (!auth || !activeIntakeId) {
      return
    }

    if (statusLabel(intakeStatus?.status ?? 'Uploaded') === 'Ready for Review' || statusLabel(intakeStatus?.status ?? 'Uploaded') === 'Finalized') {
      return
    }

    const timer = window.setInterval(async () => {
      try {
        const status = await api.getIntake(auth.accessToken, activeIntakeId)
        setIntakeStatus(status)

        if (statusLabel(status.status) === 'Ready for Review' || statusLabel(status.status) === 'Finalized') {
          await refreshQueue(auth.accessToken)
          window.clearInterval(timer)
        }
      } catch {
        window.clearInterval(timer)
      }
    }, 2500)

    return () => window.clearInterval(timer)
  }, [activeIntakeId, auth, intakeStatus?.status, refreshQueue])

  useEffect(() => {
    if (!auth || !selectedReviewId) {
      return
    }

    setReviewDetail(null)
    void refreshReview(auth.accessToken, selectedReviewId)
  }, [auth, refreshReview, selectedReviewId])

  useEffect(() => {
    if (!auth) {
      document.title = 'Northwoods · Intake Review Console'
      return
    }

    if (reviewDetail?.reviewId) {
      document.title = `Review ${reviewDetail.reviewId} · Northwoods`
      return
    }

    document.title = `Northwoods · ${auth.tenantId}`
  }, [auth, reviewDetail?.reviewId])

  const handleFieldChange = (index: number, value: string) => {
    setEditableFields((current) =>
      current.map((field, fieldIndex) =>
        fieldIndex === index
          ? { ...field, value, requiresReview: false, confidence: Math.max(field.confidence, 0.99) }
          : field,
      ),
    )
  }

  const handleFinalize = async () => {
    if (!auth || !selectedReviewId || !reviewDetail) {
      return
    }

    setFinalizeBusy(true)
    setFinalizeError(null)

    const payload: FinalizeReviewRequest = {
      fields: editableFields,
      reviewerNote,
    }

    try {
      await api.finalizeReview(auth.accessToken, selectedReviewId, payload)
      await refreshReview(auth.accessToken, selectedReviewId)
      if (activeIntakeId === selectedReviewId) {
        const latest = await api.getIntake(auth.accessToken, selectedReviewId)
        setIntakeStatus(latest)
      }
      await refreshQueue(auth.accessToken)
    } catch (error) {
      setFinalizeError(userMessage(error, 'Unable to finalize review. Please try again.'))
    } finally {
      setFinalizeBusy(false)
    }
  }

  const handleSearch = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!auth || !searchQuery.trim()) return

    setSearchBusy(true)
    setSearchError(null)
    setSearchPerformed(true)

    try {
      const response = await api.search(auth.accessToken, searchQuery.trim())
      setSearchResults(response.results)
    } catch (error) {
      setSearchError(userMessage(error, 'Search failed. Please try again.'))
      setSearchResults([])
    } finally {
      setSearchBusy(false)
    }
  }

  const openCaseView = async (personKey: string) => {
    if (!auth) return

    setCaseBusy(true)
    setCaseError(null)

    try {
      const response = await api.getCaseAggregate(auth.accessToken, personKey)
      setCaseView(response)
    } catch (error) {
      setCaseError(userMessage(error, 'Unable to load case details. Please try again.'))
      setCaseView(null)
    } finally {
      setCaseBusy(false)
    }
  }

  return (
    <>
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:bg-slate-900 focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-white"
      >
        Skip to main content
      </a>

      <main id="main-content" className="min-h-screen bg-slate-50 text-slate-900">
        <div className="mx-auto flex min-h-screen w-full max-w-7xl flex-col gap-8 px-4 py-6 sm:px-6 lg:px-8">
          <header className={`${panelClass} p-6`}>
            <div className="space-y-4">
              <div className="space-y-2">
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">Northwoods</p>
                <h1 className="max-w-3xl text-3xl font-semibold tracking-tight sm:text-4xl">
                  Review-ready intake queue and field correction workflow
                </h1>
                <p className="max-w-3xl text-sm leading-6 text-slate-600">
                  Login with a seeded tenant role, upload a template-matched intake form, and correct uncertain extracted fields.
                </p>
              </div>

              <nav aria-label="Primary sections" className="flex flex-wrap gap-2 text-sm">
                <a href="#section-auth" className={`${navLinkClass} ${FOCUS_RING}`}>
                  1. Authentication
                </a>
                <a href="#section-templates" className={`${navLinkClass} ${FOCUS_RING}`}>
                  2. Intake workflow
                </a>
                <a href="#section-queue" className={`${navLinkClass} ${FOCUS_RING}`}>
                  3. Review queue
                </a>
                <a href="#section-review" className={`${navLinkClass} ${FOCUS_RING}`}>
                  4. Review detail
                </a>
                <a href="#section-search" className={`${navLinkClass} ${FOCUS_RING}`}>
                  5. Search
                </a>
                <a href="#section-case" className={`${navLinkClass} ${FOCUS_RING}`}>
                  6. Case view
                </a>
              </nav>
            </div>
          </header>

          <section className="grid gap-6 xl:grid-cols-[0.8fr_1.2fr]">
            <div className="space-y-6">
              <section id="section-auth" className={sectionClass} aria-labelledby="section-auth-title">
                <SectionTitle
                  titleId="section-auth-title"
                  eyebrow="1 · Authenticate"
                  title="Sign in with seeded credentials"
                  description="Use the preset buttons or edit credentials manually to establish tenant context."
                />

                <div className="mt-5 flex flex-wrap gap-2">
                  {Object.entries(loginPresets).map(([key, preset]) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => setPreset(key as keyof typeof loginPresets)}
                      className={`${focusableSecondary} text-xs`}
                      aria-pressed={preset.email === loginForm.email && preset.tenantId === loginForm.tenantId}
                    >
                      {preset.tenantId} · {roleLabel(preset.role)}
                    </button>
                  ))}
                </div>

                <form className="mt-5 space-y-4" onSubmit={handleLogin} noValidate>
                  <div className="space-y-1">
                    <label htmlFor="tenant-id" className="block space-y-2">
                      <span className={labelClass}>
                        Tenant ID <span aria-hidden="true" className="text-red-600">*</span>
                        <span className="sr-only">required</span>
                      </span>
                      <input
                        id="tenant-id"
                        value={loginForm.tenantId}
                        onChange={(event) => setLoginForm((current) => ({ ...current, tenantId: event.target.value }))}
                        className={inputInteractive}
                        required
                        aria-required="true"
                        autoComplete="organization"
                      />
                    </label>
                  </div>

                  <div className="space-y-1">
                    <label htmlFor="tenant-email" className="block space-y-2">
                      <span className={labelClass}>
                        Email <span aria-hidden="true" className="text-red-600">*</span>
                        <span className="sr-only">required</span>
                      </span>
                      <input
                        id="tenant-email"
                        type="email"
                        value={loginForm.email}
                        onChange={(event) => setLoginForm((current) => ({ ...current, email: event.target.value }))}
                        className={inputInteractive}
                        required
                        aria-required="true"
                        autoComplete="email"
                      />
                    </label>
                  </div>

                  <div className="space-y-1">
                    <label htmlFor="tenant-password" className="block space-y-2">
                      <span className={labelClass}>
                        Password <span aria-hidden="true" className="text-red-600">*</span>
                        <span className="sr-only">required</span>
                      </span>
                      <input
                        id="tenant-password"
                        type="password"
                        value={loginForm.password}
                        onChange={(event) => setLoginForm((current) => ({ ...current, password: event.target.value }))}
                        className={inputInteractive}
                        required
                        aria-required="true"
                        autoComplete="current-password"
                      />
                    </label>
                  </div>

                  <button
                    type="submit"
                    disabled={authBusy}
                    className={focusablePrimary}
                    aria-live="polite"
                  >
                    {authBusy ? 'Signing in…' : 'Sign in'}
                  </button>

                  <p role="alert" aria-live="assertive" className="min-h-5 text-sm text-rose-700">
                    {authError}
                  </p>

                  {auth ? (
                    <p className="rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800">
                      Signed in to <strong>{auth.tenantId}</strong> as <strong>{roleLabel(auth.role)}</strong>.
                    </p>
                  ) : null}
                </form>
              </section>

              <section id="section-templates" className={sectionClass} aria-labelledby="section-template-title">
                <SectionTitle
                  titleId="section-template-title"
                  eyebrow="2 · Intake templates"
                  title="Choose a template"
                  description="Templates are tenant-scoped and drive field extraction and review schema."
                />

                <div className="mt-5 rounded-lg border border-slate-200 bg-slate-50 p-4">
                  {templatesError ? (
                    <p role="alert" aria-live="assertive" className="text-sm text-rose-700">
                      {templatesError}
                    </p>
                  ) : null}

                  {templatesBusy ? (
                    <p className="text-sm text-slate-600">Loading available templates…</p>
                  ) : templates.length === 0 ? (
                    <p className="text-sm text-slate-600">
                      No templates are available for this tenant. Complete authentication to load templates.
                    </p>
                  ) : (
                    <ul className="space-y-3">
                      {templates.map((template) => {
                        const isSelected = template.id === selectedTemplateId

                        return (
                          <li
                            key={template.id}
                            className={`rounded-lg border p-4 transition ${
                              isSelected
                                ? 'border-sky-300 bg-sky-50'
                                : 'border-slate-200 bg-white'
                            }`}
                            aria-label={`Template ${template.name}`}
                          >
                            <div className="flex flex-wrap items-center justify-between gap-3">
                              <div>
                                <p className="text-sm font-medium text-slate-900">{template.name}</p>
                                <p className="text-xs text-slate-600">Template ID: {template.id}</p>
                                <p className="mt-2 text-xs text-slate-600">{template.fields.length} fields</p>
                              </div>
                              <div className="flex flex-wrap gap-2">
                                <button
                                  type="button"
                                  onClick={() => setSelectedTemplateId(template.id)}
                                  className={`${focusablePrimary} ${isSelected ? 'bg-sky-800' : ''}`}
                                  aria-pressed={isSelected}
                                >
                                  {isSelected ? 'Selected' : 'Select'}
                                </button>
                                {template.blankPdfKey ? (
                                  <button
                                    type="button"
                                    onClick={() => void downloadTemplateBlank(template.id)}
                                    className={focusableSecondary}
                                  >
                                    Download blank
                                  </button>
                                ) : (
                                  <p className="text-xs text-slate-600">
                                    No printable form available -- contact your administrator.
                                  </p>
                                )}
                              </div>
                            </div>
                          </li>
                        )
                      })}
                    </ul>
                  )}
                </div>

                <form className="mt-5 space-y-4" onSubmit={handleUpload} noValidate>
                  <label htmlFor="upload-template" className="block space-y-2">
                    <span className={labelClass}>
                      Template <span aria-hidden="true" className="text-red-600">*</span>
                      <span className="sr-only">required</span>
                    </span>
                    <select
                      id="upload-template"
                      value={selectedTemplateId}
                      onChange={(event) => setSelectedTemplateId(event.target.value)}
                      disabled={!auth || templates.length === 0}
                      className={`${inputInteractive} ${FOCUS_RING}`}
                      required
                      aria-required="true"
                    >
                      {templates.length === 0 ? (
                        <option value="" disabled>
                          {auth ? 'No templates available' : 'Sign in to load templates'}
                        </option>
                      ) : null}
                      {templates.map((template) => (
                        <option key={template.id} value={template.id}>
                          {template.name}
                        </option>
                      ))}
                    </select>
                  </label>

                  <label htmlFor="upload-file" className="block space-y-2">
                    <span className={labelClass}>
                      Handwritten form scan <span aria-hidden="true" className="text-red-600">*</span>
                      <span className="sr-only">required</span>
                    </span>
                    <input
                      id="upload-file"
                      type="file"
                      accept=".pdf,image/*"
                      onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)}
                      className={`${inputInteractive} border-dashed border-slate-300 py-2.5 file:mr-4 file:rounded-lg file:border-0 file:bg-sky-700 file:px-4 file:py-2 file:text-sm file:font-semibold file:text-white file:hover:bg-sky-800`}
                      required
                      aria-required="true"
                      aria-describedby="upload-help upload-file-error"
                    />
                    <p id="upload-help" className={helperClass}>
                      Supported file types: PDF or image uploads.
                    </p>
                  </label>

                  <button
                    type="submit"
                    disabled={!auth || !selectedFile || uploadBusy || templates.length === 0}
                    className={focusablePrimary}
                  >
                    {uploadBusy ? 'Uploading…' : 'Upload intake'}
                  </button>

                  <p
                    id="upload-file-error"
                    role="alert"
                    aria-live="assertive"
                    className="min-h-5 text-sm text-rose-700"
                  >
                    {uploadError}
                  </p>
                </form>
              </section>

              <section className={sectionClass}>
                <header className="flex items-center justify-between gap-4">
                  <div>
                    <h2 className="text-lg font-medium text-slate-900">Current intake status</h2>
                    <p className={helperClass}>Worker polling advances uploaded documents to review-ready.</p>
                  </div>
                  <span className="rounded-full border border-slate-300 bg-slate-100 px-3 py-1 text-xs font-medium text-slate-700">
                    {intakeStatus ? statusLabel(intakeStatus.status) : 'No active intake'}
                  </span>
                </header>

                {activeIntakeId ? (
                  <div className="mt-4 space-y-3 text-sm text-slate-700">
                    <p>
                      Active intake ID: <span className="font-medium">{activeIntakeId}</span>
                    </p>
                    {intakeStatus?.fields?.length ? (
                      <ul className="space-y-2">
                        {intakeStatus.fields.map((field) => {
                          const confidence = confidenceTone(field.confidence)
                          return (
                            <li
                              key={field.fieldKey}
                              className="flex items-center justify-between gap-3 rounded-lg border border-slate-200 bg-white px-3 py-2"
                            >
                              <span>{field.fieldKey}</span>
                              <span
                                className={`rounded-full border px-2 py-1 text-xs font-medium ${confidence.className}`}
                                aria-label={formatFieldConfidence(field.confidence)}
                              >
                                {confidence.label}
                              </span>
                            </li>
                          )
                        })}
                      </ul>
                    ) : (
                      <p className="text-slate-600">
                        No extracted fields yet. Upload a document and wait for extraction to complete.
                      </p>
                    )}
                  </div>
                ) : (
                  <p className="mt-4 text-sm text-slate-600">No intake uploaded in this session yet.</p>
                )}
              </section>
            </div>

            <div className="space-y-6">
              <section id="section-queue" className={sectionClass} aria-labelledby="section-queue-title">
                <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
                  <SectionTitle
                    titleId="section-queue-title"
                    eyebrow="3 · Review queue"
                    title="Review-ready documents"
                    description="Select one document to open extracted fields for correction."
                  />
                  <button
                    type="button"
                    disabled={!auth || queueBusy}
                    onClick={() => auth && refreshQueue(auth.accessToken)}
                    className={focusableSecondary}
                  >
                    {queueBusy ? 'Refreshing…' : 'Refresh queue'}
                  </button>
                </div>

                <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
                  <label htmlFor="review-queue-search" className="w-full text-sm text-slate-700">
                    <span className="sr-only">Search review queue</span>
                    <input
                      id="review-queue-search"
                      type="search"
                      value={reviewQueueSearch}
                      onChange={(event) => setReviewQueueSearch(event.target.value)}
                      placeholder="Search by applicant, template, or review id"
                      className={inputInteractive}
                    />
                  </label>
                  <p className="text-xs text-slate-600" role="status" aria-live="polite">
                    {filteredReviewQueue.length} result{filteredReviewQueue.length === 1 ? '' : 's'}
                  </p>
                </div>

                {queueError ? (
                  <p role="alert" aria-live="assertive" className="mt-4 text-sm text-rose-700">
                    {queueError}
                  </p>
                ) : null}

                {queueBusy ? (
                  <p className="mt-4 text-sm text-slate-600">Loading review queue…</p>
                ) : filteredReviewQueue.length ? (
                  <ul className="mt-4 space-y-3" role="list" aria-live="polite" aria-busy={queueBusy}>
                    {filteredReviewQueue.map((item) => {
                      const isSelected = selectedReviewId === item.reviewId

                      return (
                        <li key={item.reviewId}>
                          <button
                            type="button"
                            onClick={() => setSelectedReviewId(item.reviewId)}
                            aria-pressed={isSelected}
                            aria-current={isSelected ? 'page' : undefined}
                            aria-label={`Open review for ${item.applicantName} with ${item.uncertainFieldCount} uncertain fields`}
                            className={`w-full rounded-lg border px-4 py-3 text-left transition ${
                              isSelected
                                ? 'border-sky-300 bg-sky-50'
                                : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50'
                            } ${focusableSecondary} min-h-11`}
                          >
                            <div className="flex items-center justify-between gap-4">
                              <div>
                                <p className="font-medium text-slate-900">{item.applicantName}</p>
                                <p className={helperClass}>Template {item.templateId}</p>
                              </div>
                              <span className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-xs font-medium text-amber-700">
                                {item.uncertainFieldCount} uncertain
                              </span>
                            </div>
                          </button>
                        </li>
                      )
                    })}
                  </ul>
                ) : (
                  <div className="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-6 text-sm text-slate-600">
                    {reviewQueueSearch
                      ? 'No review items match your search query. Try a different applicant name or template id.'
                      : 'No review-ready documents yet. Upload an intake and wait for the worker to finish extraction.'}
                  </div>
                )}
              </section>

              <section id="section-review" className={sectionClass} aria-labelledby="section-review-title">
                <SectionTitle
                  titleId="section-review-title"
                  eyebrow="4 · Review detail"
                  title="Correct uncertain fields and finalize"
                  description="Use the source confidence, extracted field values, and similar cases as review context."
                />

                {reviewLoading ? (
                  <p className="mt-5 text-sm text-slate-600" role="status" aria-live="polite">
                    Loading review details…
                  </p>
                ) : reviewLoadError ? (
                  <p role="alert" aria-live="assertive" className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
                    {reviewLoadError}
                  </p>
                ) : reviewDetail ? (
                  <div className="mt-5 space-y-6">
                    <div className="flex flex-col gap-3 rounded-lg border border-slate-200 bg-slate-50 p-4 sm:flex-row sm:items-center sm:justify-between">
                      <div className="space-y-1">
                        <p className="text-base font-semibold text-slate-900">Review {reviewDetail.reviewId}</p>
                        <p className={helperClass}>
                          Template {reviewDetail.templateId} · Status {statusLabel(reviewDetail.status)}
                        </p>
                      </div>
                      <a
                        href={auth ? documentSourceUrl(auth.accessToken, reviewDetail.reviewId) : '#'}
                        target="_blank"
                        rel="noreferrer"
                        className={focusableSecondary}
                      >
                        Open in new tab
                      </a>
                    </div>

                    <div className="rounded-lg border border-slate-200 bg-white overflow-hidden">
                      <p className="bg-slate-100 px-4 py-2 text-xs font-medium text-slate-700">Source document</p>
                      <iframe
                        src={auth ? documentSourceUrl(auth.accessToken, reviewDetail.reviewId) : ''}
                        title="Source intake document"
                        className="h-[480px] w-full border-0"
                        sandbox="allow-same-origin"
                      />
                    </div>

                    <div className="grid gap-4">
                      {editableFields.map((field, index) => {
                        const confidence = confidenceTone(field.confidence)
                        const fieldInputId = `review-field-${index}`
                        const confidenceHintId = `${fieldInputId}-confidence`

                        return (
                          <div
                            key={field.fieldKey}
                            className={`rounded-lg border p-4 ${
                              field.requiresReview ? 'border-amber-300 bg-amber-50' : 'border-slate-200 bg-white'
                            }`}
                          >
                            <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                              <div className="space-y-1">
                                <label htmlFor={fieldInputId} className="text-sm font-medium text-slate-900">
                                  {field.fieldKey}
                                </label>
                                <p id={confidenceHintId} className={helperClass}>
                                  {formatFieldConfidence(field.confidence)}
                                </p>
                              </div>
                              <span
                                className={`inline-flex min-h-11 items-center rounded-full border px-2.5 py-1 text-xs font-medium ${confidence.className}`}
                                aria-live="polite"
                              >
                                {confidence.label}
                              </span>
                            </div>
                            <textarea
                              id={fieldInputId}
                              value={field.value}
                              onChange={(event) => handleFieldChange(index, event.target.value)}
                              rows={Math.max(2, Math.ceil((field.value.length + 1) / 40))}
                              aria-describedby={confidenceHintId}
                              className={`${inputInteractive} mt-4`}
                            />
                          </div>
                        )
                      })}
                    </div>

                    <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                        <p className="text-sm font-medium text-slate-900">Similar cases</p>
                        <span className="text-xs text-slate-600">Top {reviewSimilarCases.length}</span>
                      </div>

                      {reviewSimilarCases.length > 0 ? (
                        <div className="mt-3 space-y-3">
                          {reviewSimilarCases.map((item: SimilarCase) => (
                            <button
                              key={item.intakeId}
                              type="button"
                              onClick={() => setSelectedReviewId(item.reviewId)}
                              className={`${focusableSecondary} flex w-full flex-col gap-2 rounded-lg border border-slate-200 bg-white px-4 py-3 text-left sm:flex-row sm:items-center sm:justify-between`}
                            >
                              <div>
                                <p className="text-sm font-medium text-slate-900">{item.applicantName}</p>
                                <p className="text-xs text-slate-600">Template {item.templateId}</p>
                              </div>
                              <span className="rounded-full border border-sky-200 bg-sky-50 px-2.5 py-1 text-xs font-medium text-sky-700">
                                {(item.matchScore * 100).toFixed(0)}% match
                              </span>
                              <p className="text-xs leading-relaxed text-slate-700 sm:col-span-2">{item.summary}</p>
                            </button>
                          ))}
                        </div>
                      ) : (
                        <p className="mt-3 text-sm text-slate-600">No similar cases found for this document yet.</p>
                      )}
                    </div>

                    <label htmlFor="reviewer-note" className="block space-y-2 text-sm text-slate-700">
                      <span>Reviewer note</span>
                      <textarea
                        id="reviewer-note"
                        value={reviewerNote}
                        onChange={(event) => setReviewerNote(event.target.value)}
                        rows={3}
                        className={`${inputInteractive}`}
                        placeholder="Record the decision or correction rationale"
                      />
                    </label>

                    <div className="flex flex-wrap items-center gap-3">
                      <button
                        type="button"
                        disabled={!canReview || finalizeBusy}
                        onClick={handleFinalize}
                        className={focusableDanger}
                      >
                        {finalizeBusy ? 'Finalizing…' : 'Finalize review'}
                      </button>

                      {finalizeError ? (
                        <p role="alert" aria-live="assertive" className="text-sm text-rose-700">
                          {finalizeError}
                        </p>
                      ) : null}
                    </div>

                    <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
                      <p className="text-sm font-medium text-slate-900">Audit trail</p>
                      <ul className="mt-3 space-y-2 text-sm text-slate-700" aria-live="polite">
                        {reviewDetail.auditEvents.map((ev, eventIndex) => (
                          <li
                            key={`${ev.eventType}-${eventIndex}`}
                            className="rounded-md border border-slate-200 bg-white px-3 py-2"
                          >
                            {formatAuditEvent(ev.eventType)}
                          </li>
                        ))}
                      </ul>
                    </div>
                  </div>
                ) : (
                  <div className="mt-5 rounded-lg border border-dashed border-slate-300 bg-slate-50 p-8 text-sm text-slate-600">
                    Sign in and choose a review-ready document to inspect extracted fields and finalize the record.
                  </div>
                )}
              </section>
            </div>
          </section>

          <section id="section-search" className={sectionClass} aria-labelledby="section-search-title">
            <SectionTitle
              titleId="section-search-title"
              eyebrow="5 · Search"
              title="Search processed intakes"
              description="Full-text and fuzzy search across all extracted fields within your tenant."
            />

            <form className="mt-5 flex gap-3" onSubmit={handleSearch} noValidate>
              <label htmlFor="search-query" className="sr-only">Search query</label>
              <input
                id="search-query"
                type="search"
                value={searchQuery}
                onChange={(event) => setSearchQuery(event.target.value)}
                placeholder="Search by name, address, or any extracted field"
                className={`${inputInteractive} flex-1`}
                required
                aria-required="true"
              />
              <button
                type="submit"
                disabled={!auth || searchBusy || !searchQuery.trim()}
                className={focusablePrimary}
              >
                {searchBusy ? 'Searching…' : 'Search'}
              </button>
            </form>

            {searchError ? (
              <p role="alert" aria-live="assertive" className="mt-4 text-sm text-rose-700">
                {searchError}
              </p>
            ) : null}

            {searchBusy ? (
              <p className="mt-4 text-sm text-slate-600" role="status" aria-live="polite">
                Searching…
              </p>
            ) : searchResults.length > 0 ? (
              <div className="mt-4 space-y-3">
                <p className="text-xs text-slate-600" role="status" aria-live="polite">
                  {searchResults.length} result{searchResults.length === 1 ? '' : 's'}
                </p>
                <ul className="space-y-3" role="list">
                  {searchResults.map((item) => {
                    const confidence = confidenceTone(item.confidence)
                    const badge = statusBadge(item.status)
                    return (
                      <li key={item.intakeId}>
                        <button
                          type="button"
                          onClick={() => void openCaseView(item.applicantName)}
                          className={`${focusableSecondary} w-full rounded-lg border border-slate-200 bg-white px-4 py-3 text-left`}
                          aria-label={`View case for ${item.applicantName}`}
                        >
                          <div className="flex items-center justify-between gap-4">
                            <div className="space-y-1">
                              <p className="text-sm font-medium text-slate-900">{item.applicantName}</p>
                              <p className="text-xs text-slate-600">
                                Template {item.templateId}
                              </p>
                              {item.snippet ? (
                                <p className="mt-1 text-xs leading-relaxed text-slate-700">
                                  {renderHighlightedSnippet(item.snippet)}
                                </p>
                              ) : null}
                            </div>
                            <span
                              className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${badge.badgeClass}`}
                            >
                              {badge.label}
                            </span>
                            <span
                              className={`shrink-0 rounded-full border px-2.5 py-1 text-xs font-medium ${confidence.className}`}
                            >
                              {confidencePercent(item.confidence)}
                            </span>
                          </div>
                        </button>
                      </li>
                    )
                  })}
                </ul>
              </div>
            ) : searchPerformed ? (
              <div className="mt-4 rounded-lg border border-dashed border-slate-300 bg-slate-50 p-6 text-sm text-slate-600">
                No results found for your search query. Try different keywords or check spelling.
              </div>
            ) : null}
          </section>

          <section id="section-case" className={sectionClass} aria-labelledby="section-case-title">
            <SectionTitle
              titleId="section-case-title"
              eyebrow="6 · Case view"
              title="Aggregated case documents"
              description="All documents for a person across templates and statuses. Click a search result to view their case."
            />

            {caseError ? (
              <p role="alert" aria-live="assertive" className="mt-4 text-sm text-rose-700">
                {caseError}
              </p>
            ) : null}

            {caseBusy ? (
              <p className="mt-4 text-sm text-slate-600" role="status" aria-live="polite">
                Loading case…
              </p>
            ) : caseView ? (
              <div className="mt-4 space-y-4">
                <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
                  <p className="text-base font-semibold text-slate-900">{caseView.personKey}</p>
                  <p className="text-xs text-slate-600">{caseView.documents.length} document{caseView.documents.length === 1 ? '' : 's'}</p>
                </div>

                {caseView.documents.length === 0 ? (
                  <div className="rounded-lg border border-dashed border-slate-300 bg-slate-50 p-6 text-sm text-slate-600">
                    No documents found for this person in your tenant.
                  </div>
                ) : (
                  <ol className="relative border-l-2 border-slate-200 ml-2 mt-2" aria-label="Document timeline">
                    {caseView.documents.map((doc: CaseDocumentItem) => {
                      const badge = statusBadge(doc.status)
                      const date = new Date(doc.createdAt)
                      const dateLabel = date.toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' })
                      const timeLabel = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
                      return (
                        <li key={doc.intakeId} className="relative mb-6 last:mb-0 pl-8">
                          <span
                            className={`absolute -left-[7px] top-1.5 h-3 w-3 rounded-full border-2 border-white ${
                              badge.badgeClass.includes('emerald') ? 'bg-emerald-500'
                                : badge.badgeClass.includes('amber') ? 'bg-amber-500'
                                : badge.badgeClass.includes('rose') ? 'bg-rose-500'
                                : 'bg-slate-400'
                            }`}
                            aria-hidden="true"
                          />
                          <p className="text-xs font-medium text-slate-500">{dateLabel} &middot; {timeLabel}</p>
                          <div className="mt-1.5 rounded-lg border border-slate-200 bg-white p-4 space-y-3">
                            <div className="flex items-center justify-between gap-4">
                              <div>
                                <p className="text-sm font-medium text-slate-900">Template {doc.templateId}</p>
                              </div>
                              <span className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${badge.badgeClass}`}>
                                {badge.label}
                              </span>
                            </div>
                            <button
                              type="button"
                              onClick={() => setSelectedReviewId(doc.intakeId)}
                              className={focusableSecondary}
                            >
                              Open review
                            </button>
                            {doc.fields.length > 0 ? (
                              <ul className="space-y-1">
                                {doc.fields.map((field) => {
                                  const confidence = confidenceTone(field.confidence)
                                  return (
                                    <li
                                      key={field.fieldKey}
                                      className="flex items-center justify-between gap-3 rounded-md border border-slate-100 bg-slate-50 px-3 py-1.5 text-sm"
                                    >
                                      <span className="text-slate-700">{field.fieldKey}: <span className="font-medium text-slate-900">{field.value}</span></span>
                                      <span
                                        className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-medium ${confidence.className}`}
                                      >
                                        {confidence.label}
                                      </span>
                                    </li>
                                  )
                                })}
                              </ul>
                            ) : (
                              <p className="text-sm text-slate-600">No extracted fields.</p>
                            )}
                          </div>
                        </li>
                      )
                    })}
                  </ol>
                )}
              </div>
            ) : (
              <div className="mt-4 rounded-lg border border-dashed border-slate-300 bg-slate-50 p-8 text-sm text-slate-600">
                Search for an applicant above and click a result to view their aggregated case documents.
              </div>
            )}
          </section>

          <section id="section-admin" className={sectionClass} aria-labelledby="section-admin-title">
            <SectionTitle
              titleId="section-admin-title"
              eyebrow="7 · Admin"
              title="Admin operations"
              description="Reprocess all documents to re-run extraction and capture discovered fields and interviewDate."
            />
            <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center">
              <button
                type="button"
                disabled={!auth || reprocessBusy}
                onClick={async () => {
                  if (!auth) return
                  setReprocessBusy(true)
                  setReprocessResult(null)
                  setReprocessError(null)
                  try {
                    const result = await api.reprocessDocuments(auth.accessToken)
                    setReprocessResult(`Queued ${result.queued} document(s) for reprocessing (tenant: ${result.tenantId}).`)
                  } catch (err) {
                    setReprocessError(userMessage(err, 'Reprocess failed.'))
                  } finally {
                    setReprocessBusy(false)
                  }
                }}
                className={focusableSecondary}
              >
                {reprocessBusy ? 'Queuing…' : 'Reprocess all documents'}
              </button>
              {reprocessResult ? (
                <p className="text-sm text-emerald-700">{reprocessResult}</p>
              ) : reprocessError ? (
                <p className="text-sm text-rose-700">{reprocessError}</p>
              ) : null}
            </div>
            <p className={`mt-2 ${helperClass}`}>
              Resets all processed documents to uploaded status and clears extracted fields. The extraction worker will re-run extraction for all documents, capturing discovered fields and populating interviewDate.
            </p>
          </section>
        </div>
      </main>
    </>
  )
}
