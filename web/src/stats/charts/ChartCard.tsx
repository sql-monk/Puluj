import { useState, type ReactNode } from 'react'
import type { TipState } from './hooks'

/** A chart with its table twin: the SVG is the default view, "таблиця" swaps in the same numbers as a <table>. */
export interface TableSpec {
  head: string[]
  rows: (string | number)[][]
}

export default function ChartCard({ title, subtitle, legend, table, empty, children, className }: { title: string; subtitle?: string; legend?: ReactNode; table?: TableSpec; empty?: boolean; children: ReactNode; className?: string }) {
  const [showTable, setShowTable] = useState(false)
  return (
    <section className={`flex flex-col gap-2 rounded-xl bg-white p-3 shadow-sm dark:bg-slate-900 ${className ?? ''}`} aria-label={title}>
      <header className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <h3 className="text-sm font-semibold">{title}</h3>
        {subtitle && <span className="text-xs text-slate-500 dark:text-slate-400">{subtitle}</span>}
        {table && !empty && (
          <button type="button" className="ml-auto text-xs text-slate-500 underline-offset-2 hover:underline dark:text-slate-400" onClick={() => setShowTable((v) => !v)} aria-pressed={showTable}>
            {showTable ? 'графік' : 'таблиця'}
          </button>
        )}
      </header>
      {legend && !showTable && !empty && <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-slate-600 dark:text-slate-300">{legend}</div>}
      {empty ? (
        <div className="py-6 text-center text-xs text-slate-500 dark:text-slate-400">за період даних немає</div>
      ) : showTable && table ? (
        <div className="max-h-80 overflow-auto">
          <table className="w-full text-xs tabular-nums">
            <thead className="text-left text-slate-500 dark:text-slate-400">
              <tr>
                {table.head.map((h, i) => (
                  <th key={i} className={`py-1 pr-2 font-medium ${i > 0 ? 'text-right' : ''}`}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {table.rows.map((r, i) => (
                <tr key={i} className="border-t border-slate-100 dark:border-slate-800">
                  {r.map((c, j) => (
                    <td key={j} className={`py-1 pr-2 ${j > 0 ? 'text-right' : ''}`}>
                      {typeof c === 'number' ? c.toLocaleString('uk-UA') : c}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        children
      )}
    </section>
  )
}

/** A colour key beside a series name (a rect for bars/areas, a line for lines) — identity never rides on text colour. */
export function LegendItem({ color, label, line }: { color: string; label: string; line?: boolean }) {
  return (
    <span className="inline-flex items-center gap-1">
      <span aria-hidden className={line ? 'inline-block h-0.5 w-3' : 'inline-block h-2.5 w-2.5 rounded-sm'} style={{ background: color }} />
      {label}
    </span>
  )
}

export function Tooltip({ tip, width }: { tip: TipState | null; width: number }) {
  if (!tip) return null
  const flip = width > 0 && tip.x > width * 0.6
  return (
    <div
      role="status"
      className="pointer-events-none absolute z-10 min-w-28 rounded-md border border-slate-200 bg-white/95 px-2 py-1.5 text-xs shadow-lg dark:border-slate-700 dark:bg-slate-800/95"
      style={{ left: flip ? undefined : tip.x + 12, right: flip ? width - tip.x + 12 : undefined, top: Math.max(0, tip.y - 8) }}
    >
      {tip.title && <div className="mb-0.5 text-[11px] text-slate-500 dark:text-slate-400">{tip.title}</div>}
      {tip.lines.map((l, i) => (
        <div key={i} className="flex items-center gap-1.5 whitespace-nowrap">
          {l.color && <span aria-hidden className="inline-block h-0.5 w-2.5" style={{ background: l.color }} />}
          <span className="font-semibold tabular-nums">{l.value}</span>
          <span className="text-slate-500 dark:text-slate-400">{l.label}</span>
        </div>
      ))}
    </div>
  )
}
