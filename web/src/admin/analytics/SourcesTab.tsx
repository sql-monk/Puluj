import { useMemo, useState, type ReactNode } from 'react'
import type { AnalyticsReportDto, SourceAnalyticsDto } from '../../api/analytics'
import { Badge, Section } from '../../components/settings/fields'
import { COLORS, Columns, HourStrip, ShareBar } from './charts'
import { fmtDelay, fmtInt, pct } from './format'

type SortKey = 'unique' | 'posts' | 'copies' | 'copiedBy'

const SORTS: { id: SortKey; label: string }[] = [
  { id: 'unique', label: 'за унікальністю' },
  { id: 'posts', label: 'за постами' },
  { id: 'copies', label: 'за копіями' },
  { id: 'copiedBy', label: 'за «скопійовано»' },
]

function sortBy(key: SortKey) {
  return (a: SourceAnalyticsDto, b: SourceAnalyticsDto) => {
    if (key === 'unique') return (b.uniqueShare ?? -1) - (a.uniqueShare ?? -1) || b.posts - a.posts
    return b[key] - a[key] || b.posts - a.posts
  }
}

export function SourcesTab({ report }: { report: AnalyticsReportDto }) {
  const [sort, setSort] = useState<SortKey>('unique')
  const active = useMemo(() => report.sources.filter((s) => s.posts > 0).sort(sortBy(sort)), [report, sort])
  const nameOf = useMemo(() => new Map(report.sources.map((s) => [s.id, s.name])), [report])
  return (
    <>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className="text-slate-500">Сортування:</span>
        {SORTS.map((s) => (
          <button key={s.id} className={`rounded px-2 py-0.5 ${sort === s.id ? 'bg-slate-200 font-medium dark:bg-slate-700' : 'hover:bg-slate-100 dark:hover:bg-slate-800'}`} onClick={() => setSort(s.id)}>
            {s.label}
          </button>
        ))}
        <span className="ml-auto text-slate-500">
          Пости — логічні (редагування одного поста рахується раз). Копія — пост, що повторює раніший пост іншого джерела; «скопійовано» — скільки разів інші повторили це джерело.
        </span>
      </div>
      {active.length === 0 && <div className="text-xs text-slate-500">За період нічого не проіндексовано.</div>}
      <div className="grid gap-3 lg:grid-cols-2">
        {active.map((s) => (
          <SourceCard key={s.id} source={s} days={report.days} />
        ))}
      </div>
      {report.externalForwards.length > 0 && (
        <Section title="Пересилання з-поза системи" badge={<Badge ok={null} text={`${report.externalForwards.length} каналів`} />}>
          <p className="text-xs text-slate-500">Канали, з яких наші джерела пересилають найчастіше, але яких ми не збираємо — кандидати на додавання у «Джерела».</p>
          <table className="w-full text-xs">
            <thead className="text-left text-slate-500">
              <tr>
                <th className="py-1 pr-2">Джерело</th>
                <th className="pr-2">Пересилає з</th>
                <th className="pr-2">Постів</th>
              </tr>
            </thead>
            <tbody>
              {report.externalForwards.map((f) => (
                <tr key={`${f.sourceId}-${f.channelRef}`} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1 pr-2">{nameOf.get(f.sourceId) ?? `#${f.sourceId}`}</td>
                  <td className="pr-2 font-mono">{f.channelRef}</td>
                  <td className="pr-2">{fmtInt(f.count)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
      )}
    </>
  )
}

function SourceCard({ source: s, days }: { source: SourceAnalyticsDto; days: string[] }) {
  const series = [
    { label: 'постів', values: s.perDay.map((d) => d.posts), color: COLORS.posts },
    { label: 'копій', values: s.perDay.map((d) => d.copies), color: COLORS.copies },
    { label: 'скопійовано', values: s.perDay.map((d) => d.copiedBy), color: COLORS.copiedBy },
  ]
  return (
    <section className={`space-y-2 rounded-lg border border-slate-200 p-3 text-xs dark:border-slate-700 ${s.enabled ? '' : 'opacity-60'}`}>
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="truncate text-sm font-semibold">
          {s.name} <span className="font-normal text-slate-400">{s.code}</span>
        </h3>
        <span className="flex items-center gap-2">
          {!s.enabled && <Badge ok={false} text="вимкнено" />}
          <ShareBar value={s.uniqueShare} width="w-20" text={`${pct(s.uniqueShare)} унік.`} />
        </span>
      </div>
      <dl className="grid grid-cols-3 gap-x-3 gap-y-1 sm:grid-cols-4">
        <Row label="Постів" value={<>{fmtInt(s.posts)}{s.edits > 0 && <span className="text-slate-400"> +{fmtInt(s.edits)} ред.</span>}</>} />
        <Row label="Копій" value={<>{fmtInt(s.copies)} <span className="text-slate-400">({pct(s.posts ? s.copies / s.posts : null)})</span></>} />
        <Row label="Скопійовано" value={fmtInt(s.copiedBy)} />
        <Row label="Дослівних" value={pct(s.verbatimShare)} />
        <Row label="Затримка сер." value={fmtDelay(s.avgCopyDelaySeconds)} title="середня затримка, з якою це джерело повторює інших" />
        <Row label="Затримка мед." value={fmtDelay(s.medianCopyDelaySeconds)} />
        <Row label="Випередження" value={fmtDelay(s.avgLeadSeconds)} title="на скільки в середньому це джерело випереджає тих, хто його повторює" />
        <Row label="Пересилань" value={<>{fmtInt(s.forwardsInternal)} <span className="text-slate-400">/ {fmtInt(s.forwardsExternal)}</span></>} title="з наших джерел / із зовнішніх каналів" />
      </dl>
      <Columns days={days} series={series} height={100} ariaLabel={`${s.name}: пости, копії, скопійовано за днями`} />
      <div>
        <div className="mb-0.5 text-[10px] uppercase tracking-wide text-slate-500">Години доби (Київ)</div>
        <HourStrip values={s.perHour} />
      </div>
    </section>
  )
}

function Row({ label, value, title }: { label: string; value: ReactNode; title?: string }) {
  return (
    <div title={title}>
      <dt className="text-[10px] uppercase tracking-wide text-slate-500">{label}</dt>
      <dd className="font-mono">{value}</dd>
    </div>
  )
}
