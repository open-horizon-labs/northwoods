import { useCallback, useEffect, useMemo, useState } from 'react'

import { api } from './api'
import type {
  ConfidenceField,
  FinalizeReviewRequest,
  IntakeStatusResponse,
  LoginRequest,
  LoginResponse,
  ReviewDetailResponse,
  ReviewQueueItem,
  SimilarCase,
  TemplateDescriptor,
  UserRole,
} from './types'
import { roleLabel, statusLabel } from './types'

type LoginForm = {
  tenantId: string
  email: string
  password: string
}

type LoginPreset = LoginForm & { role: UserRole }

const loginPresets: Record<string, LoginPreset> = {
  'tenant-a:worker': {
    tenantId: 'tenant-a',
    email: 'worker@sunrise.example',
    password: 'dev',
    role: 0 as UserRole,
  },
  'tenant-a:reviewer': {
    tenantId: 'tenant-a',
    email: 'reviewer@sunrise.example',
    password: 'dev',
    role: 1 as UserRole,
  },
  'tenant-b:worker': {
    tenantId: 'tenant-b',
    email: 'worker@lakewood.example',
    password: 'dev',
    role: 0 as UserRole,
  },
  'tenant-b:reviewer': {
    tenantId: 'tenant-b',
    email: 'reviewer@lakewood.example',
    password: 'dev',
    role: 1 as UserRole,
  },
}

const initialLogin = loginPresets['tenant-a:worker']

const confidenceTone = (confidence: number) => {
  if (confidence >= 0.9) return 'bg-emerald-500/15 text-emerald-300 ring-1 ring-emerald-500/30'
  if (confidence >= 0.8) return 'bg-sky-500/15 text-sky-300 ring-1 ring-sky-500/30'
  if (confidence >= 0.65) return 'bg-amber-500/15 text-amber-200 ring-1 ring-amber-500/30'
  return 'bg-rose-500/15 text-rose-200 ring-1 ring-rose-500/30'
}

const SectionTitle = ({ eyebrow, title, description }: { eyebrow: string; title: string; description?: string }) => {
  return (
    <div className="space-y-2">
      <p className="text-xs font-semibold uppercase tracking-[0.24em] text-sky-300">{eyebrow}</p>
      <div className="space-y-1">
        <h2 className="text-xl font-semibold text-white">{title}</h2>
        {description ? <p className="text-sm text-slate-300">{description}</p> : null}
      </div>
    </div>
  )
}

