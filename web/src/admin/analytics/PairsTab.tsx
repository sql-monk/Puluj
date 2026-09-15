import { useMemo } from 'react'
import { analytics, type AnalyticsReportDto, type CopyPairDto } from '../../api/analytics'
import { Badge, Section } from '../../components/settings/fields'
import { Histogram, Stat } from './charts'
import { useLoad } from './data'
import { FindingCard } from './FindingCard'
import { fmtDelay, fmtInt, pct } from './format'

export interface PairFilter {
  copierId?: number
  originalId?: number
}

export function PairsTab({ report, days, filter, setFilter, selected, setSelected }: { report: AnalyticsReportDto; days: number; filter: PairFilter; setFilter: (f: PairFilter) => void; selected: PairFilter | null; setSelected: (p: PairFilter | null) => void }) {
  const nameOf = useMemo(() => {
    const m = new Map(report.sources.map((s) => [s.id, s.name]))
    return (id: number) => m.get(id) ?? `#${id}`
  }, [report])
  const rows = useMemo(
    () =>
      report.pairs
        .filter((p) => p.countAll > 0)
        .filter((p) => (filter.copierId ? p.copierId === filter.copierId : true))
        .filter((p) => (filter.originalId ? p.originalId === filter.originalId : true))
        .sort((a, b) => b.count - a.count || b.countAll - a.countAll),
    [report, filter],
  )
  const selectClass = 'rounded border border-slate-300 bg-white px-1 py-0.5 text-xs dark:border-slate-600 dark:bg-slate-800'
  const sourceOptions = report.sources.map((s) => (
    <option key={s.id} value={s.id}>
      {s.name}
    </option>
  ))
  const pick = (p: CopyPairDto) => setSelected(selected?.copierId === p.copierId && selected?.originalId === p.originalId ? null : { copierId: p.copierId, originalId: p.originalId })
  return (
    <>
      <Section
        title="Пари копіювальник → першоджерело"
        badge={
          <span className="flex items-center gap-2 text-xs">
            <select className={selectClass} value={filter.copierId ?? ''} onChange={(e) => setFilter({ ...filter, copierId: e.target.value ? Number(e.target.value) : undefined })}>
              <option value="">копіювальник: усі</option>
              {sourceOptions}
            </select>
            <select className={selectClass} value={filter.originalId ?? ''} onChange={(e) => setFilter({ ...filter, originalId: e.target.value ? Number(e.target.value) : undefined })}>
              <option value="">першоджерело: усі</option>
              {sourceOptions}
            </select>
            {(filter.copierId || filter.originalId) && (
              <button className="rounded border border-slate-300 px-2 py-0.5 dark:border-slate-600" onClick={() => setFilter({})}>
                скинути
              </button>
            )}
            <Badge ok={null} text={`${rows.length} пар`} />
          </span>
        }
      >
        <p className="text-xs text-slate-500">«Копій» — випадки, де це джерело було найпершим з тим самим текстом; «з проміжними» — усі збіги, включно з тими, де першим було ще інше джерело. Клік по рядку — розподіл затримок і приклади.</p>
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <thead className="text-left text-slate-500">
              <tr>
                <th className="py-1 pr-2">Копіювальник</th>
                <th className="pr-2">Першоджерело</th>
                <th className="pr-2">Копій</th>
                <th className="pr-2">З проміжними</th>
                <th className="pr-2">Дослівних</th>
                <th className="pr-2">Пересилань</th>
                <th className="pr-2">Затримка сер. / мед. / мін.</th>
                <th className="pr-2">Схожість</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => {
                const on = selected?.copierId === p.copierId && selected?.originalId === p.originalId
                return (
                  <tr key={`${p.copierId}-${p.originalId}`} className={`cursor-pointer border-t border-slate-100 dark:border-slate-800 ${on ? 'bg-sky-50 dark:bg-sky-900/30' : 'hover:bg-slate-50 dark:hover:bg-slate-800/60'}`} onClick={() => pick(p)}>
                    <td className="py-1 pr-2">{nameOf(p.copierId)}</td>
                    <td className="pr-2">{nameOf(p.originalId)}</td>
                    <td className="pr-2 font-mono">{fmtInt(p.count)}</td>
                    <td className="pr-2 text-slate-500">{fmtInt(p.countAll)}</td>
                    <td className="pr-2">{fmtInt(p.verbatim)}</td>
                    <td className="pr-2">{fmtInt(p.forwards)}</td>
                    <td className="pr-2">
                      {fmtDelay(p.avgDelaySeconds)} / {fmtDelay(p.medianDelaySeconds)} / {fmtDelay(p.minDelaySeconds)}
                    </td>
                    <td className="pr-2 font-mono">{p.avgJaccard.toFixed(2)}</td>
                  </tr>
                )
              })}
              {rows.length === 0 && (
                <tr>
                  <td colSpan={8} className="py-1 text-slate-500">
                    Копіювань за період не знайдено.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </Section>
      {selected?.copierId && selected.originalId && <PairDetails copierId={selected.copierId} originalId={selected.originalId} days={days} nameOf={nameOf} onClose={() => setSelected(null)} />}
    </>
  )
}

