import { useCallback, useEffect, useState } from 'react'

import { api, userMessage } from '../api'
import type { DocumentListItem, LoginResponse } from '../types'
import { statusBadge } from '../types'

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

type Props = {
  auth: LoginResponse
  onLogout: () => void
}

export default function SubmissionsPage({ auth, onLogout }: Props) {
  const [documents, setDocuments] = useState<DocumentListItem[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [filterText, setFilterText] = useState('')
  const [copiedId, setCopiedId] = useState<string | null>(null)

  const loadDocuments = useCallback(async () => {
    setBusy(true)
    setError(null)
    try {
      const items = await api.getDocuments(auth.accessToken)
      setDocuments(items)
    } catch (err) {
      setError(userMessage(err, 'Unable to load documents. Please try again.'))
      setDocuments([])
    } finally {
      setBusy(false)
    }
  }, [auth.accessToken])

  useEffect(() => {
    void loadDocuments()
  }, [loadDocuments])

  const filtered = filterText.trim()
    ? documents.filter((d) =>
        `${d.applicantName} ${d.templateId} ${d.status} ${d.documentId}`.toLowerCase().includes(filterText.trim().toLowerCase()),
      )
    : documents

  const isReviewer = auth.role === 1 || auth.role === 'Reviewer'

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-slate-50">
      {/* Header */}
      <header className="shrink-0 border-b border-slate-200 bg-white">
        <div className="flex h-14 items-center justify-between px-4 sm:px-6">
          <div className="flex items-center gap-3">
            <p className="text-sm font-semibold text-slate-900">Northwoods</p>
          </div>
          <div className="flex items-center gap-4">
            <span className="hidden text-xs text-slate-500 sm:block">{auth.tenantId}</span>
            <button
              type="button"
              onClick={onLogout}
              className={`text-xs text-slate-600 underline underline-offset-2 hover:text-slate-900 ${FOCUS_RING}`}
            >
              Sign out
            </button>
          </div>
        </div>
        {/* Nav */}
        {isReviewer ? (
          <nav className="border-t border-slate-100 px-4 sm:px-6" aria-label="Reviewer navigation">
            <div className="flex gap-1">
              <a
                href="#"
                onClick={(e) => { e.preventDefault(); window.location.hash = ''; }}
                className={`border-b-2 border-transparent px-3 py-2 text-xs font-medium text-slate-500 hover:text-slate-700 ${FOCUS_RING}`}
              >
                Queue
              </a>
              <span
                className={`border-b-2 border-sky-700 px-3 py-2 text-xs font-semibold text-sky-900`}
                aria-current="page"
              >
                All Documents
              </span>
              <a
                href="#rag-report"
                onClick={(e) => { e.preventDefault(); window.location.hash = '#rag-report'; }}
                className={`border-b-2 border-transparent px-3 py-2 text-xs font-medium text-slate-500 hover:text-slate-700 ${FOCUS_RING}`}
              >
                RAG Report
              </a>
            </div>
          </nav>
        ) : null}
      </header>

      {/* Content */}
      <main className="flex-1 overflow-y-auto">
        <div className="mx-auto max-w-5xl px-4 py-6 sm:px-6">
          <div className="flex items-center justify-between gap-4">
            <div>
              <h1 className="text-lg font-semibold text-slate-900">All Documents</h1>
              <p className="mt-0.5 text-sm text-slate-500">
                All submissions for {auth.tenantId}
              </p>
            </div>
            <button
              type="button"
              onClick={() => void loadDocuments()}
              disabled={busy}
              className={`inline-flex items-center rounded border border-slate-300 bg-white px-3 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-60 ${FOCUS_RING}`}
            >
              {busy ? 'Loading…' : 'Refresh'}
            </button>
          </div>

          {/* Filter */}
          <div className="mt-4">
            <label htmlFor="doc-filter" className="sr-only">Filter documents</label>
            <input
              id="doc-filter"
              type="search"
              value={filterText}
              onChange={(e) => setFilterText(e.target.value)}
              placeholder="Filter by name, template, or status…"
              className={`w-full max-w-sm rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600 ${FOCUS_RING}`}
            />
          </div>

          {/* Error */}
          {error ? (
            <p role="alert" className="mt-4 rounded border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
              {error}
            </p>
          ) : null}

          {/* Loading */}
          {busy && documents.length === 0 ? (
            <p className="mt-6 text-sm text-slate-500" role="status" aria-live="polite">
              Loading documents…
            </p>
          ) : null}

          {/* Table */}
          {!busy && !error && filtered.length === 0 ? (
            <p className="mt-6 text-sm text-slate-500">
              {filterText ? 'No documents match your filter.' : 'No documents found.'}
            </p>
          ) : filtered.length > 0 ? (
            <>
              <p className="mt-3 text-xs text-slate-500" role="status" aria-live="polite">
                {filtered.length} document{filtered.length === 1 ? '' : 's'}
                {filterText ? ' matching filter' : ''}
              </p>
              <div className="mt-2 overflow-hidden rounded border border-slate-200 bg-white">
                <table className="w-full text-left text-sm">
                  <thead>
                    <tr className="border-b border-slate-200 bg-slate-50">
                      <th scope="col" className="px-4 py-2.5 text-xs font-semibold text-slate-600">ID</th>
                      <th scope="col" className="px-4 py-2.5 text-xs font-semibold text-slate-600">Applicant</th>
                      <th scope="col" className="px-4 py-2.5 text-xs font-semibold text-slate-600">Template</th>
                      <th scope="col" className="px-4 py-2.5 text-xs font-semibold text-slate-600">Form Date</th>
                      <th scope="col" className="px-4 py-2.5 text-xs font-semibold text-slate-600">Status</th>
                      <th scope="col" className="px-4 py-2.5 text-xs font-semibold text-slate-600">Uploaded</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {filtered.map((doc) => {
                      const badge = statusBadge(doc.status)
                      const uploadedDate = new Date(doc.createdAt)
                      const uploadedStr = uploadedDate.toLocaleDateString([], { year: 'numeric', month: 'short', day: 'numeric' })
                      const shortId = doc.documentId.slice(0, 8)
                      return (
                        <tr
                          key={doc.documentId}
                          className="cursor-pointer hover:bg-slate-50"
                          onClick={() => { window.location.hash = `#doc/${doc.documentId}` }}
                        >
                          <td className="px-4 py-3">
                            <button
                              type="button"
                              title={doc.documentId}
                              aria-label={copiedId === doc.documentId ? 'Copied!' : `Copy ID ${doc.documentId}`}
                              onClick={(e) => {
                                e.stopPropagation()
                                navigator.clipboard.writeText(doc.documentId).then(() => {
                                  setCopiedId(doc.documentId)
                                  setTimeout(() => setCopiedId(null), 1500)
                                }).catch(() => undefined)
                              }}
                              className="font-mono text-xs text-slate-500 hover:text-slate-800 focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-sky-700 rounded"
                            >
                              {copiedId === doc.documentId ? 'Copied!' : shortId}
                            </button>
                          </td>
                          <td className="px-4 py-3 font-medium text-slate-900">{doc.applicantName}</td>
                          <td className="px-4 py-3 text-slate-600">{doc.templateId}</td>
                          <td className="px-4 py-3 text-slate-500">{doc.documentDate ?? ''}</td>
                          <td className="px-4 py-3">
                            <span className={`inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium ${badge.badgeClass}`}>
                              {badge.label}
                            </span>
                          </td>
                          <td className="px-4 py-3 text-slate-500">{uploadedStr}</td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </>
          ) : null}
        </div>
      </main>
    </div>
  )
}