export default function App() {
  const [loginForm, setLoginForm] = useState<LoginForm>(initialLogin)
  const [auth, setAuth] = useState<LoginResponse | null>(null)
  const [authError, setAuthError] = useState<string | null>(null)
  const [authBusy, setAuthBusy] = useState(false)

  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [uploadBusy, setUploadBusy] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [activeIntakeId, setActiveIntakeId] = useState<string | null>(null)
  const [intakeStatus, setIntakeStatus] = useState<IntakeStatusResponse | null>(null)

  const [templates, setTemplates] = useState<TemplateDescriptor[]>([])
  const [selectedTemplateId, setSelectedTemplateId] = useState('general-assistance')
  const [templatesBusy, setTemplatesBusy] = useState(false)
  const [templatesError, setTemplatesError] = useState<string | null>(null)

  const [reviewQueue, setReviewQueue] = useState<ReviewQueueItem[]>([])
  const [queueBusy, setQueueBusy] = useState(false)
  const [queueError, setQueueError] = useState<string | null>(null)
  const [selectedReviewId, setSelectedReviewId] = useState<string | null>(null)
  const [reviewDetail, setReviewDetail] = useState<ReviewDetailResponse | null>(null)
  const [editableFields, setEditableFields] = useState<ConfidenceField[]>([])
  const [reviewerNote, setReviewerNote] = useState('')
  const [finalizeBusy, setFinalizeBusy] = useState(false)
  const [finalizeError, setFinalizeError] = useState<string | null>(null)

  const canReview = useMemo(
    () => auth?.role === 1 || auth?.role === 'Reviewer',
    [auth],
  )
  const reviewSimilarCases = useMemo(() => reviewDetail?.similarCases ?? [], [reviewDetail])

  const openTemplateBlank = useCallback(async (templateId: string, download: boolean) => {
    if (!auth) {
      setTemplatesError('Log in before viewing or downloading templates.')
      return
    }

    setTemplatesError(null)

    try {
      const blob = await api.getTemplateBlank(auth.accessToken, templateId, download)
      const objectUrl = URL.createObjectURL(blob)

      if (download) {
        const link = document.createElement('a')
        link.href = objectUrl
        link.download = `${templateId}-template.html`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
      } else {
        window.open(objectUrl, '_blank', 'noopener,noreferrer')
      }

      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 30_000)
    } catch (error) {
      setTemplatesError(error instanceof Error ? error.message : 'Failed to load blank template.')
    }
  }, [auth])

  const setPreset = (key: keyof typeof loginPresets) => {
    const preset = loginPresets[key]
    setLoginForm({
      tenantId: preset.tenantId,
      email: preset.email,
      password: preset.password,
    })
    setAuth(null)
    setAuthError(null)
    setTemplates([])
    setTemplatesBusy(false)
    setTemplatesError(null)
    setSelectedTemplateId('general-assistance')
    setSelectedFile(null)
    setUploadError(null)
    setActiveIntakeId(null)
    setIntakeStatus(null)
    setReviewQueue([])
    setSelectedReviewId(null)
    setReviewDetail(null)
    setEditableFields([])
    setReviewerNote('')
  }

  const refreshQueue = useCallback(async (accessToken: string) => {
    setQueueBusy(true)
    setQueueError(null)
    try {
      const queue = await api.getReviewQueue(accessToken)
      setReviewQueue(queue)
      setSelectedReviewId((current) => current ?? queue[0]?.reviewId ?? null)
    } catch (error) {
      setQueueError(error instanceof Error ? error.message : 'Failed to load review queue.')
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
        setSelectedTemplateId('general-assistance')
      }
    } catch (error) {
      setTemplatesError(error instanceof Error ? error.message : 'Failed to load template catalog.')
      setTemplates([])
    } finally {
      setTemplatesBusy(false)
    }
  }, [])

  const refreshReview = useCallback(async (accessToken: string, reviewId: string) => {
    const detail = await api.getReview(accessToken, reviewId)
    setReviewDetail(detail)
    setEditableFields(detail.fields.map((field) => ({ ...field })))
  }, [])

  const handleLogin = async (event: React.FormEvent) => {
    event.preventDefault()
    setAuthBusy(true)
    setAuthError(null)

    try {
      const nextAuth = await api.login(loginForm as LoginRequest)
      setAuth(nextAuth)
      await Promise.all([refreshTemplates(nextAuth.accessToken), refreshQueue(nextAuth.accessToken)])
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Login failed.')
    } finally {
      setAuthBusy(false)
    }
  }

  const handleUpload = async (event: React.FormEvent) => {
    event.preventDefault()
    if (!auth || !selectedFile) return

    setUploadBusy(true)
    setUploadError(null)

    try {
      const created = await api.createIntake(auth.accessToken, selectedTemplateId, selectedFile)
      setActiveIntakeId(created.intakeId)
      const status = await api.getIntake(auth.accessToken, created.intakeId)
      setIntakeStatus(status)
      await refreshQueue(auth.accessToken)
    } catch (error) {
      setUploadError(error instanceof Error ? error.message : 'Upload failed.')
    } finally {
      setUploadBusy(false)
    }
  }

  useEffect(() => {
    if (!auth || !activeIntakeId) return

    if (statusLabel(intakeStatus?.status ?? 'Uploaded') === 'Review Ready' || statusLabel(intakeStatus?.status ?? 'Uploaded') === 'Finalized') {
      return
    }

    const timer = window.setInterval(async () => {
      try {
        const status = await api.getIntake(auth.accessToken, activeIntakeId)
        setIntakeStatus(status)
        if (
          statusLabel(status.status) === 'Review Ready' ||
          statusLabel(status.status) === 'Finalized'
        ) {
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
    if (!auth || !selectedReviewId) return

    refreshReview(auth.accessToken, selectedReviewId).catch(() => {
      setReviewDetail(null)
    })
  }, [auth, refreshReview, selectedReviewId])

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
    if (!auth || !selectedReviewId || !reviewDetail) return

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
      setFinalizeError(error instanceof Error ? error.message : 'Failed to finalize review.')
    } finally {
      setFinalizeBusy(false)
    }
  }

  return (
    <main className="min-h-screen bg-slate-950 text-slate-100">
      <div className="mx-auto flex min-h-screen w-full max-w-7xl flex-col gap-8 px-4 py-6 sm:px-6 lg:px-8">
        <header className="rounded-3xl border border-white/10 bg-slate-900/80 p-6 shadow-2xl shadow-slate-950/40 backdrop-blur">
          <div className="space-y-4">
            <p className="text-xs font-semibold uppercase tracking-[0.24em] text-sky-300">Northwoods · review-ready intake loop</p>
            <div className="grid gap-4 lg:grid-cols-[1.3fr_0.7fr] lg:items-end">
              <div className="space-y-3">
                <h1 className="max-w-3xl text-4xl font-semibold tracking-tight text-white sm:text-5xl">
                  Upload a handwritten intake, extract a reviewable draft, and finalize uncertainty.
                </h1>
                <p className="max-w-3xl text-base leading-7 text-slate-300">
                  This frontend drives the exact vertical slice we chose: one tenant-safe workflow from login to upload,
                  asynchronous extraction, reviewer correction, and finalization.
                </p>
              </div>
              <div className="grid gap-3 rounded-2xl border border-white/10 bg-slate-950/60 p-4 text-sm text-slate-300">
                <div className="flex items-center justify-between gap-4">
                  <span>API</span>
                  <code className="rounded bg-slate-800 px-2 py-1 text-sky-200">/api via Vite proxy</code>
                </div>
                <div className="flex items-center justify-between gap-4">
                  <span>Storage</span>
                  <code className="rounded bg-slate-800 px-2 py-1 text-sky-200">MinIO</code>
                </div>
                <div className="flex items-center justify-between gap-4">
                  <span>Persistence</span>
                  <code className="rounded bg-slate-800 px-2 py-1 text-sky-200">Postgres 18 + RLS</code>
                </div>
              </div>
            </div>
          </div>
        </header>

        <section className="grid gap-6 xl:grid-cols-[0.8fr_1.2fr]">
          <div className="space-y-6">
            <section className="rounded-3xl border border-white/10 bg-slate-900/80 p-6 shadow-xl shadow-slate-950/30">
              <SectionTitle
                eyebrow="1 · Authenticate"
                title="Login as a seeded tenant user"
                description="Choose one of the seeded worker/reviewer accounts and establish the tenant boundary for the rest of the workflow."
              />

              <div className="mt-5 flex flex-wrap gap-2">
                {Object.entries(loginPresets).map(([key, preset]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => setPreset(key as keyof typeof loginPresets)}
                    className="rounded-full border border-white/10 bg-slate-950 px-3 py-1.5 text-xs font-medium text-slate-200 transition hover:border-sky-400/40 hover:text-white"
                  >
                    {preset.tenantId} · {roleLabel(preset.role)}
                  </button>
                ))}
              </div>

              <form className="mt-5 space-y-4" onSubmit={handleLogin}>
                <label className="block space-y-2 text-sm text-slate-300">
                  <span>Tenant ID</span>
                  <input
                    value={loginForm.tenantId}
                    onChange={(event) => setLoginForm((current) => ({ ...current, tenantId: event.target.value }))}
                    className="w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-white outline-none placeholder:text-slate-500 focus:border-sky-400/50"
                  />
                </label>

                <label className="block space-y-2 text-sm text-slate-300">
                  <span>Email</span>
                  <input
                    type="email"
                    value={loginForm.email}
                    onChange={(event) => setLoginForm((current) => ({ ...current, email: event.target.value }))}
                    className="w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-white outline-none placeholder:text-slate-500 focus:border-sky-400/50"
                  />
                </label>

                <label className="block space-y-2 text-sm text-slate-300">
                  <span>Password</span>
                  <input
                    type="password"
                    value={loginForm.password}
                    onChange={(event) => setLoginForm((current) => ({ ...current, password: event.target.value }))}
                    className="w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-white outline-none placeholder:text-slate-500 focus:border-sky-400/50"
                  />
                </label>

                <button
                  type="submit"
                  disabled={authBusy}
                  className="inline-flex items-center justify-center rounded-2xl bg-sky-500 px-4 py-3 text-sm font-semibold text-slate-950 transition hover:bg-sky-400 disabled:cursor-not-allowed disabled:bg-slate-700 disabled:text-slate-300"
                >
                  {authBusy ? 'Signing in…' : 'Sign in'}
                </button>

                {authError ? <p className="text-sm text-rose-300">{authError}</p> : null}
                {auth ? (
                  <div className="rounded-2xl border border-emerald-400/20 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-100">
                    Signed in for <strong>{auth.tenantId}</strong> as <strong>{roleLabel(auth.role)}</strong>.
                  </div>
                ) : null}
              </form>
            </section>

            <section className="rounded-3xl border border-white/10 bg-slate-900/80 p-6 shadow-xl shadow-slate-950/30">
              <SectionTitle
                eyebrow="2 · Template catalog"
                title="Choose a template"
                description="All tenant-available templates are listed with preview and download options."
              />

              <div className="mt-5 rounded-2xl border border-white/10 bg-slate-950/70 p-4">
                {templatesError ? <p className="text-sm text-rose-300">{templatesError}</p> : null}

                {templatesBusy ? (
                  <p className="text-sm text-slate-400">Loading available templates…</p>
                ) : templates.length === 0 ? (
                  <p className="text-sm text-slate-400">No templates are available for this tenant.</p>
                ) : (
                  <div className="space-y-3">
                    {templates.map((template) => {
                      const isSelected = template.id === selectedTemplateId
                      return (
                        <div
                          key={template.id}
                          className={`rounded-2xl border p-4 transition ${
                            isSelected ? 'border-sky-400/50 bg-sky-500/10' : 'border-white/10 bg-slate-950/60'
                          }`}
                        >
                          <div className="flex flex-wrap items-center justify-between gap-3">
                            <div>
                              <p className="text-sm font-medium text-white">{template.name}</p>
                              <p className="text-xs text-slate-400">Template ID: {template.id}</p>
                              <p className="mt-2 text-xs text-slate-300">{template.fields.length} fields</p>
                            </div>
                            <div className="flex flex-wrap gap-2">
                              <button
                                type="button"
                                onClick={() => setSelectedTemplateId(template.id)}
                                className="rounded-full border border-white/10 bg-slate-950 px-3 py-1.5 text-xs font-medium text-slate-100 transition hover:border-sky-400/40 hover:text-white"
                              >
                                {isSelected ? 'Selected' : 'Select'}
                              </button>
                              <button
                                type="button"
                                onClick={() => void openTemplateBlank(template.id, false)}
                                className="rounded-full border border-white/10 bg-slate-950 px-3 py-1.5 text-xs font-medium text-slate-100 transition hover:border-sky-400/40 hover:text-white"
                              >
                                View blank
                              </button>
                              <button
                                type="button"
                                onClick={() => void openTemplateBlank(template.id, true)}
                                className="rounded-full border border-emerald-500/40 bg-emerald-500/10 px-3 py-1.5 text-xs font-medium text-emerald-100 transition hover:border-emerald-300 hover:text-emerald-50"
                              >
                                Download blank
                              </button>
                            </div>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                )}
              </div>

              <form className="mt-5 space-y-4" onSubmit={handleUpload}>
                <label className="block space-y-2 text-sm text-slate-300">
                  <span>Template</span>
                  <select
                    value={selectedTemplateId}
                    onChange={(event) => setSelectedTemplateId(event.target.value)}
                    disabled={!auth || templates.length === 0}
                    className="w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-white outline-none focus:border-sky-400/50 disabled:cursor-not-allowed disabled:bg-slate-900 disabled:text-slate-500"
                  >
                    {templates.map((template) => (
                      <option key={template.id} value={template.id}>
                        {template.name}
                      </option>
                    ))}
                  </select>
                </label>

                <label className="block space-y-2 text-sm text-slate-300">
                  <span>Handwritten form scan</span>
                  <input
                    type="file"
                    accept=".pdf,image/*"
                    onChange={(event) => setSelectedFile(event.target.files?.[0] ?? null)}
                    className="block w-full rounded-2xl border border-dashed border-white/15 bg-slate-950 px-4 py-3 text-sm text-slate-200 file:mr-4 file:rounded-full file:border-0 file:bg-sky-500 file:px-4 file:py-2 file:text-sm file:font-semibold file:text-slate-950 hover:file:bg-sky-400"
                  />
                </label>

                <button
                  type="submit"
                  disabled={!auth || !selectedFile || uploadBusy || templates.length === 0}
                  className="inline-flex items-center justify-center rounded-2xl bg-white px-4 py-3 text-sm font-semibold text-slate-950 transition hover:bg-slate-200 disabled:cursor-not-allowed disabled:bg-slate-700 disabled:text-slate-300"
                >
                  {uploadBusy ? 'Uploading…' : 'Upload intake'}
                </button>

                {uploadError ? <p className="text-sm text-rose-300">{uploadError}</p> : null}
              </form>

              <div className="mt-5 rounded-2xl border border-white/10 bg-slate-950/70 p-4">
                <div className="flex items-center justify-between gap-4">
                  <div>
                    <p className="text-sm font-medium text-white">Current intake status</p>
                    <p className="text-xs text-slate-400">Worker polling advances uploaded documents to review-ready.</p>
                  </div>
                  <span className="rounded-full border border-white/10 px-3 py-1 text-xs font-medium text-slate-200">
                    {intakeStatus ? statusLabel(intakeStatus.status) : 'No active intake'}
                  </span>
                </div>

                {activeIntakeId ? (
                  <div className="mt-4 space-y-3 text-sm text-slate-300">
                    <p>
                      Active intake: <code className="rounded bg-slate-900 px-2 py-1 text-sky-200">{activeIntakeId}</code>
                    </p>
                    {intakeStatus?.fields?.length ? (
                      <ul className="space-y-2">
                        {intakeStatus.fields.map((field) => (
                          <li key={field.fieldKey} className="flex items-center justify-between gap-3 rounded-xl bg-slate-900/80 px-3 py-2">
                            <span>{field.fieldKey}</span>
                            <span className={`rounded-full px-2 py-1 text-xs font-medium ${confidenceTone(field.confidence)}`}>
                              {(field.confidence * 100).toFixed(0)}%
                            </span>
                          </li>
                        ))}
                      </ul>
                    ) : (
                      <p className="text-slate-400">No extracted fields yet. Upload a document and wait for extraction.</p>
                    )}
                  </div>
                ) : (
                  <p className="mt-4 text-sm text-slate-400">No intake uploaded in this session yet.</p>
                )}
              </div>
            </section>
          </div>

          <div className="space-y-6">
            <section className="rounded-3xl border border-white/10 bg-slate-900/80 p-6 shadow-xl shadow-slate-950/30">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
                <SectionTitle
                  eyebrow="3 · Review queue"
                  title="Review-ready documents"
                  description="This queue is sourced from the same tenant-scoped backend data the worker writes after extraction."
                />
                <button
                  type="button"
                  disabled={!auth || queueBusy}
                  onClick={() => auth && refreshQueue(auth.accessToken)}
                  className="rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-sm font-medium text-slate-200 transition hover:border-sky-400/40 hover:text-white disabled:cursor-not-allowed disabled:text-slate-500"
                >
                  {queueBusy ? 'Refreshing…' : 'Refresh queue'}
                </button>
              </div>

              {queueError ? <p className="mt-4 text-sm text-rose-300">{queueError}</p> : null}

              <div className="mt-5 grid gap-3">
                {reviewQueue.length ? (
                  reviewQueue.map((item) => {
                    const selected = selectedReviewId === item.reviewId
                    return (
                      <button
                        key={item.reviewId}
                        type="button"
                        onClick={() => setSelectedReviewId(item.reviewId)}
                        className={`rounded-2xl border px-4 py-4 text-left transition ${
                          selected
                            ? 'border-sky-400/50 bg-sky-500/10'
                            : 'border-white/10 bg-slate-950/60 hover:border-white/20 hover:bg-slate-950'
                        }`}
                      >
                        <div className="flex items-center justify-between gap-4">
                          <div>
                            <p className="font-medium text-white">{item.applicantName}</p>
                            <p className="text-sm text-slate-400">{item.templateId}</p>
                          </div>
                          <span className="rounded-full bg-amber-500/15 px-3 py-1 text-xs font-medium text-amber-200 ring-1 ring-amber-500/30">
                            {item.uncertainFieldCount} uncertain
                          </span>
                        </div>
                      </button>
                    )
                  })
                ) : (
                  <div className="rounded-2xl border border-dashed border-white/10 bg-slate-950/60 p-6 text-sm text-slate-400">
                    No review-ready documents yet. Upload an intake and wait for the worker to finish extraction.
                  </div>
                )}
              </div>
            </section>

            <section className="rounded-3xl border border-white/10 bg-slate-900/80 p-6 shadow-xl shadow-slate-950/30">
              <SectionTitle
                eyebrow="4 · Review detail"
                title="Correct uncertain fields and finalize"
                description="Use the source document, confidence cues, and audit history to complete the human-in-the-loop step."
              />

              {reviewDetail ? (
                <div className="mt-5 space-y-6">
                  <div className="flex flex-col gap-3 rounded-2xl border border-white/10 bg-slate-950/60 p-4 lg:flex-row lg:items-center lg:justify-between">
                    <div className="space-y-1">
                      <p className="text-base font-semibold text-white">Review {reviewDetail.reviewId}</p>
                      <p className="text-sm text-slate-400">
                        Template {reviewDetail.templateId} · Status {statusLabel(reviewDetail.status)}
                      </p>
                    </div>
                    <a
                      href={reviewDetail.sourceDocumentUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex items-center justify-center rounded-2xl border border-white/10 bg-slate-900 px-4 py-3 text-sm font-medium text-slate-100 transition hover:border-sky-400/40 hover:text-white"
                    >
                      Open source document
                    </a>
                  </div>

                  <div className="grid gap-4">
                    {editableFields.map((field, index) => (
                      <div
                        key={field.fieldKey}
                        className={`rounded-2xl border p-4 ${
                          field.requiresReview ? 'border-amber-400/40 bg-amber-500/10' : 'border-white/10 bg-slate-950/60'
                        }`}
                      >
                        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                          <div>
                            <p className="text-sm font-medium text-white">{field.fieldKey}</p>
                            <p className="text-xs text-slate-400">Confidence-based extraction result</p>
                          </div>
                          <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${confidenceTone(field.confidence)}`}>
                            {(field.confidence * 100).toFixed(0)}% confidence
                          </span>
                        </div>
                        <textarea
                          value={field.value}
                          onChange={(event) => handleFieldChange(index, event.target.value)}
                          rows={field.value.length > 40 ? 3 : 2}
                          className="mt-4 w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-sm text-white outline-none placeholder:text-slate-500 focus:border-sky-400/50"
                        />
                      </div>
                    ))}
                  </div>

                  <div className="rounded-2xl border border-white/10 bg-slate-950/60 p-4">
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-sm font-medium text-white">Similar cases</p>
                      <span className="text-xs text-slate-400">Top {reviewSimilarCases.length}</span>
                    </div>
                    {reviewSimilarCases.length > 0 ? (
                      <div className="mt-3 space-y-3">
                        {reviewSimilarCases.map((item: SimilarCase) => (
                          <button
                            key={item.intakeId}
                            type="button"
                            onClick={() => setSelectedReviewId(item.reviewId)}
                            className="w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-left transition hover:border-sky-400/40 hover:bg-slate-950/80"
                          >
                            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                              <div>
                                <p className="text-sm font-medium text-white">{item.applicantName}</p>
                                <p className="text-xs text-slate-400">Template {item.templateId}</p>
                              </div>
                              <span className="rounded-full border border-sky-400/30 bg-sky-500/10 px-2.5 py-1 text-xs font-medium text-sky-200">
                                {(item.matchScore * 100).toFixed(0)}% match
                              </span>
                            </div>
                            <p className="mt-2 text-xs leading-relaxed text-slate-300">{item.summary}</p>
                          </button>
                        ))}
                      </div>
                    ) : (
                      <p className="mt-3 text-sm text-slate-400">No similar cases found for this document yet.</p>
                    )}
                  </div>

                  <label className="block space-y-2 text-sm text-slate-300">
                    <span>Reviewer note</span>
                    <textarea
                      value={reviewerNote}
                      onChange={(event) => setReviewerNote(event.target.value)}
                      rows={3}
                      className="w-full rounded-2xl border border-white/10 bg-slate-950 px-4 py-3 text-white outline-none placeholder:text-slate-500 focus:border-sky-400/50"
                      placeholder="Record the decision or correction rationale"
                    />
                  </label>

                  <div className="flex flex-wrap items-center gap-3">
                    <button
                      type="button"
                      disabled={!canReview || finalizeBusy}
                      onClick={handleFinalize}
                      className="rounded-2xl bg-emerald-400 px-4 py-3 text-sm font-semibold text-slate-950 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-slate-700 disabled:text-slate-300"
                    >
                      {finalizeBusy ? 'Finalizing…' : 'Finalize review'}
                    </button>
                    {finalizeError ? <p className="text-sm text-rose-300">{finalizeError}</p> : null}
                  </div>

                  <div className="rounded-2xl border border-white/10 bg-slate-950/60 p-4">
                    <p className="text-sm font-medium text-white">Audit trail</p>
                    <ul className="mt-3 space-y-2 text-sm text-slate-300">
                      {reviewDetail.auditEvents.map((eventName) => (
                        <li key={eventName} className="rounded-xl bg-slate-900/90 px-3 py-2">
                          {eventName}
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              ) : (
                <div className="mt-5 rounded-2xl border border-dashed border-white/10 bg-slate-950/60 p-8 text-sm text-slate-400">
                  Sign in and choose a review-ready document to inspect extracted fields and finalize the record.
                </div>
              )}
            </section>
          </div>
        </section>
      </div>
    </main>
  )
}
