import { useState } from 'react'
import { analytics, type AnalyticsStatusDto } from '../../api/analytics'
import { Badge, Section } from '../../components/settings/fields'
import { Stat } from './charts'
import { invalidate } from './data'
import { ago, fmtBytes, fmtDuration, fmtInt, fmtRate, fmtTime, uptime } from './format'

export const HEARTBEAT_STALE_MS = 90_000

export function serviceBadge(status: AnalyticsStatusDto | null, now: number): { ok: boolean | null; text: string } | null {
  if (!status) return null
  const alive = !!status.heartbeatAt && now - new Date(status.heartbeatAt).getTime() < HEARTBEAT_STALE_MS
  if (!status.initialized) return { ok: false, text: 'ще не запускався' }
  if (!alive) return { ok: false, text: 'heartbeat застарів' }
  const last = status.lastRun
  if (last?.status === 'failed') return { ok: false, text: 'останній прогін з помилкою' }
  if (last?.status === 'running') return { ok: null, text: 'прогін триває' }
  if (status.backlog > 5000) return { ok: null, text: `відстає на ${fmtInt(status.backlog)}` }
  return { ok: true, text: 'працює' }
}

export function ServiceTab({ status, error, fetchedAt, reload }: { status: AnalyticsStatusDto | null; error: string | null; fetchedAt: number; reload: () => void }) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const badge = serviceBadge(status, fetchedAt)
  const inst = status?.instance ?? null

  const reset = async () => {
    if (!window.confirm('Видалити індекс, знайдені пари і першоджерела? Сервіс перебудує все з нуля на наступному циклі (~10–20 хв на 400 тис. повідомлень); цей час панель показуватиме порожні дані.')) return
    setBusy(true)
    setMessage(null)
    try {
      await analytics.reset()
      invalidate('report:')
      invalidate('pair:')
      invalidate('recent:')
      setMessage('Індекс очищено. Сервіс перебудує його на наступному циклі.')
      reload()
    } catch (e) {
      setMessage((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <Section title="Сервіс аналітики" badge={badge && <Badge ok={badge.ok} text={badge.text} />}>
        <p className="text-xs text-slate-500">
          Окремий сервіс (контейнер <code>analytics</code>, локально <code>Puluj.Analytics.Worker</code>) читає нові повідомлення, нормалізує текст, рахує MinHash-відбиток і шукає серед інших джерел пости з тим самим змістом. Результати — у схемі <code>analytics</code>; ця панель читає її напряму.
        </p>
        {error && <div className="text-xs text-red-600">{error}</div>}
        {status && !status.initialized && (
          <div className="rounded border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900 dark:border-amber-700 dark:bg-amber-900/30 dark:text-amber-100">
            Сервіс ще не створив свою схему в базі — запустіть контейнер <code>analytics</code> (або <code>Puluj.Analytics.Worker</code> локально). Перший прогін над усією історією триває 10–20 хвилин.
          </div>
        )}
        {status && (
          <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
            <Stat label="Heartbeat" value={ago(status.heartbeatAt, fetchedAt)} title={fmtTime(status.heartbeatAt)} hint={status.heartbeatAt ? fmtTime(status.heartbeatAt) : 'ключ Runtime:Worker:analytics:Heartbeat відсутній'} />
            <Stat label="Версія" value={inst?.version ?? '—'} hint={inst?.builtAt ? `збірка ${fmtTime(inst.builtAt)}` : 'статус інстансу ще не публікується'} />
            <Stat label="Uptime" value={uptime(inst?.startedAt, fetchedAt)} hint={inst?.startedAt ? `з ${fmtTime(inst.startedAt)}` : undefined} />
            <Stat label="Процес" value={inst ? `${inst.host ?? '?'} · pid ${inst.pid ?? '?'}` : '—'} hint={inst ? `${fmtBytes(inst.workingSetBytes)} · CPU ${inst.cpuPercent === null || inst.cpuPercent === undefined ? '—' : `${inst.cpuPercent.toFixed(0)}%`} · ${inst.threads ?? '—'} потоків` : undefined} />
            <Stat label="Відставання" value={`${fmtInt(status.backlog)} повідомл.`} hint={`курсор ${fmtInt(status.watermark)} з ${fmtInt(status.latestRawMessageId)}`} />
            <Stat label="Проіндексовано" value={fmtInt(status.messagesIndexed)} hint={`${fmtInt(status.messagesFingerprinted)} з відбитком`} />
            <Stat label="Пар знайдено" value={fmtInt(status.pairsTotal)} />
            <Stat label="Схема analytics" value={fmtBytes(status.schemaBytes)} hint={`${status.migrations.length} міграцій`} />
          </div>
        )}
        {status?.initialized && (
          <div className="flex flex-wrap items-center gap-3">
            <button className="rounded border border-red-300 px-3 py-1 text-xs text-red-700 hover:bg-red-50 disabled:opacity-50 dark:border-red-700 dark:text-red-300 dark:hover:bg-red-900/30" disabled={busy} onClick={() => void reset()}>
              {busy ? 'Очищую…' : 'Перебудувати індекс'}
            </button>
            {message && <span className="text-xs text-slate-600 dark:text-slate-300">{message}</span>}
          </div>
        )}
      </Section>

      {status && status.runs.length > 0 && (
        <Section title="Останні прогони" badge={<Badge ok={null} text={`${status.runs.length}`} />}>
          <p className="text-xs text-slate-500">Прогін — від пробудження до вичерпання черги; швидкість — скановано повідомлень за секунду тривалості.</p>
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead className="text-left text-slate-500">
                <tr>
                  <th className="py-1 pr-2">Початок</th>
                  <th className="pr-2">Стан</th>
                  <th className="pr-2">Тривалість</th>
                  <th className="pr-2">Швидкість</th>
                  <th className="pr-2">Курсор</th>
                  <th className="pr-2">Скановано</th>
                  <th className="pr-2">З відбитком</th>
                  <th className="pr-2">Пар</th>
                  <th className="pr-2">Інстанс</th>
                  <th className="pr-2">Помилка</th>
                </tr>
              </thead>
              <tbody>
                {status.runs.map((r) => {
                  const ms = (r.finishedAt ? new Date(r.finishedAt).getTime() : fetchedAt) - new Date(r.startedAt).getTime()
                  return (
                    <tr key={r.id} className="border-t border-slate-100 dark:border-slate-800">
                      <td className="py-1 pr-2 text-slate-500">{fmtTime(r.startedAt)}</td>
                      <td className="pr-2">
                        <Badge ok={r.status === 'ok' ? true : r.status === 'running' ? null : false} text={r.status} />
                      </td>
                      <td className="pr-2">{fmtDuration(r.startedAt, r.finishedAt, fetchedAt)}</td>
                      <td className="pr-2 font-mono">{fmtRate(r.messagesScanned, ms)}</td>
                      <td className="pr-2 font-mono">
                        {fmtInt(r.watermarkFrom)} → {fmtInt(r.watermarkTo)}
                      </td>
                      <td className="pr-2">{fmtInt(r.messagesScanned)}</td>
                      <td className="pr-2">{fmtInt(r.messagesFingerprinted)}</td>
                      <td className="pr-2">{fmtInt(r.pairsFound)}</td>
                      <td className="pr-2 text-slate-500">{r.instance}</td>
                      <td className="max-w-xs truncate pr-2 text-red-600" title={r.error}>
                        {r.error ?? ''}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </Section>
      )}

      {status && status.migrations.length > 0 && (
        <Section title="Міграції схеми analytics" badge={<Badge ok={null} text={`${status.migrations.length}`} />}>
          <ul className="font-mono text-xs text-slate-600 dark:text-slate-300">
            {status.migrations.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        </Section>
      )}
    </>
  )
}
