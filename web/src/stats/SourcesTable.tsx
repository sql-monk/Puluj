import type { StatsSourceDto } from '../api/types'
import Sparkline from './charts/Sparkline'
import { lagText, pct } from './period'

/**
 * One row per source: how much it published, its trend over the buckets, how much of it was processed, how much
 * yielded facts, how many facts (repeats excluded) and the median delay between publishing and collection.
 */
export default function SourcesTable({ sources, sparkColor }: { sources: StatsSourceDto[]; sparkColor: string }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead className="text-left text-slate-500 dark:text-slate-400">
          <tr>
            <th className="py-1 pr-2 font-medium">Джерело</th>
            <th className="pr-2 text-right font-medium">Повідомлень</th>
            <th className="pr-2 font-medium">Динаміка</th>
            <th className="pr-2 text-right font-medium" title="Частка повідомлень, які вже пройшли обробку">
              Оброблено
            </th>
            <th className="pr-2 text-right font-medium" title="Частка оброблених повідомлень, з яких розібрано хоча б один факт">
              З фактами
            </th>
            <th className="pr-2 text-right font-medium" title="Факти без повторів іншого джерела">
              Фактів
            </th>
            <th className="pr-2 text-right font-medium" title="Медіана «отримано − опубліковано» для повідомлень, зібраних наживо">
              Затримка
            </th>
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {sources.map((s) => (
            <tr key={s.id} className="border-t border-slate-100 dark:border-slate-800">
              <td className="py-1.5 pr-2">
                <span className="font-medium">{s.name}</span> <span className="text-slate-400">{s.code}</span>
              </td>
              <td className="pr-2 text-right">{s.messages.toLocaleString('uk-UA')}</td>
              <td className="pr-2">
                <Sparkline values={s.series} color={sparkColor} />
              </td>
              <td className="pr-2 text-right">{pct(s.processed, s.messages)}</td>
              <td className="pr-2 text-right">{pct(s.withTargets, s.processed)}</td>
              <td className="pr-2 text-right">{s.targets.toLocaleString('uk-UA')}</td>
              <td className="pr-2 text-right">{lagText(s.medianLagSeconds)}</td>
            </tr>
          ))}
          {sources.length === 0 && (
            <tr>
              <td colSpan={7} className="py-2 text-slate-500 dark:text-slate-400">
                за період повідомлень немає
              </td>
            </tr>
          )}
        </tbody>
      </table>
    </div>
  )
}
