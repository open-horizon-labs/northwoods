import { useCallback, useEffect, useRef, useState } from 'react'

const STORAGE_KEY = 'nw:reviewPanelSplit'
const DEFAULT_RATIO = 0.55
const MIN_RATIO = 0.25
const MAX_RATIO = 0.75

function clamp(value: number): number {
  return Math.min(MAX_RATIO, Math.max(MIN_RATIO, value))
}

function loadRatio(): number {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored !== null) {
      const parsed = parseFloat(stored)
      if (Number.isFinite(parsed)) return clamp(parsed)
    }
  } catch {
    // localStorage unavailable (e.g. private browsing)
  }
  return DEFAULT_RATIO
}

function saveRatio(ratio: number): void {
  try {
    localStorage.setItem(STORAGE_KEY, ratio.toFixed(4))
  } catch {
    // ignore write failures
  }
}

export type ResizablePanelState = {
  /** Current left-panel ratio (0..1) */
  ratio: number
  /** Ref to attach to the drag handle element */
  handleRef: React.RefObject<HTMLDivElement | null>
  /** Whether the user is currently dragging */
  isDragging: boolean
}

/**
 * Hook that manages a resizable two-panel split.
 *
 * @param containerRef - ref to the container element whose width defines 100%
 * @param enabled - set to false to disable resizing (e.g. on narrow viewports)
 */
export function useResizablePanel(
  containerRef: React.RefObject<HTMLDivElement | null>,
  enabled: boolean,
): ResizablePanelState {
  const [ratio, setRatio] = useState(loadRatio)
  const [isDragging, setIsDragging] = useState(false)
  const handleRef = useRef<HTMLDivElement | null>(null)

  // Track starting position for drag
  const dragStart = useRef<{ startX: number; startRatio: number } | null>(null)

  const onPointerMove = useCallback(
    (e: PointerEvent) => {
      if (!dragStart.current || !containerRef.current) return
      const containerRect = containerRef.current.getBoundingClientRect()
      const containerWidth = containerRect.width
      if (containerWidth === 0) return

      const deltaX = e.clientX - dragStart.current.startX
      const deltaRatio = deltaX / containerWidth
      const newRatio = clamp(dragStart.current.startRatio + deltaRatio)
      setRatio(newRatio)
    },
    [containerRef],
  )

  const onPointerUp = useCallback(
    () => {
      dragStart.current = null
      setIsDragging(false)
      document.body.style.cursor = ''
      document.body.style.userSelect = ''

      // Persist final ratio
      setRatio((r) => {
        saveRatio(r)
        return r
      })
    },
    [],
  )

  // Attach/detach document-level listeners during drag
  useEffect(() => {
    if (!isDragging) return
    document.addEventListener('pointermove', onPointerMove)
    document.addEventListener('pointerup', onPointerUp)
    return () => {
      document.removeEventListener('pointermove', onPointerMove)
      document.removeEventListener('pointerup', onPointerUp)
    }
  }, [isDragging, onPointerMove, onPointerUp])

  // Handle pointer-down on the drag handle
  useEffect(() => {
    if (!enabled) return
    const handle = handleRef.current
    if (!handle) return

    const onPointerDown = (e: PointerEvent) => {
      e.preventDefault()
      dragStart.current = { startX: e.clientX, startRatio: ratio }
      setIsDragging(true)
      document.body.style.cursor = 'col-resize'
      document.body.style.userSelect = 'none'
    }

    handle.addEventListener('pointerdown', onPointerDown)
    return () => handle.removeEventListener('pointerdown', onPointerDown)
  }, [enabled, ratio])

  return { ratio, handleRef, isDragging }
}
