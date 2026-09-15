import type { StatsBucketUnit, StatsRouteDto } from '../api/types'

export type Preset = '24h' | '7d' | '30d' | 'custom'

export const PRESETS: { id: Preset; label: string; hours?: number }[] = [
  { id: '24h', label: '24 год', hours: 24 },
  { id: '7d', label: '7 днів', hours: 24 * 7 },
  { id: '30d', label: '30 днів', hours: 24 * 30 },
  { id: 'custom', label: 'Довільний' },
]

export interface Period {
  preset: Preset
  from: Date
  to: Date
}

const FIVE_MIN = 5 * 60_000

/** `now` rounded down to 5 minutes: every viewer of a preset asks the server for the same period, so one cache entry serves them all. */
export function roundedNow(now = new Date()): Date {
  return new Date(Math.floor(now.getTime() / FIVE_MIN) * FIVE_MIN)
}

export function presetPeriod(preset: Exclude<Preset, 'custom'>, now = new Date()): Period {
  const to = roundedNow(now)
  const hours = PRESETS.find((p) => p.id === preset)?.hours ?? 24
  return { preset, from: new Date(to.getTime() - hours * 3600_000), to }
}

/**
 * The period lives in the hash so a view can be linked: `#/stats` (24 h), `#/stats?p=7d`, `#/stats?from=…&to=…`.
 * Anything unreadable falls back to 24 h.
 */
export function parsePeriodHash(hash: string, now = new Date()): Period {
  const q = hash.indexOf('?')
  const params = new URLSearchParams(q >= 0 ? hash.slice(q + 1) : '')
  const p = params.get('p')
  if (p === '7d' || p === '30d') return presetPeriod(p, now)
  const from = params.get('from')
  const to = params.get('to')
  if (from && to) {
    const f = new Date(from)
    const t = new Date(to)
    if (!Number.isNaN(f.getTime()) && !Number.isNaN(t.getTime()) && t > f) {
      return { preset: 'custom', from: f, to: t }
    }
  }
  return presetPeriod('24h', now)
}

export function periodHash(period: Period): string {
  if (period.preset === '24h') return '#/stats'
  if (period.preset === 'custom') return `#/stats?from=${encodeURIComponent(period.from.toISOString())}&to=${encodeURIComponent(period.to.toISOString())}`
  return `#/stats?p=${period.preset}`
}

/** Value for an <input type="datetime-local"> in the viewer's local time. */
export function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

const WEEKDAYS = ['пн', 'вт', 'ср', 'чт', 'пт', 'сб', 'нд']

/** Short axis label of a bucket start: the hour for hour buckets, the day for days, "day.month" for weeks. */
export function bucketLabel(iso: string, unit: StatsBucketUnit): string {
  const d = new Date(iso)
  if (unit === 'hour') return d.toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' })
  return d.toLocaleDateString('uk-UA', { day: '2-digit', month: '2-digit' })
}

/** Full label for a tooltip: the bucket start and, for hours, the date too. */
export function bucketTitle(iso: string, unit: StatsBucketUnit): string {
  const d = new Date(iso)
  if (unit === 'hour') return d.toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
  if (unit === 'week') return `тиждень з ${d.toLocaleDateString('uk-UA', { day: '2-digit', month: '2-digit' })}`
  return `${WEEKDAYS[(d.getDay() + 6) % 7]} ${d.toLocaleDateString('uk-UA', { day: '2-digit', month: '2-digit' })}`
}

/** How many axis labels fit: every k-th bucket gets one. */
export function labelEvery(count: number, maxLabels = 12): number {
  return Math.max(1, Math.ceil(count / maxLabels))
}

/** Compact figure: 1 284 / 12,9 тис. / 4,2 млн. */
export function compact(n: number): string {
  if (Math.abs(n) >= 1_000_000) return `${(n / 1_000_000).toLocaleString('uk-UA', { maximumFractionDigits: 1 })} млн`
  if (Math.abs(n) >= 10_000) return `${(n / 1000).toLocaleString('uk-UA', { maximumFractionDigits: 1 })} тис.`
  return n.toLocaleString('uk-UA')
}

export function hoursText(h: number): string {
  if (h < 1) return `${Math.round(h * 60)} хв`
  if (h < 48) return `${h.toLocaleString('uk-UA', { maximumFractionDigits: 1 })} год`
  let days = Math.floor(h / 24)
  let rest = Math.round(h % 24)
  if (rest === 24) {
    days += 1
    rest = 0
  }
  return `${days} д ${rest} год`
}

export function lagText(s?: number): string {
  if (s === undefined || s === null) return '—'
  return s < 90 ? `${Math.round(s)} с` : `${Math.round(s / 60)} хв`
}

export function pct(part: number, whole: number): string {
  return whole > 0 ? `${Math.round((part / whole) * 100)}%` : '—'
}

/** Clean axis ticks: 0 and a few round steps up to the maximum. */
export function niceTicks(max: number, count = 4): number[] {
  if (max <= 0) return [0]
  const raw = max / count
  const mag = 10 ** Math.floor(Math.log10(raw))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((s) => s >= raw) ?? raw
  // The last tick is the first step at or above the maximum, so every mark fits under the top gridline.
  const ticks: number[] = []
  for (let v = 0; ; v += step) {
    ticks.push(Math.round(v * 1000) / 1000)
    if (v >= max - 1e-9) break
  }
  return ticks
}

/** Top origins × top destinations (by their totals) as a matrix; the rest of the pairs live in the list and the table. */
export function routeMatrix(routes: StatsRouteDto[], top: number): { rows: { id: number; name: string }[]; cols: { id: number; name: string }[]; values: number[][] } {
  const fromTotals = new Map<number, { name: string; n: number }>()
  const toTotals = new Map<number, { name: string; n: number }>()
  for (const r of routes) {
    fromTotals.set(r.fromId, { name: r.fromName, n: (fromTotals.get(r.fromId)?.n ?? 0) + r.count })
    toTotals.set(r.toId, { name: r.toName, n: (toTotals.get(r.toId)?.n ?? 0) + r.count })
  }
  const pickTop = (m: Map<number, { name: string; n: number }>) =>
    [...m.entries()]
      .sort((a, b) => b[1].n - a[1].n || a[1].name.localeCompare(b[1].name, 'uk'))
      .slice(0, top)
      .map(([id, v]) => ({ id, name: v.name }))
  const rows = pickTop(fromTotals)
  const cols = pickTop(toTotals)
  const values = rows.map((r) => cols.map((c) => routes.find((x) => x.fromId === r.id && x.toId === c.id)?.count ?? 0))
  return { rows, cols, values }
}
