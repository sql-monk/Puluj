import { useMemo } from 'react'
import { useStore, type Filters } from '../store/useStore'
import Legend from './Legend'

const items: { key: keyof Filters; label: string }[] = [
  { key: 'uav', label: 'БпЛА' },
  { key: 'cruise', label: 'Крилаті ракети' },
  { key: 'ballistic', label: 'Балістика' },
  { key: 'aircraft', label: 'Авіація' },
  { key: 'alerts', label: 'Тривоги' },
  { key: 'activeOnly', label: 'Лише активні' },
]

const RECENT_MS = 60 * 60_000

/** Kyiv page left panel: the ten districts with their alert state and recent message count, plus the class filters. */
export default function KyivPanel({ open }: { open: boolean }) {
  const regions = useStore((s) => s.regions)
  const alerts = useStore((s) => s.alerts)
  const observations = useStore((s) => s.observations)
  const filters = useStore((s) => s.filters)
  const setFilter = useStore((s) => s.setFilter)
  const selectedRegionId = useStore((s) => s.selectedRegionId)
  const selectRegion = useStore((s) => s.selectRegion)
  const now = useStore((s) => s.now)

  const kyiv = useMemo(() => regions.find((r) => r.level === 'City' && r.countryCode === 'UA' && r.name === 'Київ'), [regions])
  const districts = useMemo(
    () => regions.filter((r) => r.level === 'District' && r.parentId === kyiv?.id).sort((a, b) => a.name.localeCompare(b.name, 'uk')),
    [regions, kyiv],
  )
  const active = useMemo(() => Object.values(alerts).filter((a) => !a.endedAt), [alerts])
  const cityLevel = kyiv ? active.find((a) => a.placeId === kyiv.id)?.level : undefined
  const since = now.getTime() - RECENT_MS
  const recent = (placeId: number) => observations.filter((o) => new Date(o.observedAt).getTime() >= since && (o.location?.placeId === placeId || o.destination?.placeId === placeId)).length

  const rows = districts.map((d) => {
    const own = active.find((a) => a.placeId === d.id)?.level
    // Any non-yellow alert (red or unlevelled) on the district or the whole city counts as a full alert.
    const levels = [own, cityLevel].filter((l): l is NonNullable<typeof l> => !!l)
    const level: 'red' | 'yellow' | null = levels.some((l) => l !== 'Yellow') ? 'red' : levels.length > 0 ? 'yellow' : null
    return { id: d.id, name: d.name.replace(' район', ''), level, recent: recent(d.id) }
  })

  const chip = (level: 'red' | 'yellow' | null) =>
    level === 'red' ? (
      <span className="rounded bg-red-600 px-1.5 py-0.5 text-[10px] font-medium text-white">тривога</span>
    ) : level === 'yellow' ? (
      <span className="rounded bg-yellow-400 px-1.5 py-0.5 text-[10px] font-medium text-slate-900">жовтий</span>
    ) : (
      <span className="text-[10px] text-slate-400">—</span>
    )

  return (
    <aside
      className={`pointer-events-auto absolute z-10 flex max-h-[60vh] w-full flex-col gap-3 overflow-y-auto rounded-t-xl bg-white/95 p-3 shadow-lg backdrop-blur transition-transform md:left-3 md:top-14 md:max-h-[calc(100vh-5rem)] md:w-72 md:rounded-xl dark:bg-slate-900/95 dark:text-slate-100 ${
        open ? 'bottom-0 translate-y-0' : 'bottom-0 translate-y-full md:translate-y-0'
      }`}
    >
      <div>
        <div className="mb-1 flex items-baseline justify-between">
          <span className="font-medium">Райони Києва</span>
          {kyiv && (
            <button
              className={`text-xs ${selectedRegionId === kyiv.id ? 'font-medium text-blue-700 dark:text-blue-300' : 'text-slate-500 hover:underline'}`}
              onClick={() => selectRegion(kyiv.id)}
            >
              усе місто {cityLevel && chip(cityLevel === 'Yellow' ? 'yellow' : 'red')}
            </button>
          )}
        </div>
        {rows.length === 0 && <div className="text-xs text-slate-500">Полігони районів ще не завантажені.</div>}
        <ul className="text-sm">
          {rows.map((r) => (
            <li key={r.id}>
              <button
                className={`flex w-full items-center gap-2 rounded px-1.5 py-1 text-left hover:bg-slate-100 dark:hover:bg-slate-800 ${selectedRegionId === r.id ? 'bg-blue-50 font-medium dark:bg-blue-900/40' : ''}`}
                onClick={() => selectRegion(selectedRegionId === r.id ? (kyiv?.id ?? null) : r.id)}
              >
                <span className="flex-1 truncate">{r.name}</span>
                {r.recent > 0 && <span className="rounded-full bg-slate-200 px-1.5 text-[10px] text-slate-700 dark:bg-slate-700 dark:text-slate-200" title="повідомлень за годину">{r.recent}</span>}
                {chip(r.level)}
              </button>
            </li>
          ))}
        </ul>
      </div>
      <div>
        <div className="mb-1 font-medium">Фільтри</div>
        <div className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
          {items.map((it) => (
            <label key={it.key} className="flex items-center gap-2">
              <input type="checkbox" checked={filters[it.key]} onChange={(e) => setFilter(it.key, e.target.checked)} />
              {it.label}
            </label>
          ))}
        </div>
      </div>
      <Legend />
    </aside>
  )
}
