import { useEffect, useRef, useState, type FocusEvent, type PointerEvent, type RefObject } from 'react'

/** Width of the chart's container, so every SVG is drawn at its real pixel size (no distorted text from a stretched viewBox). */
export function useWidth<T extends HTMLElement>(): [RefObject<T | null>, number] {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(0)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const ro = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect.width ?? 0
      setWidth(Math.floor(w))
    })
    ro.observe(el)
    setWidth(Math.floor(el.getBoundingClientRect().width))
    return () => ro.disconnect()
  }, [])
  return [ref, width]
}

export interface TipState {
  x: number
  y: number
  lines: { value: string; label: string; color?: string }[]
  title?: string
}

/**
 * One floating readout per chart, positioned inside the chart's container. Values lead, labels follow; every value in
 * it is also reachable through the table view, so the tooltip only enhances.
 */
export function useTooltip() {
  const [tip, setTip] = useState<TipState | null>(null)
  const show = (e: PointerEvent | FocusEvent, title: string | undefined, lines: TipState['lines']) => {
    const host = (e.currentTarget as Element).closest('[data-chart]') as HTMLElement | null
    const rect = host?.getBoundingClientRect()
    const target = (e.target as Element).getBoundingClientRect()
    const px = 'clientX' in e ? e.clientX : target.left + target.width / 2
    const py = 'clientY' in e ? e.clientY : target.top
    setTip({ x: px - (rect?.left ?? 0), y: py - (rect?.top ?? 0), title, lines })
  }
  const hide = () => setTip(null)
  return { tip, show, hide }
}

