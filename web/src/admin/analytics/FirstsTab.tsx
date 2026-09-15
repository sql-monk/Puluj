import { useMemo } from 'react'
import type { AnalyticsReportDto } from '../../api/analytics'
import { Badge, Section } from '../../components/settings/fields'
import { ShareBar } from './charts'
import { fmtDelay, fmtInt } from './format'

/** Who opens the pipeline's tracks first, per target category: the `track_firsts` table of the analytics schema. */
export function FirstsTab({ report }: { report: AnalyticsReportDto }) {
  const nameOf = useMemo(() => new Map(report.sources.map((s) => [s.id, s.name])), [report])
  const categories = useMemo(() => [...new Set(report.firsts.map((f) => f.categoryCode))].sort(), [report])
  const byKey = useMemo(() => new Map(report.firsts.map((f) => [`${f.sourceId}-${f.categoryCode}`, f])), [report])
  const totals = useMemo(() => {
    const m = new Map<string, number>()
    for (const f of report.firsts) m.set(f.categoryCode, (m.get(f.categoryCode) ?? 0) + f.firsts)
    return m
  }, [report])
  const rows = useMemo(
    () =>
      [...new Set(report.firsts.map((f) => f.sourceId))]
        .map((id) => ({ id, total: categories.reduce((n, c) => n + (byKey.get(`${id}-${c}`)?.firsts ?? 0), 0), participations: categories.reduce((n, c) => n + (byKey.get(`${id}-${c}`)?.participations ?? 0), 0) }))
        .sort((a, b) => b.total - a.total),
    [report, categories, byKey],
  )
  const allFirsts = rows.reduce((n, r) => n + r.total, 0)
  if (report.firsts.length === 0) {
    return (
      <Section title="Хто перший бачить цілі">
        <p className="text-xs text-slate-500">За період даних немає: таблиця першоджерел перераховується сервісом після кожного прогону за останні 30 днів (`Analytics:TrackFirstsDays`).</p>
      </Section>
    )
  }
  return (
    <Section title="Хто перший бачить цілі" badge={<Badge ok={null} text={`${categories.length} категорій · ${fmtInt(allFirsts)} треків`} />}>
      <p className="text-xs text-slate-500">
        За треками конвеєра: джерело, чиє повідомлення відкрило трек, — «перше». У клітинці — скільки треків категорії джерело відкрило і його частка серед усіх перших; нижче — на скільки воно в середньому відстає, коли першим було інше. «Участь» — у скількох треках джерело було взагалі.
      </p>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-left text-slate-500">
            <tr>
              <th className="py-1 pr-2">Джерело</th>
              <th className="pr-3">Усього перших</th>
              {categories.map((c) => (
                <th key={c} className="pr-3">
                  {c} <span className="font-normal text-slate-400">({fmtInt(totals.get(c) ?? 0)})</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map(({ id, total, participations }) => (
              <tr key={id} className="border-t border-slate-100 dark:border-slate-800 align-top">
                <td className="py-1.5 pr-2 whitespace-nowrap">{nameOf.get(id) ?? `#${id}`}</td>
                <td className="pr-3">
                  <ShareBar value={allFirsts ? total / allFirsts : 0} color="bg-sky-500" width="w-20" text={`${fmtInt(total)} · ${allFirsts ? Math.round((total / allFirsts) * 100) : 0}%`} />
                  <div className="text-[10px] text-slate-400">участь у {fmtInt(participations)}</div>
                </td>
                {categories.map((c) => {
                  const f = byKey.get(`${id}-${c}`)
                  const t = totals.get(c) ?? 0
                  return (
                    <td key={c} className="pr-3" title={f ? `участь у ${f.participations} треках` : undefined}>
                      {f ? (
                        <>
                          <ShareBar value={t ? f.firsts / t : 0} color="bg-emerald-500" width="w-14" text={`${fmtInt(f.firsts)} · ${t ? Math.round((f.firsts / t) * 100) : 0}%`} />
                          {f.avgLagSeconds !== undefined && f.avgLagSeconds !== null && <div className="text-[10px] text-slate-400">відстає на {fmtDelay(f.avgLagSeconds)}</div>}
                        </>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Section>
  )
}
