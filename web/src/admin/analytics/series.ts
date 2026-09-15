// Pure data preparation of the analytics charts: delay buckets (the same edges as the backend's PairDelays), daily
// series over sources, axis scaling.
import type { DelayBucketDto, SourceAnalyticsDto, SourceDayDto } from '../../api/analytics'

/** Upper edges of the delay buckets, seconds: under a minute, 1–5, 5–15, 15–60 min, 1–6 h, over 6 h. */
export const DELAY_EDGES = [60, 300, 900, 3600, 21600]

export function bucketIndex(seconds: number): number {
  let i = 0
  while (i < DELAY_EDGES.length && seconds >= DELAY_EDGES[i]) i++
  return i
}

/** Client-side counterpart of the backend histogram, for sets the page already holds (e.g. the loaded findings). */
export function bucketDelays(seconds: number[]): DelayBucketDto[] {
  const counts = new Array<number>(DELAY_EDGES.length + 1).fill(0)
  for (const s of seconds) counts[bucketIndex(s)]++
  return counts.map((count, i) => ({ fromSeconds: i === 0 ? 0 : DELAY_EDGES[i - 1], toSeconds: i === DELAY_EDGES.length ? null : DELAY_EDGES[i], count }))
}

export interface Bar {
  count: number
  /** Share of the total, 0..1 (0 when the total is 0). */
  share: number
  /** Height relative to the tallest bar, 0..1. */
  height: number
}

export function histogramBars(buckets: DelayBucketDto[]): Bar[] {
  const total = buckets.reduce((n, b) => n + b.count, 0)
  const max = Math.max(...buckets.map((b) => b.count), 0)
  return buckets.map((b) => ({ count: b.count, share: total ? b.count / total : 0, height: max ? b.count / max : 0 }))
}

/** Sum of one per-day field over sources, aligned with the report's `days`. */
export function sumPerDay(sources: SourceAnalyticsDto[], key: keyof Omit<SourceDayDto, 'day'>, days: number): number[] {
  const out = new Array<number>(days).fill(0)
  for (const s of sources) s.perDay.forEach((d, i) => (i < days ? (out[i] += d[key]) : undefined))
  return out
}

/** The smallest "nice" number (1, 2, 5 × 10ⁿ, plus 2.5 × 10ⁿ) at or above `max`; 1 for 0. */
export function niceMax(max: number): number {
  if (!(max > 0)) return 1
  const exp = Math.floor(Math.log10(max))
  const base = 10 ** exp
  for (const m of [1, 2, 2.5, 5, 10]) {
    if (m * base >= max) return m * base
  }
  return 10 * base
}

/** Axis ticks from 0 to niceMax(max), `count` intervals; integers whenever the top is an integer. */
export function ticks(max: number, count = 4): number[] {
  const top = niceMax(max)
  const out: number[] = []
  for (let i = 0; i <= count; i++) out.push(Math.round(((top * i) / count) * 100) / 100)
  return out
}

/** Copy-weighted mean of per-source medians — the same approximation the backend uses inside a source. */
export function weightedMedianDelay(sources: SourceAnalyticsDto[]): number | null {
  let sum = 0
  let weight = 0
  for (const s of sources) {
    if (s.medianCopyDelaySeconds !== undefined && s.medianCopyDelaySeconds !== null && s.copies > 0) {
      sum += s.medianCopyDelaySeconds * s.copies
      weight += s.copies
    }
  }
  return weight ? sum / weight : null
}
