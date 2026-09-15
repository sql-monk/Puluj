/**
 * Chart colours (validated with the dataviz palette checker on the white, sepia, graphite and slate-950 surfaces).
 * Categories are keyed by code and always drawn in this order, so a filter never repaints a survivor; the hues echo
 * the map (drones green, missiles blue). One sequential blue ramp carries every magnitude (heatmaps); text never wears
 * a series colour.
 */
export const CATEGORY_ORDER = ['UAV', 'MISSILE', 'GUIDED_BOMB', 'AIRCRAFT', 'UNKNOWN'] as const

const LIGHT: Record<string, string> = { UAV: '#15803d', MISSILE: '#2563eb', GUIDED_BOMB: '#ea580c', AIRCRAFT: '#7c3aed', UNKNOWN: '#b45309' }
const DARK: Record<string, string> = { UAV: '#16a34a', MISSILE: '#3b82f6', GUIDED_BOMB: '#ea580c', AIRCRAFT: '#9085e9', UNKNOWN: '#d97706' }

export function categoryColor(code: string, dark: boolean): string {
  return (dark ? DARK : LIGHT)[code] ?? (dark ? DARK.UNKNOWN : LIGHT.UNKNOWN)
}

/** The single hue for one-series bars and columns. */
export function accent(dark: boolean): string {
  return dark ? '#3b82f6' : '#2a78d6'
}

/** De-emphasis grey for sparklines and context marks. */
export function muted(dark: boolean): string {
  return dark ? '#64748b' : '#94a3b8'
}

// Sequential blue, light → dark (dataviz reference ramp, steps 100…700).
const RAMP = ['#cde2fb', '#b7d3f6', '#9ec5f4', '#86b6ef', '#6da7ec', '#5598e7', '#3987e5', '#2a78d6', '#256abf', '#1c5cab', '#184f95', '#104281', '#0d366b']

/** Colour for a magnitude 0…max. On dark surfaces the ramp runs the other way so that "more" is brighter; zero stays transparent. */
export function seqColor(v: number, max: number, dark: boolean): string {
  if (v <= 0 || max <= 0) return 'transparent'
  const t = Math.min(1, Math.sqrt(v / max))
  const i = Math.min(RAMP.length - 1, Math.round(t * (RAMP.length - 1)))
  return dark ? RAMP[RAMP.length - 1 - i] : RAMP[i]
}

/** Ink for a label placed inside a filled cell: by the fill's step, not by guesswork. */
export function seqInk(v: number, max: number, dark: boolean): string {
  if (v <= 0 || max <= 0) return 'currentColor'
  const t = Math.min(1, Math.sqrt(v / max))
  const deep = dark ? t < 0.5 : t > 0.5
  return deep ? '#ffffff' : '#0b0b0b'
}
