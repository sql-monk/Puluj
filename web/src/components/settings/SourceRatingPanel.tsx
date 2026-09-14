import { useEffect, useMemo, useState } from 'react'
import { admin } from '../../api/admin'
import type { SourceRatingReportDto } from '../../api/types'
import { Badge, Section } from './fields'

const COLORS = ['#2563eb', '#dc2626', '#16a34a', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#4b5563', '#65a30d', '#9333ea']

function fmtDelay(s?: number): string {
  if (s === undefined || s === null) return '—'
  return s < 90 ? `${Math.round(s)} с` : `${Math.round(s / 60)} хв`
}

/**
 * Earned rating of the sources (not the operator's trust level): per day, 0.7 × originality (share of facts that were
 * not copies of another source) + 0.3 × influence (share of facts other sources repeated). With the history as a chart
 * and a table, who copies whom (counts and delays), and the groups of sources that feed on each other.
 */
export default function SourceRatingPanel() {
  const [days, setDays] = useState(14)
  const [report, setReport] = useState<SourceRatingReportDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    admin
      .sourceRating(days)
      .then((r) => {
        if (!cancelled) setReport(r)
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message)
      })
    return () => {
      cancelled = true
    }
  }, [days])

  const nameOf = useMemo(() => new Map((report?.sources ?? []).map((s) => [s.id, s.name])), [report])
  const active = useMemo(() => (report?.sources ?? []).filter((s) => s.days.some((d) => d.targets > 0)), [report])

  return (
    <>
      <Section
        title="Рейтинг джерел"
        badge={
          <span className="flex items-center gap-2">
            <select className="rounded border border-slate-300 bg-white px-1 py-0.5 text-xs dark:border-slate-600 dark:bg-slate-800" value={days} onChange={(e) => setDays(Number(e.target.value))}>
              {[7, 14, 30, 60].map((d) => (
                <option key={d} value={d}>
                  {d} днів
                </option>
              ))}
            </select>
            <Badge ok={null} text={`${active.length} активних`} />
          </span>
        }
      >
        <p className="text-xs text-slate-500">
          Рейтинг заробляється, а не задається: за добу 0.7 × оригінальність (частка фактів, які не були копією іншого джерела) + 0.3 × вплив (частка фактів, які потім повторили інші). Копія — це той самий факт, що вже був у іншого джерела за останні 3 хвилини. Довіра, яку ви задаєте джерелу вручну, тут не враховується.
        </p>
        {error && <div className="text-xs text-red-600">{error}</div>}
        {!report && !error && <div className="text-xs text-slate-500">Завантаження…</div>}
        {report && <RatingChart report={report} sources={active} />}
        {report && (
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead className="text-left text-slate-500">
                <tr>
                  <th className="py-1 pr-2">Джерело</th>
                  <th className="pr-2">Рейтинг</th>
                  <th className="pr-2">Довіра (ручна)</th>
                  <th className="pr-2">Фактів</th>
                  <th className="pr-2">Копій</th>
                  <th className="pr-2">Скопійовано іншими</th>
                  <th className="pr-2">Випередження</th>
                  <th className="pr-2">Група</th>
                </tr>
              </thead>
              <tbody>
                {[...active].sort((a, b) => (b.rating ?? -1) - (a.rating ?? -1)).map((s, i) => {
                  const targets = s.days.reduce((n, d) => n + d.targets, 0)
                  const copies = s.days.reduce((n, d) => n + d.copies, 0)
                  const copiedBy = s.days.reduce((n, d) => n + d.copiedBy, 0)
                  const leads = s.days.filter((d) => d.avgLeadSeconds !== undefined && d.copiedBy > 0)
                  const lead = leads.length ? leads.reduce((n, d) => n + d.avgLeadSeconds! * d.copiedBy, 0) / Math.max(1, leads.reduce((n, d) => n + d.copiedBy, 0)) : undefined
                  return (
                    <tr key={s.id} className="border-t border-slate-100 dark:border-slate-800">
                      <td className="py-1.5 pr-2">
                        <span className="mr-1 inline-block h-2.5 w-2.5 rounded-full" style={{ background: COLORS[active.indexOf(s) % COLORS.length] }} />
                        {s.name}
                        {i === 0 && s.rating !== undefined && <span className="ml-1 text-[10px] text-emerald-600">лідер</span>}
                      </td>
                      <td className="pr-2 font-mono">{s.rating !== undefined ? s.rating.toFixed(2) : '—'}</td>
                      <td className="pr-2 text-slate-400">{Math.round(s.trustLevel * 100)}%</td>
                      <td className="pr-2">{targets}</td>
                      <td className="pr-2">
                        {copies} <span className="text-slate-400">({targets ? Math.round((copies / targets) * 100) : 0}%)</span>
                      </td>
                      <td className="pr-2">{copiedBy}</td>
                      <td className="pr-2">{fmtDelay(lead)}</td>
                      <td className="pr-2">{s.group ? <span className="rounded bg-amber-100 px-1 text-amber-900 dark:bg-amber-900/40 dark:text-amber-100">група {s.group}</span> : '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Section>
      {report && (
        <Section title="Хто кого копіює" badge={<Badge ok={null} text={`${report.copies.length} пар`} />}>
          <p className="text-xs text-slate-500">
            Пара «копіювальник → оригінал» з кількістю повторених фактів і середньою затримкою. Джерела, між якими ≥ 3 копії і ≥ 30 % випуску копіювальника, утворюють групу.
          </p>
          {report.groups.length > 0 && (
            <div className="flex flex-wrap gap-2 text-xs">
              {report.groups.map((g, i) => (
                <span key={i} className="rounded bg-amber-100 px-2 py-0.5 text-amber-900 dark:bg-amber-900/40 dark:text-amber-100">
                  група {i + 1}: {g.map((id) => nameOf.get(id) ?? `#${id}`).join(' · ')}
                </span>
              ))}
            </div>
          )}
          <table className="w-full text-xs">
            <thead className="text-left text-slate-500">
              <tr>
                <th className="py-1 pr-2">Копіювальник</th>
                <th className="pr-2">Оригінал</th>
                <th className="pr-2">Копій</th>
                <th className="pr-2">Середня затримка</th>
              </tr>
            </thead>
            <tbody>
              {report.copies.slice(0, 40).map((c) => (
                <tr key={`${c.copierId}-${c.originalId}`} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1 pr-2">{nameOf.get(c.copierId) ?? `#${c.copierId}`}</td>
                  <td className="pr-2">{nameOf.get(c.originalId) ?? `#${c.originalId}`}</td>
                  <td className="pr-2">{c.count}</td>
                  <td className="pr-2">{fmtDelay(c.avgDelaySeconds)}</td>
                </tr>
              ))}
              {report.copies.length === 0 && (
                <tr>
                  <td colSpan={4} className="py-1 text-slate-500">
                    Копіювань за період не зафіксовано.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </Section>
      )}
    </>
  )
}

/** Rating per day, one line per source; days without facts leave a gap. */
function RatingChart({ report, sources }: { report: SourceRatingReportDto; sources: SourceRatingReportDto['sources'] }) {
  const W = 720
  const H = 200
  const padL = 34
  const padB = 22
  const padT = 8
  const n = report.days.length
  const x = (i: number) => padL + ((W - padL - 8) * i) / Math.max(1, n - 1)
  const y = (v: number) => padT + (H - padT - padB) * (1 - v)
  return (
    <div className="overflow-x-auto">
      <svg width={W} height={H} className="max-w-full text-[10px]" role="img" aria-label="Рейтинг джерел за днями">
        {[0, 0.25, 0.5, 0.75, 1].map((v) => (
          <g key={v}>
            <line x1={padL} x2={W - 8} y1={y(v)} y2={y(v)} stroke="currentColor" strokeOpacity={0.15} />
            <text x={padL - 4} y={y(v) + 3} textAnchor="end" fill="currentColor" fillOpacity={0.6}>
              {v.toFixed(2)}
            </text>
          </g>
        ))}
        {report.days.map((d, i) => (
          <text key={d} x={x(i)} y={H - 6} textAnchor="middle" fill="currentColor" fillOpacity={0.6}>
            {i % Math.max(1, Math.ceil(n / 10)) === 0 ? d.slice(5) : ''}
          </text>
        ))}
        {sources.map((s, si) => {
          const color = COLORS[si % COLORS.length]
          let path = ''
          let pen = false
          s.days.forEach((d, i) => {
            if (d.rating === undefined || d.rating === null || d.targets === 0) {
              pen = false
              return
            }
            path += `${pen ? 'L' : 'M'}${x(i).toFixed(1)},${y(d.rating).toFixed(1)} `
            pen = true
          })
          return (
            <g key={s.id}>
              <path d={path} fill="none" stroke={color} strokeWidth={2} strokeLinejoin="round" />
              {s.days.map((d, i) =>
                d.rating !== undefined && d.rating !== null && d.targets > 0 ? <circle key={i} cx={x(i)} cy={y(d.rating)} r={2.5} fill={color}>
                  <title>{`${s.name} · ${d.day}: ${d.rating.toFixed(2)} (фактів ${d.targets}, копій ${d.copies}, скопійовано ${d.copiedBy})`}</title>
                </circle> : null,
              )}
            </g>
          )
        })}
      </svg>
    </div>
  )
}
