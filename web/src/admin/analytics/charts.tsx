// Chart primitives of the analytics panel: own SVG/HTML, no libraries (bundle size, themes). Colours are plain hex
// that read on both the light and the dark background; grid and labels use slate classes that follow the theme.
import type { ReactNode } from 'react'
import type { DelayBucketDto } from '../../api/analytics'
import { fmtBucketLabel, fmtDay, fmtHour, fmtInt } from './format'
import { histogramBars, ticks } from './series'

export const COLORS = { posts: '#94a3b8', copies: '#d97706', copiedBy: '#2563eb', accent: '#0891b2' }

export interface Series {
  label: string
  values: number[]
  color: string
}

/**
 * Grouped columns per day with a y axis, gridlines and sparse day labels; every bar carries a native tooltip.
 * Days come from the report (`YYYY-MM-DD`), the series are aligned with them.
 */
export function Columns({ days, series, height = 120, legend = true, ariaLabel }: { days: string[]; series: Series[]; height?: number; legend?: boolean; ariaLabel: string }) {
  const w = 720
  const pad = { l: 36, r: 8, t: 8, b: 18 }
  const plotW = w - pad.l - pad.r
  const plotH = height - pad.t - pad.b
  const max = Math.max(0, ...series.flatMap((s) => s.values))
  const axis = ticks(max)
  const top = axis[axis.length - 1]
  const n = Math.max(1, days.length)
  const slot = plotW / n
  const barW = Math.max(1, (slot * 0.78) / Math.max(1, series.length))
  const y = (v: number) => pad.t + (1 - v / top) * plotH
  const labelEvery = Math.max(1, Math.ceil(n / 12))
  return (
    <div className="space-y-1">
      <svg viewBox={`0 0 ${w} ${height}`} className="w-full" role="img" aria-label={ariaLabel}>
        {axis.map((t) => (
          <g key={t}>
            <line x1={pad.l} x2={w - pad.r} y1={y(t)} y2={y(t)} className="stroke-slate-200 dark:stroke-slate-700" strokeWidth={1} />
            <text x={pad.l - 4} y={y(t) + 3} textAnchor="end" className="fill-slate-400" fontSize={9}>
              {fmtInt(t)}
            </text>
          </g>
        ))}
        {days.map((d, i) =>
          i % labelEvery === 0 || i === n - 1 ? (
            <text key={d} x={pad.l + slot * i + slot / 2} y={height - 4} textAnchor="middle" className="fill-slate-400" fontSize={9}>
              {fmtDay(d)}
            </text>
          ) : null,
        )}
        {days.map((d, i) =>
          series.map((s, k) => {
            const v = s.values[i] ?? 0
            const x = pad.l + slot * i + (slot - barW * series.length) / 2 + barW * k
            const h = v > 0 ? Math.max(1, (v / top) * plotH) : 0
            return (
              <rect key={`${d}-${k}`} x={x} y={pad.t + plotH - h} width={barW} height={h} fill={s.color} rx={1}>
                <title>{`${fmtDay(d)} · ${s.label}: ${fmtInt(v)}`}</title>
              </rect>
            )
          }),
        )}
      </svg>
      {legend && (
        <div className="flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-slate-500">
          {series.map((s) => (
            <span key={s.label} className="flex items-center gap-1">
              <span className="inline-block h-2 w-2 rounded-sm" style={{ background: s.color }} />
              {s.label}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

/** The delay histogram: one column per bucket, count on top, share below the label. */
export function Histogram({ buckets, color = COLORS.copies, height = 150 }: { buckets: DelayBucketDto[]; color?: string; height?: number }) {
  const w = 480
  const pad = { l: 8, r: 8, t: 16, b: 30 }
  const plotW = w - pad.l - pad.r
  const plotH = height - pad.t - pad.b
  const bars = histogramBars(buckets)
  const slot = plotW / Math.max(1, buckets.length)
  const barW = slot * 0.7
  const total = buckets.reduce((n, b) => n + b.count, 0)
  return (
    <svg viewBox={`0 0 ${w} ${height}`} className="w-full max-w-xl" role="img" aria-label="Розподіл затримок копіювання">
      <line x1={pad.l} x2={w - pad.r} y1={pad.t + plotH} y2={pad.t + plotH} className="stroke-slate-200 dark:stroke-slate-700" strokeWidth={1} />
      {buckets.map((b, i) => {
        const bar = bars[i]
        const h = bar.count > 0 ? Math.max(2, bar.height * plotH) : 0
        const x = pad.l + slot * i + (slot - barW) / 2
        return (
          <g key={i}>
            <rect x={x} y={pad.t + plotH - h} width={barW} height={h} fill={bar.count > 0 ? color : 'transparent'} rx={2}>
              <title>{`${fmtBucketLabel(b.fromSeconds, b.toSeconds)}: ${fmtInt(bar.count)} (${Math.round(bar.share * 100)}%)`}</title>
            </rect>
            <text x={x + barW / 2} y={pad.t + plotH - h - 3} textAnchor="middle" className="fill-slate-600 dark:fill-slate-300" fontSize={10}>
              {bar.count > 0 ? fmtInt(bar.count) : ''}
            </text>
            <text x={x + barW / 2} y={pad.t + plotH + 11} textAnchor="middle" className="fill-slate-500" fontSize={9}>
              {fmtBucketLabel(b.fromSeconds, b.toSeconds)}
            </text>
            <text x={x + barW / 2} y={pad.t + plotH + 22} textAnchor="middle" className="fill-slate-400" fontSize={9}>
              {total ? `${Math.round(bar.share * 100)}%` : ''}
            </text>
          </g>
        )
      })}
    </svg>
  )
}

/** Small inline bar strip (hours of day, days): HTML, one div per value, native tooltip. */
export function Bars({ values, color = 'bg-sky-500', height = 28, title }: { values: number[]; color?: string; height?: number; title?: (i: number, v: number) => string }) {
  const max = Math.max(1, ...values)
  return (
    <div className="flex items-end gap-px" style={{ height }} aria-hidden>
      {values.map((v, i) => (
        <div key={i} className={`w-1.5 flex-1 rounded-sm ${v > 0 ? color : 'bg-slate-200 dark:bg-slate-700'}`} style={{ height: `${Math.max(2, (v / max) * 100)}%` }} title={title ? title(i, v) : String(v)} />
      ))}
    </div>
  )
}

/** Posts per hour of the day (Kyiv), 24 bars with the hour in the tooltip. */
export function HourStrip({ values }: { values: number[] }) {
  return <Bars values={values} color="bg-sky-500" height={32} title={(i, v) => `${fmtHour(i)} — ${fmtInt(v)}`} />
}

/** A horizontal share bar with the value next to it. */
export function ShareBar({ value, color = 'bg-emerald-500', width = 'w-16', text }: { value?: number | null; color?: string; width?: string; text?: ReactNode }) {
  if (value === undefined || value === null || Number.isNaN(value)) return <span className="text-slate-400">—</span>
  const p = Math.max(0, Math.min(100, Math.round(value * 100)))
  return (
    <span className="inline-flex items-center gap-1">
      <span className={`inline-block h-2 ${width} overflow-hidden rounded bg-slate-200 dark:bg-slate-700`}>
        <span className={`block h-full ${color}`} style={{ width: `${p}%` }} />
      </span>
      <span className="font-mono">{text ?? `${p}%`}</span>
    </span>
  )
}

export function Stat({ label, value, title, hint }: { label: string; value: ReactNode; title?: string; hint?: ReactNode }) {
  return (
    <div className="rounded-lg bg-slate-50 px-3 py-2 dark:bg-slate-800/60" title={title}>
      <div className="text-[10px] uppercase tracking-wide text-slate-500">{label}</div>
      <div className="truncate font-mono">{value}</div>
      {hint && <div className="truncate text-[11px] text-slate-500">{hint}</div>}
    </div>
  )
}
