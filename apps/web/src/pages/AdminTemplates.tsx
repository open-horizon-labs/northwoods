import { useCallback, useEffect, useState } from 'react'

import { api } from '../api'
import type {
  CreateTemplateRequest,
  LoginResponse,
  TemplateDescriptor,
  TemplateField,
  UpdateTemplateRequest,
} from '../types'

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

const btn =
  'inline-flex min-h-11 items-center justify-center rounded border px-4 py-2.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-60'

const btnPrimary = `${btn} border-sky-700 bg-sky-700 text-white hover:bg-sky-800 ${FOCUS_RING}`
const btnSecondary = `${btn} border-slate-300 bg-white text-slate-700 hover:bg-slate-50 ${FOCUS_RING}`
const btnDanger = `${btn} border-rose-600 bg-rose-600 text-white hover:bg-rose-700 ${FOCUS_RING}`

const inputStyle =
  'w-full rounded border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600'
const inputInteractive = `${inputStyle} ${FOCUS_RING}`

// ---------------------------------------------------------------------------
// Field editor row
// ---------------------------------------------------------------------------

function FieldRow({
  field,
  index,
  onChange,
  onRemove,
}: {
  field: TemplateField
  index: number
  onChange: (index: number, updated: TemplateField) => void
  onRemove: (index: number) => void
}) {
  return (
    <div className="flex items-start gap-2 rounded border border-slate-200 bg-slate-50 p-3">
      <div className="flex-1">
        <label className="mb-1 block text-xs font-medium text-slate-600" htmlFor={`field-key-${index}`}>
          Key
        </label>
        <input
          id={`field-key-${index}`}
          type="text"
          className={inputInteractive}
          value={field.key}
          onChange={(e) => onChange(index, { ...field, key: e.target.value })}
          placeholder="fieldKey"
          required
        />
      </div>
      <div className="w-36">
        <label className="mb-1 block text-xs font-medium text-slate-600" htmlFor={`field-type-${index}`}>
          Type
        </label>
        <select
          id={`field-type-${index}`}
          className={inputInteractive}
          value={field.type}
          onChange={(e) => onChange(index, { ...field, type: e.target.value })}
        >
          <option value="string">Text</option>
          <option value="date">Date</option>
          <option value="integer">Number</option>
          <option value="decimal">Currency</option>
          <option value="array">List</option>
        </select>
      </div>
      <div className="flex flex-col items-center pt-5">
        <label className="flex items-center gap-1.5 text-xs text-slate-600">
          <input
            type="checkbox"
            checked={field.required}
            onChange={(e) => onChange(index, { ...field, required: e.target.checked })}
            className="h-4 w-4 rounded border-slate-300 text-sky-700"
          />
          Required
        </label>
      </div>
      <button
        type="button"
        className="mt-5 rounded p-1.5 text-slate-400 hover:bg-slate-200 hover:text-slate-600"
        onClick={() => onRemove(index)}
        aria-label={`Remove field ${field.key}`}
      >
        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
    </div>
  )
}

// ---------------------------------------------------------------------------
// Template editor (create / edit)
// ---------------------------------------------------------------------------

type EditorMode = { kind: 'create' } | { kind: 'edit'; template: TemplateDescriptor }

