import { useEffect, useRef, useState } from 'react'
import { Document, Page, pdfjs } from 'react-pdf'

import { fetchDocumentSource } from '../api'

pdfjs.GlobalWorkerOptions.workerSrc = `https://unpkg.com/pdfjs-dist@${pdfjs.version}/build/pdf.worker.min.mjs`

const ZOOM_MIN = 0.5
const ZOOM_MAX = 3.0
const ZOOM_STEP = 0.25

interface PdfViewerProps {
  accessToken: string
  documentId: string
  /** Called once we know whether the source document is available. */
  onAvailabilityChange?: (available: boolean) => void
}

const btnCtrl =
  'inline-flex items-center justify-center rounded border border-slate-300 bg-white px-2.5 py-1 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-1'

export function PdfViewer({ accessToken, documentId, onAvailabilityChange }: PdfViewerProps) {
  const [pdfData, setPdfData] = useState<{ data: ArrayBuffer } | null>(null)
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [numPages, setNumPages] = useState<number>(0)
  const [pageNum, setPageNum] = useState(1)
  const [scale, setScale] = useState(1.0)
  const containerRef = useRef<HTMLDivElement>(null)
  const [containerWidth, setContainerWidth] = useState<number | undefined>(undefined)
  const onAvailabilityChangeRef = useRef(onAvailabilityChange)
  useEffect(() => { onAvailabilityChangeRef.current = onAvailabilityChange }, [onAvailabilityChange])

  // Measure container width for responsive page rendering
  useEffect(() => {
    const el = containerRef.current
    if (!el) return
    const obs = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width
      if (width) setContainerWidth(width)
    })
    obs.observe(el)
    return () => obs.disconnect()
  }, [])

  // Fetch document source when documentId/accessToken changes.
  useEffect(() => {
    let cancelled = false
    setPdfData(null)
    setImageUrl(null)
    setLoadError(false)
    setPageNum(1)

    fetchDocumentSource(accessToken, documentId)
      .then(({ buffer, contentType }) => {
        if (cancelled) return
        if (contentType.startsWith('image/')) {
          const blob = new Blob([buffer], { type: contentType })
          setImageUrl(URL.createObjectURL(blob))
          onAvailabilityChangeRef.current?.(true)
        } else {
          setPdfData({ data: buffer })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setLoadError(true)
          onAvailabilityChangeRef.current?.(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [accessToken, documentId])

  // Clean up object URL on unmount or change
  useEffect(() => {
    return () => {
      if (imageUrl) URL.revokeObjectURL(imageUrl)
    }
  }, [imageUrl])

  if (loadError) {
    return null
  }

  // Image viewer
  if (imageUrl) {
    return (
      <div className="flex flex-1 flex-col overflow-hidden">
        <div ref={containerRef} className="flex-1 overflow-auto bg-slate-100 p-2">
          <img
            src={imageUrl}
            alt="Uploaded intake document"
            className="mx-auto max-w-full"
            style={{ transform: `scale(${scale})`, transformOrigin: 'top center' }}
          />
        </div>
        <div className="flex shrink-0 items-center justify-center gap-2 border-t border-slate-200 bg-slate-50 px-3 py-1.5">
          <button
            type="button"
            className={btnCtrl}
            onClick={() => setScale((s) => Math.max(ZOOM_MIN, parseFloat((s - ZOOM_STEP).toFixed(2))))}
            disabled={scale <= ZOOM_MIN}
            aria-label="Zoom out"
          >
            &minus;
          </button>
          <span className="min-w-[3.5rem] text-center text-xs text-slate-600">
            {Math.round(scale * 100)}%
          </span>
          <button
            type="button"
            className={btnCtrl}
            onClick={() => setScale((s) => Math.min(ZOOM_MAX, parseFloat((s + ZOOM_STEP).toFixed(2))))}
            disabled={scale >= ZOOM_MAX}
            aria-label="Zoom in"
          >
            &#43;
          </button>
        </div>
      </div>
    )
  }

  if (!pdfData) {
    return (
      <div className="flex flex-1 items-center justify-center bg-slate-50">
        <p className="text-sm text-slate-500" role="status">
          Loading document…
        </p>
      </div>
    )
  }

  const pageWidth = containerWidth ? containerWidth * scale : undefined

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Controls bar */}
      <div className="flex shrink-0 items-center gap-2 border-b border-slate-200 bg-slate-50 px-3 py-1.5">
        {/* Page navigation */}
        <button
          type="button"
          className={btnCtrl}
          onClick={() => setPageNum((p) => Math.max(1, p - 1))}
          disabled={pageNum <= 1}
          aria-label="Previous page"
        >
          &lsaquo;
        </button>
        <span className="min-w-[5rem] text-center text-xs text-slate-600">
          {pageNum} / {numPages || '?'}
        </span>
        <button
          type="button"
          className={btnCtrl}
          onClick={() => setPageNum((p) => Math.min(numPages, p + 1))}
          disabled={pageNum >= numPages}
          aria-label="Next page"
        >
          &rsaquo;
        </button>

        <div className="mx-2 h-4 w-px bg-slate-300" aria-hidden />

        {/* Zoom controls */}
        <button
          type="button"
          className={btnCtrl}
          onClick={() => setScale((s) => Math.max(ZOOM_MIN, parseFloat((s - ZOOM_STEP).toFixed(2))))}
          disabled={scale <= ZOOM_MIN}
          aria-label="Zoom out"
        >
          &minus;
        </button>
        <span className="min-w-[3.5rem] text-center text-xs text-slate-600">
          {Math.round(scale * 100)}%
        </span>
        <button
          type="button"
          className={btnCtrl}
          onClick={() => setScale((s) => Math.min(ZOOM_MAX, parseFloat((s + ZOOM_STEP).toFixed(2))))}
          disabled={scale >= ZOOM_MAX}
          aria-label="Zoom in"
        >
          &#43;
        </button>
      </div>

      {/* Scrollable document area */}
      <div ref={containerRef} className="flex-1 overflow-auto bg-slate-100 p-2">
        <Document
          file={pdfData}
          onLoadSuccess={({ numPages: n }) => {
            setNumPages(n)
            setPageNum(1)
            onAvailabilityChangeRef.current?.(true)
          }}
          onLoadError={() => {
            setLoadError(true)
            onAvailabilityChangeRef.current?.(false)
          }}
          loading={
            <div className="flex items-center justify-center py-8">
              <p className="text-sm text-slate-500" role="status">
                Rendering…
              </p>
            </div>
          }
        >
          <Page
            pageNumber={pageNum}
            width={pageWidth}
            renderTextLayer={false}
            renderAnnotationLayer={false}
          />
        </Document>
      </div>
    </div>
  )
}
