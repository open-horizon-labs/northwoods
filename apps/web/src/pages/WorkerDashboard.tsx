import { type ChangeEvent, type FormEvent, useCallback, useEffect, useRef, useState } from 'react'

import { api } from '../api'
import type {
  IntakeStatusResponse,
  LoginResponse,
  TemplateDescriptor,
} from '../types'
import { statusLabel } from '../types'

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

const btn =
  'inline-flex min-h-11 items-center justify-center rounded border px-4 py-2.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-60'

const btnPrimary = `${btn} border-sky-700 bg-sky-700 text-white hover:bg-sky-800 ${FOCUS_RING}`
const btnSecondary = `${btn} border-slate-300 bg-white text-slate-700 hover:bg-slate-50 ${FOCUS_RING}`

const inputStyle =
  'w-full rounded border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600'
const inputInteractive = `${inputStyle} ${FOCUS_RING}`

type StatusBadgeProps = { status: IntakeStatusResponse['status'] }

const statusConfig = {
  Uploaded: { label: 'Uploaded', className: 'border-slate-300 bg-slate-100 text-slate-700' },
  Extracting: { label: 'Extracting\u2026', className: 'border-blue-200 bg-blue-50 text-blue-700' },
  ReviewReady: { label: 'Review ready', className: 'border-amber-200 bg-amber-50 text-amber-700' },
  Completed: { label: 'Completed', className: 'border-teal-200 bg-teal-50 text-teal-700' },
  Finalized: { label: 'Finalized', className: 'border-emerald-300 bg-emerald-100 text-emerald-800' },
  Failed: { label: 'Failed', className: 'border-rose-200 bg-rose-50 text-rose-700' },
  0: { label: 'Uploaded', className: 'border-slate-300 bg-slate-100 text-slate-700' },
  1: { label: 'Extracting\u2026', className: 'border-blue-200 bg-blue-50 text-blue-700' },
  2: { label: 'Review ready', className: 'border-amber-200 bg-amber-50 text-amber-700' },
  3: { label: 'Completed', className: 'border-teal-200 bg-teal-50 text-teal-700' },
  4: { label: 'Finalized', className: 'border-emerald-300 bg-emerald-100 text-emerald-800' },
  5: { label: 'Failed', className: 'border-rose-200 bg-rose-50 text-rose-700' },
} as const

function StatusBadge({ status }: StatusBadgeProps) {
  const config = statusConfig[status] ?? {
    label: statusLabel(status),
    className: 'border-slate-300 bg-slate-100 text-slate-700',
  }
  return (
    <span className={`inline-flex items-center rounded-full border px-2.5 py-1 text-xs font-medium ${config.className}`}>
      {config.label}
    </span>
  )
}

type UploadRecord = {
  intakeId: string
  templateId: string
  fileName: string
  uploadedAt: Date
  status: IntakeStatusResponse['status']
}

function useIsTouchDevice() {
  const [isTouch, setIsTouch] = useState(() =>
    typeof window !== 'undefined' && window.matchMedia('(pointer: coarse)').matches,
  )

  useEffect(() => {
    const mql = window.matchMedia('(pointer: coarse)')
    const handler = (e: MediaQueryListEvent) => setIsTouch(e.matches)
    mql.addEventListener('change', handler)
    return () => mql.removeEventListener('change', handler)
  }, [])

  return isTouch
}

type Props = {
  auth: LoginResponse
  onLogout: () => void
}

