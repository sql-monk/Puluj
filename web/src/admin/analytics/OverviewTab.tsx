import { useMemo } from 'react'
import type { AnalyticsReportDto, CopyPairDto, SourceAnalyticsDto } from '../../api/analytics'
import { Badge, Section } from '../../components/settings/fields'
import { COLORS, Columns, Stat } from './charts'
import { fmtDelay, fmtInt, pct } from './format'
import { sumPerDay, weightedMedianDelay } from './series'

export function OverviewTab({ report, onPair }: { report: AnalyticsReportDto; onPair: (copierId: number, originalId: number) => void }) {
  const active = useMemo(() => report.sources.filter((s) => s.posts > 0), [report])
  const posts = active.reduce((n, s) => n + s.posts, 0)
  const copies = active.reduce((n, s) => n + s.copies, 0)
  const verbatim = report.pairs.reduce((n, p) => n + p.verbatim, 0)
  const forwards = report.pairs.reduce((n, p) => n + p.forwards, 0)
  const pairs = report.pairs.filter((p) => p.count > 0).length
  const median = weightedMedianDelay(active)
  const days = report.days
  const series = useMemo(
    () => [
      { label: 'постів', values: sumPerDay(active, 'posts', days.length), color: COLORS.posts },
      { label: 'з них копій', values: sumPerDay(active, 'copies', days.length), color: COLORS.copies },
    ],
    [active, days.length],
  )
  return (
    <>
      <Section title="Період" badge={<Badge ok={null} text={`${active.length} активних джерел`} />}>
        <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-3 lg:grid-cols-6">
          <Stat label="Постів" value={fmtInt(posts)} hint="логічних, редагування — один" />
          <Stat label="Копій" value={fmtInt(copies)} hint={`${pct(posts ? copies / posts : null)} постів`} />
          <Stat label="Унікальних" value={pct(posts ? 1 - copies / posts : null)} hint="постів без раніших аналогів" />
          <Stat label="Затримка копії" value={fmtDelay(median)} hint="медіана, зважена за копіями" />
          <Stat label="Дослівно / переслано" value={`${fmtInt(verbatim)} / ${fmtInt(forwards)}`} hint={copies ? `${pct(verbatim / copies)} / ${pct(forwards / copies)} копій` : undefined} />
          <Stat label="Пар джерел" value={fmtInt(pairs)} hint="копіювальник → першоджерело" />
        </div>
        <Columns days={days} series={series} ariaLabel="Пости й копії за днями" />
      </Section>

      <Section title="Хто кого копіює" badge={<span className="text-[11px] text-slate-500">клік по клітинці → «Пари»</span>}>
        <p className="text-xs text-slate-500">
          Рядок — хто копіює, стовпець — кого. У клітинці — копії, для яких це джерело було першим; тон — частка від постів копіювальника. У дужках — лише через проміжні оригінали (копіювальник читає це джерело, але не воно було першим).
        </p>
        <Heatmap sources={active} pairs={report.pairs} onSelect={onPair} />
      </Section>
    </>
  )
}

export function Heatmap({ sources, pairs, onSelect, selected }: { sources: SourceAnalyticsDto[]; pairs: CopyPairDto[]; onSelect?: (copierId: number, originalId: number) => void; selected?: { copierId?: number; originalId?: number } }) {
  const byKey = useMemo(() => new Map(pairs.map((p) => [`${p.copierId}-${p.originalId}`, p])), [pairs])
  if (sources.length === 0) return <div className="text-xs text-slate-500">За період нічого не проіндексовано.</div>
  return (
    <div className="overflow-x-auto">
      <table className="text-xs">
        <thead className="text-slate-500">
          <tr>
            <th className="py-1 pr-2 text-left font-normal">копіює ↓ · кого →</th>
            {sources.map((s) => (
              <th key={s.id} className="px-1 pb-1 text-center font-normal" title={s.name}>
                <span className="inline-block max-w-16 truncate align-bottom">{s.code.replace(/^tg_/, '')}</span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sources.map((copier) => (
            <tr key={copier.id} className="border-t border-slate-100 dark:border-slate-800">
              <td className="py-0.5 pr-2 whitespace-nowrap">{copier.name}</td>
              {sources.map((orig) => {
                if (orig.id === copier.id) return <td key={orig.id} className="bg-slate-100 text-center text-slate-300 dark:bg-slate-800/60 dark:text-slate-600">·</td>
                const p = byKey.get(`${copier.id}-${orig.id}`)
                const share = p && copier.posts ? p.count / copier.posts : 0
                const alpha = p && p.count > 0 ? 0.15 + Math.min(0.85, share * 3) : 0
                const isSelected = selected?.copierId === copier.id && selected?.originalId === orig.id
                const clickable = !!onSelect && !!p && p.countAll > 0
                return (
                  <td
                    key={orig.id}
                    className={`h-7 min-w-10 text-center font-mono ${clickable ? 'cursor-pointer hover:outline hover:outline-1 hover:outline-sky-500' : ''} ${isSelected ? 'outline outline-2 outline-sky-500' : ''}`}
                    style={alpha ? { background: `rgba(217, 119, 6, ${alpha.toFixed(2)})` } : undefined}
                    title={p ? `${copier.name} → ${orig.name}: ${p.count} копій (${Math.round(share * 100)}% постів), з проміжними ${p.countAll}; затримка сер. ${fmtDelay(p.avgDelaySeconds)}, мед. ${fmtDelay(p.medianDelaySeconds)}` : undefined}
                    onClick={clickable ? () => onSelect(copier.id, orig.id) : undefined}
                  >
                    {p && p.count > 0 ? p.count : p && p.countAll > 0 ? <span className="text-slate-400">({p.countAll})</span> : ''}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
