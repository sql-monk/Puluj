import { usePalette, useStore } from '../store/useStore'

/** Spec §15: the UI distinguishes source facts, normalisation, correlation and forecast. */
export default function Legend() {
  const lifetime = useStore((s) => s.filters.lifetimeMinutes)
  const p = usePalette()
  return (
    <div className="space-y-1 text-xs text-slate-600 dark:text-slate-300">
      <div className="font-medium text-slate-800 dark:text-slate-100">Легенда</div>
      {(['uav', 'cruise', 'ballistic', 'aircraft'] as const).map((m) => (
        <div key={m} className="flex items-center gap-2">
          <span className="inline-block h-3 w-3 rounded-full" style={{ background: p.marker[m] }} />
          {m === 'uav' ? 'БпЛА / КАБ' : m === 'cruise' ? 'Крилаті ракети' : m === 'ballistic' ? 'Балістика / аеробалістика' : 'Авіація'}
        </div>
      ))}
      <div className="flex items-center gap-2">
        <span className="inline-flex w-6 items-center justify-between">
          <span className="inline-block h-2 w-2 rounded-full opacity-50" style={{ background: p.marker.uav }} />
          <span className="inline-block h-0 w-3 border-t border-dotted" style={{ borderColor: p.vector.uav }} />
        </span>
        сліди: де ціль була раніше, з часом (для виділеної — завжди)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block w-6 border-t-[2px] border-dashed" style={{ borderColor: p.vector.uav }} /> прогноз курсу на кілька хвилин (розрахунок), шеврон = напрямок
      </div>
      <div className="flex items-center gap-2">
        <span
          className="inline-block h-3 w-6"
          style={{ backgroundImage: `repeating-linear-gradient(135deg, ${p.vector.uav} 0 2px, transparent 2px 6px)` }}
        />
        зона ймовірного руху (ширша — курс менш певний)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-flex gap-0.5">
          <span className="rounded-full bg-blue-800 px-1 text-[9px] font-bold text-white">3</span>
          <span className="rounded-full border border-slate-700 bg-white px-1 text-[9px] font-bold text-slate-900">×5</span>
        </span>
        бейджі: кількість джерел · цілей у групі
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-3 rounded-full" style={{ background: p.selection, outline: `2px solid ${p.marker.uav}` }} /> виділена ціль ·
        <span className="inline-block h-3 w-3 rounded-full" style={{ background: p.marker.uav, outline: `2px solid ${p.selection}` }} /> сусіди з того ж повідомлення
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-3 rounded-full border-2" style={{ borderColor: p.threatNear }} /> поруч з вашою точкою
        <span className="ml-1 inline-block h-3 w-3 rounded-full border-2" style={{ borderColor: p.threatTowards }} /> курс у ваш бік
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-5 outline outline-1" style={{ background: p.alertRedFill, outlineColor: p.alertRedLine }} /> повітряна тривога (червоний рівень)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-5 outline outline-1" style={{ background: p.alertYellowFill, outlineColor: p.alertYellowLine }} /> жовтий рівень (загроза, район)
      </div>
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-5 outline-dashed outline-2" style={{ outlineColor: p.marker.uav }} /> останній відомий район (не точка, не тривога)
      </div>
      <div>Бліда піктограма = «на підході до пункту»: позиція приблизна. Позначка блідне і зникає через {lifetime} хв після останнього повідомлення (див. «Час життя позначки»).</div>
    </div>
  )
}