function TemplateEditor({
  mode,
  accessToken,
  onSaved,
  onCancel,
}: {
  mode: EditorMode
  accessToken: string
  onSaved: () => void
  onCancel: () => void
}) {
  const isCreate = mode.kind === 'create'
  const initial = isCreate ? null : mode.template

  const [id, setId] = useState(initial?.id ?? '')
  const [name, setName] = useState(initial?.name ?? '')
  const [fields, setFields] = useState<TemplateField[]>(initial?.fields ?? [{ key: '', type: 'string', required: true }])
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleFieldChange = useCallback((index: number, updated: TemplateField) => {
    setFields((prev) => prev.map((f, i) => (i === index ? updated : f)))
  }, [])

  const handleFieldRemove = useCallback((index: number) => {
    setFields((prev) => prev.filter((_, i) => i !== index))
  }, [])

  const addField = () => {
    setFields((prev) => [...prev, { key: '', type: 'string', required: false }])
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    const trimmedFields = fields.filter((f) => f.key.trim() !== '')
    if (trimmedFields.length === 0) {
      setError('At least one field is required.')
      return
    }

    setSaving(true)
    try {
      if (isCreate) {
        const payload: CreateTemplateRequest = { id: id.trim(), name: name.trim(), fields: trimmedFields }
        await api.createTemplate(accessToken, payload)
      } else {
        const payload: UpdateTemplateRequest = { name: name.trim(), fields: trimmedFields }
        await api.updateTemplate(accessToken, initial!.id, payload)
      }
      onSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-5">
      <h2 className="text-lg font-semibold text-slate-900">
        {isCreate ? 'Create template' : `Edit: ${initial?.name}`}
      </h2>

      {error && (
        <div className="rounded border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700" role="alert">
          {error}
        </div>
      )}

      {isCreate && (
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-700" htmlFor="template-id">
            Template ID
          </label>
          <input
            id="template-id"
            type="text"
            className={inputInteractive}
            value={id}
            onChange={(e) => setId(e.target.value)}
            placeholder="e.g. housing-stability"
            required
            pattern="[a-z0-9][a-z0-9-]*[a-z0-9]"
            title="Lowercase letters, numbers, and hyphens. Must start and end with a letter or number."
          />
          <p className="mt-1 text-xs text-slate-500">
            Lowercase letters, numbers, and hyphens. Cannot be changed after creation.
          </p>
        </div>
      )}

      <div>
        <label className="mb-1 block text-sm font-medium text-slate-700" htmlFor="template-name">
          Name
        </label>
        <input
          id="template-name"
          type="text"
          className={inputInteractive}
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="General Assistance Intake"
          required
        />
      </div>

      <fieldset>
        <legend className="mb-2 text-sm font-medium text-slate-700">Fields</legend>
        <div className="space-y-2">
          {fields.map((field, i) => (
            <FieldRow key={i} field={field} index={i} onChange={handleFieldChange} onRemove={handleFieldRemove} />
          ))}
        </div>
        <button type="button" className={`${btnSecondary} mt-3`} onClick={addField}>
          + Add field
        </button>
      </fieldset>

      <div className="flex gap-3 border-t border-slate-200 pt-4">
        <button type="submit" className={btnPrimary} disabled={saving}>
          {saving ? 'Saving\u2026' : isCreate ? 'Create template' : 'Save changes'}
        </button>
        <button type="button" className={btnSecondary} onClick={onCancel} disabled={saving}>
          Cancel
        </button>
      </div>
    </form>
  )
}

// ---------------------------------------------------------------------------
// Blank PDF uploader
// ---------------------------------------------------------------------------

function BlankPdfUploader({
  template,
  accessToken,
  onUploaded,
}: {
  template: TemplateDescriptor
  accessToken: string
  onUploaded: () => void
}) {
  const [uploading, setUploading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return

    if (file.type !== 'application/pdf') {
      setError('Only PDF files are accepted.')
      return
    }

    setUploading(true)
    setError(null)
    try {
      await api.uploadTemplateBlankPdf(accessToken, template.id, file)
      onUploaded()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed.')
    } finally {
      setUploading(false)
    }
  }

  return (
    <div className="mt-2">
      <label className="block text-xs font-medium text-slate-600">
        {template.blankPdfKey ? 'Replace blank PDF' : 'Upload blank PDF'}
      </label>
      {template.blankPdfKey && (
        <p className="mb-1 text-xs text-slate-500">Current: {template.blankPdfKey}</p>
      )}
      <input
        type="file"
        accept="application/pdf"
        onChange={handleFileChange}
        disabled={uploading}
        className="mt-1 text-sm text-slate-600"
      />
      {uploading && <p className="mt-1 text-xs text-slate-500">Uploading...</p>}
      {error && <p className="mt-1 text-xs text-rose-600">{error}</p>}
    </div>
  )
}

// ---------------------------------------------------------------------------
// Main admin templates page
// ---------------------------------------------------------------------------

type Props = { auth: LoginResponse; onLogout: () => void }

export default function AdminTemplates({ auth, onLogout }: Props) {
  const [templates, setTemplates] = useState<TemplateDescriptor[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editorMode, setEditorMode] = useState<EditorMode | null>(null)
  const [archiving, setArchiving] = useState<string | null>(null)

  const loadTemplates = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await api.getTemplatesIncludingArchived(auth.accessToken)
      setTemplates(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load templates.')
    } finally {
      setLoading(false)
    }
  }, [auth.accessToken])

  useEffect(() => {
    loadTemplates()
  }, [loadTemplates])

  const handleArchive = async (templateId: string) => {
    setArchiving(templateId)
    try {
      await api.archiveTemplate(auth.accessToken, templateId)
      await loadTemplates()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Archive failed.')
    } finally {
      setArchiving(null)
    }
  }

  const handleSaved = () => {
    setEditorMode(null)
    loadTemplates()
  }

  const activeTemplates = templates.filter((t) => !t.isArchived)
  const archivedTemplates = templates.filter((t) => t.isArchived)

  return (
    <div className="min-h-screen bg-slate-50">
      {/* Header */}
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between px-6 py-4">
          <div>
            <h1 className="text-lg font-semibold text-slate-900">Template Management</h1>
            <p className="text-sm text-slate-500">Manage intake templates for your organization</p>
          </div>
          <div className="flex items-center gap-4">
            <span className="text-sm text-slate-500">Admin</span>
            <button className={btnSecondary} onClick={onLogout}>
              Sign out
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-5xl px-6 py-8">
        {error && (
          <div className="mb-6 rounded border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700" role="alert">
            {error}
            <button className="ml-3 underline" onClick={() => setError(null)}>
              Dismiss
            </button>
          </div>
        )}

        {editorMode ? (
          <div className="rounded border border-slate-200 bg-white p-6">
            <TemplateEditor
              mode={editorMode}
              accessToken={auth.accessToken}
              onSaved={handleSaved}
              onCancel={() => setEditorMode(null)}
            />
          </div>
        ) : (
          <>
            <div className="mb-6 flex items-center justify-between">
              <h2 className="text-base font-semibold text-slate-800">
                Active templates ({activeTemplates.length})
              </h2>
              <button className={btnPrimary} onClick={() => setEditorMode({ kind: 'create' })}>
                + New template
              </button>
            </div>

            {loading ? (
              <p className="text-sm text-slate-500">Loading templates...</p>
            ) : activeTemplates.length === 0 ? (
              <p className="text-sm text-slate-500">No templates yet. Create one to get started.</p>
            ) : (
              <div className="space-y-3">
                {activeTemplates.map((t) => (
                  <div
                    key={t.id}
                    className="flex items-start justify-between rounded border border-slate-200 bg-white p-4"
                  >
                    <div className="min-w-0 flex-1">
                      <h3 className="text-sm font-semibold text-slate-900">{t.name}</h3>
                      <p className="mt-0.5 text-xs text-slate-500">
                        ID: <code className="rounded bg-slate-100 px-1">{t.id}</code> &middot;{' '}
                        {t.fields.length} field{t.fields.length !== 1 ? 's' : ''}
                        {t.blankPdfKey && (
                          <span className="ml-2 inline-flex items-center rounded-full border border-sky-200 bg-sky-50 px-2 py-0.5 text-xs text-sky-700">
                            PDF uploaded
                          </span>
                        )}
                      </p>
                      <div className="mt-2 flex flex-wrap gap-1">
                        {t.fields.map((f) => (
                          <span
                            key={f.key}
                            className="inline-flex items-center rounded border border-slate-200 bg-slate-50 px-2 py-0.5 text-xs text-slate-600"
                          >
                            {f.key}
                            {f.required && <span className="ml-0.5 text-rose-500">*</span>}
                            <span className="ml-1 text-slate-400">({f.type})</span>
                          </span>
                        ))}
                      </div>
                      <BlankPdfUploader
                        template={t}
                        accessToken={auth.accessToken}
                        onUploaded={loadTemplates}
                      />
                    </div>
                    <div className="ml-4 flex flex-shrink-0 gap-2">
                      <button
                        className={btnSecondary}
                        onClick={() => setEditorMode({ kind: 'edit', template: t })}
                      >
                        Edit
                      </button>
                      <button
                        className={btnDanger}
                        onClick={() => handleArchive(t.id)}
                        disabled={archiving === t.id}
                      >
                        {archiving === t.id ? 'Archiving\u2026' : 'Archive'}
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}

            {archivedTemplates.length > 0 && (
              <div className="mt-10">
                <h2 className="mb-3 text-base font-semibold text-slate-600">
                  Archived ({archivedTemplates.length})
                </h2>
                <div className="space-y-2">
                  {archivedTemplates.map((t) => (
                    <div
                      key={t.id}
                      className="flex items-center justify-between rounded border border-slate-200 bg-slate-100 p-3 opacity-70"
                    >
                      <div>
                        <span className="text-sm text-slate-600">{t.name}</span>
                        <span className="ml-2 text-xs text-slate-400">({t.id})</span>
                      </div>
                      <span className="rounded-full border border-slate-300 bg-slate-200 px-2 py-0.5 text-xs text-slate-500">
                        Archived
                      </span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </main>
    </div>
  )
}
