import { Tooltip } from './ChartCard'
import { useTooltip, useWidth } from './hooks'

export interface BarRow {
  key: string
  label: string
  value: number
  /** Text after the value at the bar end (e.g. "· 12 треків"). */
  hint?: string
  /** Extra tooltip lines. */
  details?: { value: string; label: string }[]
  /** A category key drawn as a thin swatch before the label (identity without recolouring the bar). */
  swatch?: string
  color?: string
}

/**
 * Horizontal bars, one hue: labels on the left, the value at the bar end (moved into the tooltip when the row is
 * too narrow). Rows are 22 px, bars 14 px thick with a 4 px rounded end; the hit target is the whole row.
 */
export default function Bars({ rows, color, format = (v) => v.toLocaleString('uk-UA'), labelWidth = 150 }: { rows: BarRow[]; color: string; format?: (v: number) => string; labelWidth?: number }) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const { tip, show, hide } = useTooltip()
  const rowH = 22
  const barH = 14
  const padR = 8
  const max = Math.max(0, ...rows.map((r) => r.value))
  const plotW = Math.max(0, width - labelWidth - padR)
  const height = rows.length * rowH + 4
  return (
    <div ref={ref} className="relative" data-chart>
      {width > 0 && (
        <svg width={width} height={height} role="img" aria-label="розподіл" className="text-[11px]">
          {rows.map((r, i) => {
            const w = max > 0 ? (plotW * r.value) / max : 0
            const y = 2 + i * rowH
            const text = format(r.value) + (r.hint ? ` ${r.hint}` : '')
            const textW = text.length * 6.2
            // The value sits after the bar end; when the bar fills the row it moves inside the end, in ink that clears the fill.
            const outside = w + 6 + textW < plotW
            const inside = !outside && textW + 12 < w
            return (
              <g key={r.key}>
                <rect x={0} y={y} width={width} height={rowH} fill="transparent" tabIndex={0} onPointerMove={(e) => show(e, r.label, [{ value: format(r.value), label: r.hint ?? '' }, ...(r.details ?? [])])} onFocus={(e) => show(e, r.label, [{ value: format(r.value), label: r.hint ?? '' }, ...(r.details ?? [])])} onPointerLeave={hide} onBlur={hide} />
                {r.swatch && <rect x={0} y={y + 5} width={3} height={barH - 2} rx={1} fill={r.swatch} />}
                <text x={r.swatch ? 7 : 0} y={y + rowH / 2 + 4} fill="currentColor" fillOpacity={0.85}>
                  {truncate(r.label, Math.floor(labelWidth / 6.3))}
                </text>
                <path d={barPath(labelWidth, y + (rowH - barH) / 2, w, barH, 4)} fill={r.color ?? color} />
                {outside && (
                  <text x={labelWidth + w + 5} y={y + rowH / 2 + 4} fill="currentColor" fillOpacity={0.75} className="tabular-nums">
                    {text}
                  </text>
                )}
                {inside && (
                  <text x={labelWidth + w - 5} y={y + rowH / 2 + 4} textAnchor="end" fill="#ffffff" className="pointer-events-none tabular-nums">
                    {text}
                  </text>
                )}
              </g>
            )
          })}
        </svg>
      )}
      <Tooltip tip={tip} width={width} />
    </div>
  )
}

/** A horizontal bar: square at the baseline (left), the data end rounded. */
function barPath(x: number, y: number, w: number, h: number, r: number): string {
  const rr = Math.min(r, h / 2, w)
  if (w <= 0) return ''
  if (rr <= 0) return `M${x},${y}h${w}v${h}h${-w}Z`
  return `M${x},${y}h${w - rr}a${rr},${rr} 0 0 1 ${rr},${rr}v${h - 2 * rr}a${rr},${rr} 0 0 1 ${-rr},${rr}h${-(w - rr)}Z`
}

function truncate(s: string, max: number): string {
  return s.length <= max ? s : s.slice(0, Math.max(1, max - 1)) + '…'
}
