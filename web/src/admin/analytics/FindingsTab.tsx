import { useMemo, useState } from 'react'
import { analytics, type AnalyticsReportDto, type CopyKind } from '../../api/analytics'
import { Badge, Section } from '../../components/settings/fields'
import { Histogram } from './charts'
import { useLoad } from './data'
import { FindingCard } from './FindingCard'
import { KIND_LABEL } from './format'
import { bucketDelays } from './series'

interface Filter {
  limit: number
  sourceId?: number
  kind?: CopyKind
  primaryOnly: boolean
}

/** The latest detected pairs with both texts: to judge the thresholds by eye. Report is optional — only for source names. */
export function FindingsTab({ report }: { report: AnalyticsReportDto | null }) {
  const [filter, setFilter] = useState<Filter>({ limit: 30, primaryOnly: false })
  const key = `recent:${filter.limit}:${filter.sourceId ?? ''}:${filter.kind ?? ''}:${filter.primaryOnly}`
  const { data, error, loading } = useLoad(key, () => analytics.recent(filter), 60_000)
  const nameOf = useMemo(() => {
    const m = new Map((report?.sources ?? []).map((s) => [s.id, s.name]))
    return (id: number) => m.get(id) ?? `#${id}`
  }, [report])
  const delays = useMemo(() => bucketDelays((data ?? []).map((r) => r.delaySeconds)), [data])
  const selectClass = 'rounded border border-slate-300 bg-white px-1 py-0.5 text-xs dark:border-slate-600 dark:bg-slate-800'
  return (
    <Section
      title="Останні знахідки"
      badge={
        <span className="flex flex-wrap items-center gap-2 text-xs">
          <select className={selectClass} value={filter.sourceId ?? ''} onChange={(e) => setFilter({ ...filter, sourceId: e.target.value ? Number(e.target.value) : undefined })}>
            <option value="">усі джерела</option>
            {(report?.sources ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
          <select className={selectClass} value={filter.kind ?? ''} onChange={(e) => setFilter({ ...filter, kind: (e.target.value || undefined) as CopyKind | undefined })}>
            <option value="">усі види</option>
            {(Object.keys(KIND_LABEL) as CopyKind[]).map((k) => (
              <option key={k} value={k}>
                {KIND_LABEL[k]}
              </option>
            ))}
          </select>
          <label className="flex items-center gap-1">
            <input type="checkbox" checked={filter.primaryOnly} onChange={(e) => setFilter({ ...filter, primaryOnly: e.target.checked })} />
            лише первинні
          </label>
          <select className={selectClass} value={filter.limit} onChange={(e) => setFilter({ ...filter, limit: Number(e.target.value) })}>
            {[30, 60, 100, 200].map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
          {data && <Badge ok={null} text={`${data.length}`} />}
        </span>
      }
    >
      <p className="text-xs text-slate-500">
        Пари в порядку виявлення, з обома текстами — щоб бачити, що саме алгоритм вважає копією (пороги: Жаккар ≥ 0.7 або вкладеність ≥ 0.85; дослівно — Жаккар ≥ 0.9; пересилання — Telegram-forward з каналу першоджерела). Спільні слова (від 4 літер) підсвічено; «первинні» — збіги з найпершим джерелом, решта — через проміжні оригінали.
      </p>
      {error && <div className="text-xs text-red-600">{error.message}</div>}
      {!data && !error && loading && <div className="text-xs text-slate-500">Завантаження…</div>}
      {data && data.length > 0 && (
        <details className="text-xs">
          <summary className="cursor-pointer text-slate-500">Затримки в цій вибірці</summary>
          <Histogram buckets={delays} height={130} />
        </details>
      )}
      {data && data.length === 0 && <div className="text-xs text-slate-500">Пар з такими фільтрами ще немає.</div>}
      <div className="space-y-2">
        {(data ?? []).map((r) => (
          <FindingCard key={`${r.copyRawMessageId}-${r.originalRawMessageId}`} item={r} nameOf={nameOf} />
        ))}
      </div>
    </Section>
  )
}
