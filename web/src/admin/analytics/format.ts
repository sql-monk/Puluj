// Formatting helpers of the analytics panel. Kept local on purpose: the shared admin helpers (admin/shared.tsx) are
// being written in parallel; merging the duplicates is a separate step (docs/plan-admin-ops.md §2.5).

const THIN_NBSP = ' '

export function fmtTime(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

/** `2026-09-15` → `15.09`. */
export function fmtDay(day: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(day)
  return m ? `${m[3]}.${m[2]}` : day
}

export function fmtHour(h: number): string {
  return `${String(h).padStart(2, '0')}:00`
}

/** Integer with thin non-breaking spaces between thousands: 1234567 → `1 234 567`. */
export function fmtInt(n: number): string {
  const sign = n < 0 ? '−' : ''
  const digits = String(Math.round(Math.abs(n)))
  return sign + digits.replace(/\B(?=(\d{3})+(?!\d))/g, THIN_NBSP)
}

export function ago(iso?: string | null, now: number = Date.now()): string {
  if (!iso) return '—'
  const s = Math.max(0, (now - new Date(iso).getTime()) / 1000)
  if (s < 90) return `${Math.round(s)} с тому`
  if (s < 5400) return `${Math.round(s / 60)} хв тому`
  if (s < 172800) return `${(s / 3600).toFixed(1)} год тому`
  return `${Math.round(s / 86400)} д тому`
}

/** A copy delay in seconds: `45 с`, `12 хв`, `1.5 год`, `2.3 д`. */
export function fmtDelay(s?: number | null): string {
  if (s === undefined || s === null || Number.isNaN(s)) return '—'
  if (s < 90) return `${Math.round(s)} с`
  if (s < 5400) return `${Math.round(s / 60)} хв`
  if (s < 172800) return `${(s / 3600).toFixed(1)} год`
  return `${(s / 86400).toFixed(1)} д`
}

/** A duration in milliseconds: `850 мс`, `12.5 с`, `18 хв`, `2.1 год`. */
export function fmtDurationMs(ms: number): string {
  if (ms < 0 || Number.isNaN(ms)) return '—'
  if (ms < 1000) return `${Math.round(ms)} мс`
  if (ms < 90_000) return `${(ms / 1000).toFixed(1)} с`
  if (ms < 5_400_000) return `${Math.round(ms / 60_000)} хв`
  return `${(ms / 3_600_000).toFixed(1)} год`
}

/** Elapsed between two instants (`to` missing = still running, measured against `now`). */
export function fmtDuration(from: string, to?: string | null, now: number = Date.now()): string {
  return fmtDurationMs((to ? new Date(to).getTime() : now) - new Date(from).getTime())
}

/** Uptime since `startedAt`: `4 хв`, `3 год 12 хв`, `2 д 5 год`. */
export function uptime(startedAt?: string | null, now: number = Date.now()): string {
  if (!startedAt) return '—'
  const s = Math.max(0, Math.floor((now - new Date(startedAt).getTime()) / 1000))
  const d = Math.floor(s / 86400)
  const h = Math.floor((s % 86400) / 3600)
  const m = Math.floor((s % 3600) / 60)
  if (d > 0) return `${d} д ${h} год`
  if (h > 0) return `${h} год ${m} хв`
  return `${m} хв`
}

export function fmtBytes(b?: number | null): string {
  if (b === undefined || b === null) return '—'
  if (b < 1024) return `${b} Б`
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(0)} КБ`
  if (b < 1024 * 1024 * 1024) return `${(b / 1024 / 1024).toFixed(1)} МБ`
  return `${(b / 1024 / 1024 / 1024).toFixed(2)} ГБ`
}

export function pct(v?: number | null, digits = 0): string {
  if (v === undefined || v === null || Number.isNaN(v)) return '—'
  return `${(v * 100).toFixed(digits)}%`
}

/** Throughput of a run: messages per second over its duration; `—` before the first second. */
export function fmtRate(count: number, ms: number): string {
  if (!(ms >= 1000) || count < 0) return '—'
  const perSec = count / (ms / 1000)
  return `${perSec >= 100 ? Math.round(perSec) : perSec.toFixed(1)}/с`
}

/** Label of a delay bucket [from, to) in seconds: `< 1 хв`, `1–5 хв`, `1–6 год`, `> 6 год`. */
export function fmtBucketLabel(fromSeconds: number, toSeconds?: number | null): string {
  const unit = (s: number) => (s >= 3600 ? `${Math.round(s / 3600)} год` : `${Math.round(s / 60)} хв`)
  const num = (s: number) => (s >= 3600 ? String(Math.round(s / 3600)) : String(Math.round(s / 60)))
  if (toSeconds === undefined || toSeconds === null) return `> ${unit(fromSeconds)}`
  if (fromSeconds <= 0) return `< ${unit(toSeconds)}`
  const sameUnit = (fromSeconds >= 3600) === (toSeconds >= 3600)
  return sameUnit ? `${num(fromSeconds)}–${unit(toSeconds)}` : `${unit(fromSeconds)}–${unit(toSeconds)}`
}

export const KIND_LABEL: Record<string, string> = { near: 'схоже', verbatim: 'дослівно', forward: 'пересилання' }

export function kindLabel(kind: string): string {
  return KIND_LABEL[kind] ?? kind
}