function PairDetails({ copierId, originalId, days, nameOf, onClose }: { copierId: number; originalId: number; days: number; nameOf: (id: number) => string; onClose: () => void }) {
  const { data, error, loading } = useLoad(`pair:${copierId}:${originalId}:${days}`, () => analytics.pair(copierId, originalId, days))
  return (
    <Section
      title={`${nameOf(copierId)} ← ${nameOf(originalId)}`}
      badge={
        <button className="rounded border border-slate-300 px-2 py-0.5 text-xs dark:border-slate-600" onClick={onClose}>
          закрити
        </button>
      }
    >
      {error && <div className="text-xs text-red-600">{error.message}</div>}
      {!data && !error && loading && <div className="text-xs text-slate-500">Завантаження…</div>}
      {data && (
        <>
          <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-3 lg:grid-cols-6">
            <Stat label="Копій" value={fmtInt(data.count)} hint={`з проміжними ${fmtInt(data.countAll)}`} />
            <Stat label="Дослівних" value={fmtInt(data.verbatim)} hint={pct(data.count ? data.verbatim / data.count : null)} />
            <Stat label="Пересилань" value={fmtInt(data.forwards)} hint={pct(data.count ? data.forwards / data.count : null)} />
            <Stat label="Затримка сер." value={fmtDelay(data.avgDelaySeconds)} />
            <Stat label="Затримка мед. / мін." value={`${fmtDelay(data.medianDelaySeconds)} / ${fmtDelay(data.minDelaySeconds)}`} />
            <Stat label="Схожість" value={data.avgJaccard === null || data.avgJaccard === undefined ? '—' : data.avgJaccard.toFixed(2)} hint="середній Жаккар" />
          </div>
          <div>
            <div className="mb-1 text-[10px] uppercase tracking-wide text-slate-500">Затримка копіювання за {data.days} дн. (первинні збіги)</div>
            {data.count === 0 ? <div className="text-xs text-slate-500">За період ця пара не має первинних копій — лише збіги через проміжні оригінали.</div> : <Histogram buckets={data.delays} />}
          </div>
          <div>
            <div className="mb-1 text-[10px] uppercase tracking-wide text-slate-500">Останні приклади ({data.recent.length})</div>
            <div className="space-y-2">
              {data.recent.map((r) => (
                <FindingCard key={`${r.copyRawMessageId}-${r.originalRawMessageId}`} item={r} nameOf={nameOf} />
              ))}
              {data.recent.length === 0 && <div className="text-xs text-slate-500">Прикладів за період немає.</div>}
            </div>
          </div>
        </>
      )}
    </Section>
  )
}
