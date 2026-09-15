import { seqColor, seqInk } from '../palette'
import { Tooltip } from './ChartCard'
import { useTooltip, useWidth } from './hooks'

/**
 * A grid of magnitudes on one sequential hue: rows × columns, every cell its own hover/focus target with a label
 * inside when it fits. Zero cells stay on the surface (a hairline outline only).
 */
export default function Heatmap({ rows, cols, values, dark, cellHeight = 20, labelWidth = 120, format = (v) => v.toLocaleString('uk-UA'), title, rotateCols = false }: { rows: string[]; cols: string[]; values: number[][]; dark: boolean; cellHeight?: number; labelWidth?: number; format?: (v: number) => string; title: (r: number, c: number) => string; /** Long column names: drawn at 40° in a taller header. */ rotateCols?: boolean }) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const { tip, show, hide } = useTooltip()
  const max = Math.max(0, ...values.flat())
  const padT = rotateCols ? 70 : 18
  // Rotated headers lean to the right past the last column: leave them room instead of clipping.
  const padR = rotateCols ? 36 : 0
  const cellW = cols.length > 0 ? Math.max(8, (width - labelWidth - padR) / cols.length) : 0
  const height = padT + rows.length * cellHeight + 2
  const colEvery = rotateCols ? 1 : Math.max(1, Math.ceil(36 / cellW))
  return (
    <div ref={ref} className="relative overflow-x-auto" data-chart>
      {width > 0 && (
        <svg width={Math.max(width, labelWidth + cellW * cols.length)} height={height} role="img" aria-label="теплова карта" className="text-[10px]">
          {cols.map((c, j) =>
            j % colEvery === 0 ? (
              rotateCols ? (
                <text key={c} transform={`translate(${labelWidth + cellW * j + cellW / 2 + 4},${padT - 6}) rotate(-40)`} fill="currentColor" fillOpacity={0.75} className="text-[11px]">
                  {c}
                </text>
              ) : (
                <text key={c} x={labelWidth + cellW * j + cellW / 2} y={12} textAnchor="middle" fill="currentColor" fillOpacity={0.65}>
                  {c}
                </text>
              )
            ) : null,
          )}
          {rows.map((r, i) => (
            <g key={r}>
              <text x={labelWidth - 6} y={padT + cellHeight * i + cellHeight / 2 + 3.5} textAnchor="end" fill="currentColor" fillOpacity={0.85} className="text-[11px]">
                {r}
              </text>
              {cols.map((_, j) => {
                const v = values[i]?.[j] ?? 0
                const x = labelWidth + cellW * j
                const y = padT + cellHeight * i
                return (
                  <g key={j}>
                    <rect x={x + 1} y={y + 1} width={cellW - 2} height={cellHeight - 2} rx={2} fill={seqColor(v, max, dark)} stroke="currentColor" strokeOpacity={v > 0 ? 0 : 0.08} tabIndex={v > 0 ? 0 : -1} onPointerMove={(e) => show(e, title(i, j), [{ value: format(v), label: '' }])} onFocus={(e) => show(e, title(i, j), [{ value: format(v), label: '' }])} onPointerLeave={hide} onBlur={hide} />
                    {v > 0 && cellW >= 30 && (
                      <text x={x + cellW / 2} y={y + cellHeight / 2 + 3.5} textAnchor="middle" fill={seqInk(v, max, dark)} className="pointer-events-none tabular-nums">
                        {format(v)}
                      </text>
                    )}
                  </g>
                )
              })}
            </g>
          ))}
        </svg>
      )}
      <Tooltip tip={tip} width={width} />
    </div>
  )
}
