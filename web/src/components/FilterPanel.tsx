import { useStore, type Filters } from '../store/useStore'
import HomeLocationPicker from './HomeLocationPicker'
import Legend from './Legend'

const items: { key: keyof Filters; label: string }[] = [
  { key: 'uav', label: 'БпЛА' },
  { key: 'cruise', label: 'Крилаті ракети' },
  { key: 'ballistic', label: 'Балістика' },
  { key: 'aircraft', label: 'Авіація' },
  { key: 'alerts', label: 'Тривоги' },
  { key: 'activeOnly', label: 'Лише активні' },
]

interface Props {
  open: boolean
  picking: boolean
  onPickingChange: (v: boolean) => void
  onReplay: () => void
}

/** Spec §19 left panel: filters, my location, history, legend. Becomes a bottom sheet on phones. */
export default function FilterPanel({ open, picking, onPickingChange, onReplay }: Props) {
  const filters = useStore((s) => s.filters)
  const setFilter = useStore((s) => s.setFilter)
  const trackCount = useStore((s) => Object.keys(s.tracks).length)
  const alertCount = useStore((s) => Object.keys(s.alerts).length)

  return (
    <aside
      className={`pointer-events-auto absolute z-10 flex max-h-[60vh] w-full flex-col gap-4 overflow-y-auto rounded-t-xl bg-white/95 p-3 shadow-lg backdrop-blur transition-transform md:left-3 md:top-14 md:max-h-[calc(100vh-5rem)] md:w-72 md:rounded-xl dark:bg-slate-900/95 dark:text-slate-100 ${
        open ? 'bottom-0 translate-y-0' : 'bottom-0 translate-y-full md:translate-y-0'
      }`}
    >
      <div>
        <div className="mb-1 flex items-baseline justify-between">
          <span className="font-medium">Фільтри</span>
          <span className="text-xs text-slate-500">
            {trackCount} об'єктів · {alertCount} тривог
          </span>
        </div>
        <div className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
          {items.map((it) => (
            <label key={it.key} className="flex items-center gap-2">
              <input type="checkbox" checked={filters[it.key]} onChange={(e) => setFilter(it.key, e.target.checked)} />
              {it.label}
            </label>
          ))}
        </div>
      </div>
      <HomeLocationPicker picking={picking} onPickingChange={onPickingChange} />
      <button className="rounded-md border border-indigo-300 px-2 py-1 text-sm text-indigo-700 hover:bg-indigo-50 dark:border-indigo-700 dark:text-indigo-300 dark:hover:bg-indigo-950" onClick={onReplay}>
        ⏱ Відтворення історії
      </button>
      <Legend />
    </aside>
  )
}
