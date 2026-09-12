import { colors } from '../map/geojson'

/** Spec §15: the UI distinguishes source facts, normalisation, correlation and forecast. */
export default function Legend() {
  return (
    <div className="space-y-1 text-xs text-slate-600 dark:text-slate-300">
      <div className="font-medium text-slate-800 dark:text-slate-100">Легенда</div>
      {(['uav', 'cruise', 'ballistic', 'aircraft'] as const).map((m) => (
        <div key={m} className="flex items-center gap-2">
          <span className="inline-block h-3 w-3 rounded-full" style={{ background: colors[m] }} />
          {m === 'uav' ? 'БпЛА / КАБ' : m === 'cruise' ? 'Крилаті ракети' : m === 'ballistic' ? 'Балістика / аеробалістика' : 'Авіація'}
        </div>
      ))}
      <div className="flex items-center gap-2">
        <span className="inline-block h-1 w-5 rounded bg-yellow-400 outline outline-1 outline-slate-700" /> шлях за повідомленнями (факт)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block w-5 border-t-[3px] border-dashed border-yellow-400" /> прогноз напрямку (розрахунок), стрілка = курс
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-5 bg-red-300 outline outline-1 outline-red-600 dark:bg-red-900" /> повітряна тривога (червоний рівень)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-5 bg-yellow-300 outline outline-1 outline-yellow-600 dark:bg-yellow-700" /> жовтий рівень (загроза, район)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-5 outline-dashed outline-2 outline-amber-500" /> останній відомий район (не точка, не тривога)
      </div>
      <div>Маркер блідне з часом: новий → 5 хв → 10 хв → 20 хв.</div>
    </div>
  )
}
