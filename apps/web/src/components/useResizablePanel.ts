import { useCallback, useEffect, useRef, useState } from 'react'

type Orientation = 'horizontal' | 'vertical'

type Options = {
  storageKey: string
  defaultRatio?: number
  minRatio?: number
  maxRatio?: number
  orientation?: Orientation
}

const KEYBOARD_STEP = 0.05

function clampValue(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

function loadRatio(key: string, defaultRatio: number, min: number, max: number): number {
  try {
    const stored = localStorage.getItem(key)
    if (stored !== null) {
      const parsed = parseFloat(stored)
      if (Number.isFinite(parsed)) return clampValue(parsed, min, max)
    }
  } catch {
    // localStorage unavailable (e.g. private browsing)
  }
  return defaultRatio
}

function saveRatio(key: string, ratio: number): void {
  try {
    localStorage.setItem(key, ratio.toFixed(4))
  } catch {
    // ignore write failures
  }
}

export type ResizablePanelState = {
  /** Current first-panel ratio (0..1) */
  ratio: number
  /** Ref to attach to the drag handle element */
  handleRef: React.RefObject<HTMLDivElement | null>
  /** Whether the user is currently dragging */
  isDragging: boolean
}

/**
 * Hook that manages a resizable two-panel split.
 *
 * @param containerRef - ref to the container element whose width/height defines 100%
 * @param enabled - set to false to disable resizing (e.g. on narrow viewports)
 * @param options - configuration for storage key, default ratio, min/max, orientation
 */
export function useResizablePanel(
  containerRef: React.RefObject<HTMLDivElement | null>,
  enabled: boolean,
  options?: Options,
): ResizablePanelState {
  const {
    storageKey = 'nw:reviewPanelSplit',
    defaultRatio = 0.55,
    minRatio = 0.25,
    maxRatio = 0.75,
    orientation = 'horizontal',
  } = options ?? {}

  const clampedDefault = clampValue(defaultRatio, minRatio, maxRatio)

  const clamp = useCallback(
    (v: number) => clampValue(v, minRatio, maxRatio),
    [minRatio, maxRatio],
  )

  const [ratio, setRatio] = useState(() => loadRatio(storageKey, clampedDefault, minRatio, maxRatio))
  const [isDragging, setIsDragging] = useState(false)
  const handleRef = useRef<HTMLDivElement | null>(null)

  // Reload ratio when storage key or bounds change (defensive -- callers currently
  // pass static props, but this prevents stale ratios if they ever become dynamic).
  useEffect(() => {
    setRatio(loadRatio(storageKey, clampedDefault, minRatio, maxRatio))
  }, [storageKey, clampedDefault, minRatio, maxRatio])

  // Track starting position for drag
  const dragStart = useRef<{ startPos: number; startRatio: number } | null>(null)

  // Mirror ratio in a ref to avoid listener churn during drag
  const ratioRef = useRef(ratio)
  ratioRef.current = ratio

  const isVertical = orientation === 'vertical'
  const cursorStyle = isVertical ? 'row-resize' : 'col-resize'

  const onPointerMove = useCallback(
    (e: PointerEvent) => {
      if (!dragStart.current || !containerRef.current) return
      const containerRect = containerRef.current.getBoundingClientRect()
      const containerSize = isVertical ? containerRect.height : containerRect.width
      if (containerSize === 0) return

      const clientPos = isVertical ? e.clientY : e.clientX
      const deltaPos = clientPos - dragStart.current.startPos
      const deltaRatio = deltaPos / containerSize
      const newRatio = clamp(dragStart.current.startRatio + deltaRatio)
      setRatio(newRatio)
    },
    [containerRef, isVertical, clamp],
  )

  const onPointerUp = useCallback(
    () => {
      dragStart.current = null
      setIsDragging(false)
      document.body.style.cursor = ''
      document.body.style.userSelect = ''

      // Persist final ratio
      setRatio((r) => {
        saveRatio(storageKey, r)
        return r
      })
    },
    [storageKey],
  )

  // Reset styles on unmount to prevent stuck cursor/user-select
  useEffect(() => {
    return () => {
      document.body.style.cursor = ''
      document.body.style.userSelect = ''
    }
  }, [])

  // Attach/detach document-level listeners during drag
  useEffect(() => {
    if (!isDragging) return
    document.addEventListener('pointermove', onPointerMove)
    document.addEventListener('pointerup', onPointerUp)
    document.addEventListener('pointercancel', onPointerUp)
    return () => {
      document.removeEventListener('pointermove', onPointerMove)
      document.removeEventListener('pointerup', onPointerUp)
      document.removeEventListener('pointercancel', onPointerUp)
    }
  }, [isDragging, onPointerMove, onPointerUp])

  // Handle pointer-down and keyboard events on the drag handle
  useEffect(() => {
    if (!enabled) return
    const handle = handleRef.current
    if (!handle) return

    const onPointerDown = (e: PointerEvent) => {
      e.preventDefault()
      const startPos = isVertical ? e.clientY : e.clientX
      dragStart.current = { startPos, startRatio: ratioRef.current }
      setIsDragging(true)
      document.body.style.cursor = cursorStyle
      document.body.style.userSelect = 'none'
    }

    const decreaseKey = isVertical ? 'ArrowUp' : 'ArrowLeft'
    const increaseKey = isVertical ? 'ArrowDown' : 'ArrowRight'

    const onKeyDown = (e: KeyboardEvent) => {
      let newRatio: number | null = null
      switch (e.key) {
        case decreaseKey:
          newRatio = clamp(ratioRef.current - KEYBOARD_STEP)
          break
        case increaseKey:
          newRatio = clamp(ratioRef.current + KEYBOARD_STEP)
          break
        case 'Home':
          newRatio = minRatio
          break
        case 'End':
          newRatio = maxRatio
          break
      }
      if (newRatio !== null) {
        e.preventDefault()
        setRatio(newRatio)
        saveRatio(storageKey, newRatio)
      }
    }

    handle.addEventListener('pointerdown', onPointerDown)
    handle.addEventListener('keydown', onKeyDown)
    return () => {
      handle.removeEventListener('pointerdown', onPointerDown)
      handle.removeEventListener('keydown', onKeyDown)
    }
  }, [enabled, isVertical, cursorStyle, clamp, minRatio, maxRatio, storageKey])

  return { ratio, handleRef, isDragging }
}