export default function WorkerDashboard({ auth, onLogout }: Props) {
  const [templates, setTemplates] = useState<TemplateDescriptor[]>([])
  const [isLoadingTemplates, setIsLoadingTemplates] = useState(true)
  const [selectedTemplateId, setSelectedTemplateId] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [uploadBusy, setUploadBusy] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [templatesError, setTemplatesError] = useState<string | null>(null)

  const [uploads, setUploads] = useState<UploadRecord[]>([])
  const [activePolling, setActivePolling] = useState<Set<string>>(new Set())

  const fileInputRef = useRef<HTMLInputElement>(null)
  const cameraInputRef = useRef<HTMLInputElement>(null)
  const isTouch = useIsTouchDevice()

  const loadTemplates = useCallback(async () => {
    setIsLoadingTemplates(true)
    setTemplatesError(null)
    try {
      const list = await api.getTemplates(auth.accessToken)
      setTemplates(list)
      if (list.length > 0) {
        setSelectedTemplateId((current) => list.find((t) => t.id === current)?.id ?? list[0].id)
      } else {
        setSelectedTemplateId('')
      }
    } catch (err) {
      setTemplatesError(err instanceof Error ? err.message : 'Failed to load templates.')
      setSelectedTemplateId('')
    } finally {
      setIsLoadingTemplates(false)
    }
  }, [auth.accessToken])

  useEffect(() => {
    void loadTemplates()
  }, [loadTemplates])

  // Poll status for active uploads
  useEffect(() => {
    if (activePolling.size === 0) return

    const timer = window.setInterval(async () => {
      const stillPolling = new Set<string>()

      for (const intakeId of activePolling) {
        try {
          const status = await api.getIntake(auth.accessToken, intakeId)
          // Normalize to lowercase string for resilient comparison across numeric and string status forms.
          const sNorm =
            typeof status.status === 'number'
              ? status.status
              : status.status.toLowerCase().replace(/\s+/g, '')
          const done = sNorm === 2 || sNorm === 3 || sNorm === 4 || sNorm === 5 || sNorm === 'reviewready' || sNorm === 'completed' || sNorm === 'finalized' || sNorm === 'failed'

          setUploads((prev) =>
            prev.map((u) => (u.intakeId === intakeId ? { ...u, status: status.status } : u)),
          )

          if (!done) {
            stillPolling.add(intakeId)
          }
        } catch {
          // Treat transient errors as non-terminal; keep the ID in the polling set
          stillPolling.add(intakeId)
        }
      }

      // Functional updater merges with current set to avoid dropping IDs added concurrently
      setActivePolling((current) => {
        const next = new Set(current)
        for (const id of activePolling) {
          if (!stillPolling.has(id)) next.delete(id)
        }
        return next
      })
    }, 2500)

    return () => window.clearInterval(timer)
  }, [activePolling, auth.accessToken])

  const handleUpload = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!selectedFile || !selectedTemplateId) return

    setUploadBusy(true)
    setUploadError(null)

    try {
      const created = await api.createIntake(auth.accessToken, selectedTemplateId, selectedFile)
      const record: UploadRecord = {
        intakeId: created.intakeId,
        templateId: selectedTemplateId,
        fileName: selectedFile.name,
        uploadedAt: new Date(),
        status: created.status,
      }
      setUploads((prev) => [record, ...prev])
      setActivePolling((prev) => new Set([...prev, created.intakeId]))

      // Reset file inputs
      setSelectedFile(null)
      if (fileInputRef.current) fileInputRef.current.value = ''
      if (cameraInputRef.current) cameraInputRef.current.value = ''
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Upload failed. Please try again.')
    } finally {
      setUploadBusy(false)
    }
  }

  const templateName = (id: string) => templates.find((t) => t.id === id)?.name ?? id

  return (
    <div className="min-h-screen bg-slate-50">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:bg-slate-900 focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-white"
      >
        Skip to main content
      </a>

      {/* Nav */}
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex h-14 max-w-2xl items-center justify-between px-4 sm:px-6">
          <p className="text-sm font-semibold text-slate-900">Northwoods</p>
          <div className="flex items-center gap-4">
            <span className="hidden text-xs text-slate-500 sm:block">
              {auth.tenantId}
            </span>
            <button
              type="button"
              onClick={onLogout}
              className={`text-xs text-slate-600 underline underline-offset-2 hover:text-slate-900 ${FOCUS_RING}`}
            >
              Sign out
            </button>
          </div>
        </div>
      </header>

      <main id="main" className="mx-auto max-w-2xl px-4 py-8 sm:px-6">
        <h1 className="text-xl font-semibold text-slate-900">Submit an intake</h1>
        <p className="mt-1 text-sm text-slate-600">
          Select the form type, attach a scan, and submit. Processing happens automatically.
        </p>

        {/* Upload form */}
        <section
          className="mt-6 rounded-lg border border-slate-200 bg-white p-5 sm:p-6"
          aria-labelledby="upload-heading"
        >
          <h2 id="upload-heading" className="text-base font-semibold text-slate-900">
            New submission
          </h2>

          {templatesError ? (
            <p role="alert" className="mt-3 text-sm text-rose-700">
              {templatesError}
            </p>
          ) : null}

          <form onSubmit={handleUpload} noValidate className="mt-4 space-y-4">
            <div className="space-y-1.5">
              <label htmlFor="template-select" className="block text-sm font-medium text-slate-700">
                Form type
                <span className="sr-only"> (required)</span>
              </label>
              <select
                id="template-select"
                value={selectedTemplateId}
                onChange={(e) => setSelectedTemplateId(e.target.value)}
                disabled={isLoadingTemplates || templates.length === 0 || uploadBusy}
                required
                aria-required="true"
                className={`${inputInteractive} disabled:bg-slate-50 disabled:text-slate-500`}
              >
                {isLoadingTemplates ? (
                  <option value="" disabled>
                    Loading form types…
                  </option>
                ) : templatesError ? (
                  <option value="" disabled>
                    Unable to load form types
                  </option>
                ) : templates.length === 0 ? (
                  <option value="" disabled>
                    No form types available
                  </option>
                ) : null}
                {templates.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1.5">
              <p className="block text-sm font-medium text-slate-700" id="file-label">
                Scan or photo
                <span className="sr-only"> (required)</span>
              </p>

              {isTouch ? (
                <>
                  <div className="flex flex-col gap-2.5" role="group" aria-labelledby="file-label">
                    <button
                      type="button"
                      onClick={() => cameraInputRef.current?.click()}
                      disabled={uploadBusy}
                      aria-describedby="file-help"
                      className={`${btnPrimary} w-full`}
                    >
                      Take photo
                    </button>
                    <button
                      type="button"
                      onClick={() => fileInputRef.current?.click()}
                      disabled={uploadBusy}
                      aria-describedby="file-help"
                      className={`${btnSecondary} w-full`}
                    >
                      Upload file
                    </button>
                  </div>
                  <input
                    ref={cameraInputRef}
                    type="file"
                    accept="image/*"
                    capture="environment"
                    className="sr-only"
                    tabIndex={-1}
                    aria-hidden="true"
                    onChange={(e: ChangeEvent<HTMLInputElement>) => setSelectedFile(e.target.files?.[0] ?? null)}
                  />
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept=".pdf,image/*"
                    className="sr-only"
                    tabIndex={-1}
                    aria-hidden="true"
                    onChange={(e: ChangeEvent<HTMLInputElement>) => setSelectedFile(e.target.files?.[0] ?? null)}
                  />
                  {selectedFile ? (
                    <p className="mt-1 truncate text-sm text-slate-700">{selectedFile.name}</p>
                  ) : null}
                </>
              ) : (
                <input
                  ref={fileInputRef}
                  id="file-upload"
                  type="file"
                  accept=".pdf,image/*"
                  required
                  aria-required="true"
                  aria-describedby="file-help"
                  aria-labelledby="file-label"
                  onChange={(e: ChangeEvent<HTMLInputElement>) => setSelectedFile(e.target.files?.[0] ?? null)}
                  disabled={uploadBusy}
                  className={`${inputInteractive} border-dashed py-2 file:mr-3 file:rounded file:border-0 file:bg-sky-700 file:px-3 file:py-1.5 file:text-xs file:font-semibold file:text-white hover:file:bg-sky-800`}
                />
              )}

              <p id="file-help" className="text-xs text-slate-500">
                PDF or image (JPG, PNG). Max one file per submission.
              </p>
            </div>

            {uploadError ? (
              <p role="alert" aria-live="assertive" className="rounded border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
                {uploadError}
              </p>
            ) : null}

            <button
              type="submit"
              disabled={!selectedFile || !selectedTemplateId || uploadBusy}
              className={`${btnPrimary} w-full sm:w-auto`}
            >
              {uploadBusy ? 'Submitting\u2026' : 'Submit intake'}
            </button>
          </form>
        </section>

        {/* Uploads list */}
        {uploads.length > 0 ? (
          <section className="mt-8" aria-labelledby="uploads-heading">
            <h2 id="uploads-heading" className="text-base font-semibold text-slate-900">
              My submissions
            </h2>

            <ul className="mt-3 space-y-3" role="list" aria-live="polite">
              {uploads.map((upload) => (
                <li
                  key={upload.intakeId}
                  className="flex items-start justify-between gap-4 rounded-lg border border-slate-200 bg-white px-4 py-3"
                >
                  <div className="min-w-0 space-y-0.5">
                    <p className="truncate text-sm font-medium text-slate-900">{upload.fileName}</p>
                    <p className="text-xs text-slate-500">
                      {templateName(upload.templateId)} &middot;{' '}
                      {upload.uploadedAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </p>
                  </div>
                  <div className="shrink-0 pt-0.5">
                    <StatusBadge status={upload.status} />
                  </div>
                </li>
              ))}
            </ul>

            <p className="mt-3 text-xs text-slate-500">
              Status updates automatically. Documents in &ldquo;Review ready&rdquo; state are queued for a reviewer.
            </p>
          </section>
        ) : null}

        {/* Empty state */}
        {uploads.length === 0 ? (
          <div className="mt-8 rounded-lg border border-dashed border-slate-300 bg-slate-50 p-8 text-center">
            <p className="text-sm text-slate-600">No submissions yet this session.</p>
            <p className="mt-1 text-xs text-slate-500">
              Upload a completed intake form above to get started.
            </p>
          </div>
        ) : null}

        <p className="mt-8 text-xs text-slate-600">
          Need help? Contact your supervisor or system administrator.
        </p>
      </main>

      {/* Accessible bottom nav hint for dev access */}
      <div className="fixed bottom-4 right-4 hidden">
        <a href="#dev" className="text-xs text-slate-400">
          Dev
        </a>
      </div>
    </div>
  )
}
