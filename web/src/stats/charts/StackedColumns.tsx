import type { StatsBucketUnit } from '../../api/types'
import { bucketLabel, bucketTitle, labelEvery, niceTicks } from '../period'
import { Tooltip } from './ChartCard'
import { useTooltip, useWidth } from './hooks'

/** A column segment: square at the bottom, the data-end rounded by `r` (only the topmost segment of a stack). */
function columnPath(x: number, y: number, w: number, h: number, r: number): string {
  const rr = Math.min(r, w / 2, h)
  if (rr <= 0) return `M${x},${y}h${w}v${h}h${-w}Z`
  return `M${x},${y + h}v${-(h - rr)}a${rr},${rr} 0 0 1 ${rr},${-rr}h${w - 2 * rr}a${rr},${rr} 0 0 1 ${rr},${rr}v${h - rr}Z`
}

export interface Series {
  key: string
  label: string
  color: string
  values: number[]
}

/**
 * Stacked columns over time buckets (one series = one category, fixed order and colour). Columns are capped at 24 px,
 * segments separated by a 2 px surface gap, the top of every column rounded; the whole column is one hover target
 * that reads every series at that bucket.
 */
export default function StackedColumns({ buckets, unit, series, height = 220, valueLabel, format = (v) => v.toLocaleString('uk-UA'), extra }: { buckets: string[]; unit: StatsBucketUnit; series: Series[]; height?: number; valueLabel: string; format?: (v: number) => string; /** More tooltip lines for a bucket (a second measure that is not drawn). */ extra?: (i: number) => { value: string; label: string }[] }) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const { tip, show, hide } = useTooltip()
  const padR = 8
  const padT = 8
  const padB = 24
  const n = buckets.length
  const totals = buckets.map((_, i) => series.reduce((s, x) => s + (x.values[i] ?? 0), 0))
  const max = Math.max(0, ...totals)
  const ticks = niceTicks(max)
  // The axis band grows with its longest tick label ("1 000", "75 год") instead of clipping it.
  const padL = Math.max(36, ...ticks.map((t) => format(t).length * 6 + 10))
  const top = ticks[ticks.length - 1] || 1
  const plotW = Math.max(0, width - padL - padR)
  const plotH = height - padT - padB
  const slot = n > 0 ? plotW / n : 0
  const bar = Math.min(24, Math.max(2, slot - 2))
  const y = (v: number) => padT + plotH * (1 - v / top)
  const every = labelEvery(n, Math.max(2, Math.floor(plotW / 56)))
  // One readout per bucket: every drawn series (zeros skipped), the total when there are several, then the extra measures.
  const lines = (i: number) => [
    ...series.map((s) => ({ value: format(s.values[i] ?? 0), label: s.label, color: s.color })).filter((_, j) => (series[j].values[i] ?? 0) > 0),
    ...(series.length > 1 ? [{ value: format(totals[i]), label: 'разом' }] : []),
    ...(extra?.(i) ?? []),
  ]
  return (
    <div ref={ref} className="relative" data-chart>
      {width > 0 && (
        <svg width={width} height={height} role="img" aria-label={`${valueLabel} за часом`} className="text-[10px]">
          {ticks.map((t) => (
            <g key={t}>
              <line x1={padL} x2={width - padR} y1={y(t)} y2={y(t)} stroke="currentColor" strokeOpacity={0.12} />
              <text x={padL - 4} y={y(t) + 3} textAnchor="end" fill="currentColor" fillOpacity={0.65}>
                {format(t)}
              </text>
            </g>
          ))}
          {buckets.map((b, i) => {
            const x = padL + slot * i + (slot - bar) / 2
            let acc = 0
            const segs = series.map((s) => {
              const v = s.values[i] ?? 0
              const y1 = y(acc + v)
              const y0 = y(acc)
              acc += v
              return { s, v, y0, y1 }
            })
            const label = i % every === 0
            const last = segs.findLastIndex((g) => g.v > 0)
            return (
              <g key={b}>
                {segs.map(({ s, v, y0, y1 }, j) =>
                  v > 0 ? (
                    <path key={s.key} d={columnPath(x, y1, bar, Math.max(0, y0 - y1), j === last ? 3 : 0)} fill={s.color} stroke="var(--color-white, #fff)" strokeWidth={1} className="dark:[stroke:var(--color-slate-900)]" />
                  ) : null,
                )}
                {/* Hit target: the whole slot, taller than the column, so a thin column is easy to reach. */}
                <rect
                  x={padL + slot * i}
                  y={padT}
                  width={slot}
                  height={plotH}
                  fill="transparent"
                  tabIndex={totals[i] > 0 ? 0 : -1}
                  onPointerMove={(e) => show(e, bucketTitle(b, unit), lines(i))}
                  onFocus={(e) => show(e, bucketTitle(b, unit), lines(i))}
                  onPointerLeave={hide}
                  onBlur={hide}
                />
                {label && (
                  <text x={padL + slot * i + slot / 2} y={height - 8} textAnchor="middle" fill="currentColor" fillOpacity={0.65}>
                    {bucketLabel(b, unit)}
                  </text>
                )}
              </g>
            )
          })}
          <line x1={padL} x2={width - padR} y1={y(0)} y2={y(0)} stroke="currentColor" strokeOpacity={0.3} />
        </svg>
      )}
      <Tooltip tip={tip} width={width} />
    </div>
  )
}
