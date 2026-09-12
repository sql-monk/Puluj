import { useState } from 'react'
import { admin, type AdminSourceDto, type SourcePatch } from '../../api/admin'
import { Badge, Section } from './fields'

interface Props {
  sources: AdminSourceDto[]
  reload: () => Promise<void>
  notify: (m: { ok: boolean; text: string }) => void
}

const statusLabel: Record<AdminSourceDto['status'], string> = { ok: 'працює', stale: 'немає даних', idle: 'очікує', disabled: 'вимкнено' }
const typeLabel: Record<string, string> = { Telegram: 'Telegram', RestApi: 'REST API', Rss: 'RSS', Web: 'Web' }

/** Table of sources with inline editing (name, channel, trust, priority, polling, home region), enable/disable, add and delete. */
export default function SourcesEditor({ sources, reload, notify }: Props) {
  const [editing, setEditing] = useState<number | null>(null)
  const [form, setForm] = useState<SourcePatch>({})
  const [adding, setAdding] = useState(false)
  // Token is sent only when typed: an untouched field keeps the stored one.
  const [tokenDraft, setTokenDraft] = useState('')
  const [add, setAdd] = useState({ type: 'Telegram', channel: '', name: '', url: '', trustLevel: '0.6', priority: '50', polling: '' })

  const startEdit = (s: AdminSourceDto) => {
    setEditing(s.id)
    setForm({ name: s.name, channel: s.channel ?? '', url: s.url ?? '', trustLevel: s.trustLevel, priority: s.priority, pollingIntervalSeconds: s.pollingIntervalSeconds ?? 0, homeRegion: s.homeRegion ?? '' })
    setTokenDraft('')
  }

  const run = async (fn: () => Promise<unknown>, okText?: string) => {
    try {
      await fn()
      await reload()
      if (okText) notify({ ok: true, text: okText })
    } catch (e) {
      notify({ ok: false, text: (e as Error).message })
    }
  }

  const saveEdit = () => run(() => admin.updateSource(editing!, tokenDraft ? { ...form, token: tokenDraft } : form), 'Джерело оновлено').then(() => setEditing(null))

  const create = () =>
    run(
      () =>
        admin.createSource({
          type: add.type,
          name: add.name,
          channel: add.type === 'Telegram' ? add.channel : undefined,
          url: add.url || undefined,
          trustLevel: Number(add.trustLevel) || 0.6,
          priority: Number(add.priority) || 50,
          pollingIntervalSeconds: Number(add.polling) || undefined,
        }),
      'Джерело додано; Worker підхопить його за кілька секунд.',
    ).then(() => {
      setAdding(false)
      setAdd({ type: 'Telegram', channel: '', name: '', url: '', trustLevel: '0.6', priority: '50', polling: '' })
    })

  const input = 'w-full rounded border border-slate-300 px-2 py-1 dark:border-slate-600 dark:bg-slate-800'

  return (
    <Section
      title="Джерела"
      badge={
        <span className="flex items-center gap-2">
          <Badge ok={null} text={`${sources.filter((x) => x.enabled).length} з ${sources.length} увімкнено`} />
          <button className="rounded bg-slate-700 px-2 py-0.5 text-xs text-white dark:bg-slate-200 dark:text-slate-900" onClick={() => setAdding((v) => !v)}>
            {adding ? 'Сховати форму' : '+ Додати'}
          </button>
        </span>
      }
    >
      <p className="text-xs text-slate-500">
        Довіра (0–1) обмежує максимальну впевненість фактів із джерела; пріоритет впливає лише на порядок. Джерело, в якого вже є збережені повідомлення, можна тільки вимкнути — воно частина ланцюжка походження.
        Усе, включно з токенами API, зберігається в базі; <code>data/sources.json</code> лише додає джерела, яких ще немає.
      </p>

      {adding && (
        <div className="grid gap-2 rounded border border-dashed border-slate-300 p-3 text-xs sm:grid-cols-3 dark:border-slate-600">
          <label>
            <span className="block text-slate-500">Тип</span>
            <select className={input} value={add.type} onChange={(e) => setAdd({ ...add, type: e.target.value })}>
              <option value="Telegram">Telegram-канал</option>
              <option value="Rss">RSS (колектор ще не реалізовано)</option>
              <option value="Web">Web (колектор ще не реалізовано)</option>
            </select>
          </label>
          {add.type === 'Telegram' ? (
            <label>
              <span className="block text-slate-500">Username каналу</span>
              <input className={input} placeholder="kpszsu" value={add.channel} onChange={(e) => setAdd({ ...add, channel: e.target.value })} />
            </label>
          ) : (
            <label>
              <span className="block text-slate-500">URL</span>
              <input className={input} value={add.url} onChange={(e) => setAdd({ ...add, url: e.target.value })} />
            </label>
          )}
          <label>
            <span className="block text-slate-500">Назва</span>
            <input className={input} value={add.name} onChange={(e) => setAdd({ ...add, name: e.target.value })} />
          </label>
          <label>
            <span className="block text-slate-500">Довіра 0–1</span>
            <input className={input} value={add.trustLevel} onChange={(e) => setAdd({ ...add, trustLevel: e.target.value })} />
          </label>
          <label>
            <span className="block text-slate-500">Пріоритет</span>
            <input className={input} value={add.priority} onChange={(e) => setAdd({ ...add, priority: e.target.value })} />
          </label>
          <label>
            <span className="block text-slate-500">Опитування, с (для REST/RSS)</span>
            <input className={input} value={add.polling} onChange={(e) => setAdd({ ...add, polling: e.target.value })} />
          </label>
          <div className="sm:col-span-3">
            <button className="rounded bg-slate-800 px-3 py-1 text-white dark:bg-slate-100 dark:text-slate-900" onClick={() => void create()} disabled={add.type === 'Telegram' ? !add.channel.trim() : !add.url.trim()}>
              Створити
            </button>
          </div>
        </div>
      )}

      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-left text-slate-500">
            <tr>
              <th className="py-1 pr-2">Джерело</th>
              <th className="pr-2">Тип</th>
              <th className="pr-2">Довіра</th>
              <th className="pr-2">Пріор.</th>
              <th className="pr-2">Стан</th>
              <th className="pr-2">Повід.</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {sources.map((s) =>
              editing === s.id ? (
                <tr key={s.id} className="border-t border-slate-100 bg-slate-50 dark:border-slate-800 dark:bg-slate-800/50">
                  <td colSpan={7} className="p-2">
                    <div className="grid gap-2 sm:grid-cols-3">
                      <label>
                        <span className="block text-slate-500">Назва</span>
                        <input className={input} value={form.name ?? ''} onChange={(e) => setForm({ ...form, name: e.target.value })} />
                      </label>
                      {s.type === 'Telegram' ? (
                        <label>
                          <span className="block text-slate-500">Username каналу</span>
                          <input className={input} value={form.channel ?? ''} onChange={(e) => setForm({ ...form, channel: e.target.value })} />
                        </label>
                      ) : (
                        <label>
                          <span className="block text-slate-500">URL</span>
                          <input className={input} value={form.url ?? ''} onChange={(e) => setForm({ ...form, url: e.target.value })} />
                        </label>
                      )}
                      <label>
                        <span className="block text-slate-500">Довіра 0–1</span>
                        <input className={input} type="number" step="0.05" min="0" max="1" value={form.trustLevel ?? 0} onChange={(e) => setForm({ ...form, trustLevel: Number(e.target.value) })} />
                      </label>
                      <label>
                        <span className="block text-slate-500">Пріоритет</span>
                        <input className={input} type="number" value={form.priority ?? 0} onChange={(e) => setForm({ ...form, priority: Number(e.target.value) })} />
                      </label>
                      <label>
                        <span className="block text-slate-500">Опитування, с (0 = типово)</span>
                        <input className={input} type="number" value={form.pollingIntervalSeconds ?? 0} onChange={(e) => setForm({ ...form, pollingIntervalSeconds: Number(e.target.value) })} />
                      </label>
                      <label>
                        <span className="block text-slate-500">Домашній регіон (для ОВА: «Київська область»)</span>
                        <input className={input} value={form.homeRegion ?? ''} onChange={(e) => setForm({ ...form, homeRegion: e.target.value })} />
                      </label>
                      {s.type !== 'Telegram' && (
                        <label>
                          <span className="block text-slate-500">Токен API {s.hasToken ? '(збережено — введіть, щоб замінити)' : '(не задано)'}</span>
                          <input className={input} type="password" autoComplete="off" value={tokenDraft} onChange={(e) => setTokenDraft(e.target.value)} placeholder={s.hasToken ? '••••••••' : ''} />
                          {s.hasToken && (
                            <button className="mt-1 text-[11px] text-red-600 underline" onClick={() => void run(() => admin.updateSource(s.id, { token: '' }), 'Токен видалено')}>
                              прибрати токен
                            </button>
                          )}
                        </label>
                      )}
                    </div>
                    <div className="mt-2 flex gap-2">
                      <button className="rounded bg-slate-800 px-3 py-1 text-white dark:bg-slate-100 dark:text-slate-900" onClick={() => void saveEdit()}>
                        Зберегти
                      </button>
                      <button className="rounded border border-slate-300 px-3 py-1 dark:border-slate-600" onClick={() => setEditing(null)}>
                        Скасувати
                      </button>
                    </div>
                  </td>
                </tr>
              ) : (
                <tr key={s.id} className={`border-t border-slate-100 dark:border-slate-800 ${s.enabled ? '' : 'opacity-60'}`}>
                  <td className="py-1.5 pr-2">
                    <div className="font-medium">{s.name}</div>
                    <div className="text-slate-400">
                      {s.channel ? `@${s.channel}` : (s.url ?? s.code)}
                      {s.homeRegion && ` · ${s.homeRegion}`}
                      {s.type !== 'Telegram' && (s.hasToken ? ' · токен ✓' : ' · без токена')}
                    </div>
                  </td>
                  <td className="pr-2">{typeLabel[s.type] ?? s.type}</td>
                  <td className="pr-2">{Math.round(s.trustLevel * 100)}%</td>
                  <td className="pr-2">{s.priority}</td>
                  <td className="pr-2">
                    <Badge ok={s.status === 'ok' ? true : s.status === 'stale' ? false : null} text={statusLabel[s.status]} />
                    {s.lastMessageAt && <div className="text-slate-400">останнє {new Date(s.lastMessageAt).toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}</div>}
                    {s.lastError && (
                      <div className="max-w-40 truncate text-red-500" title={s.lastError}>
                        {s.lastError}
                      </div>
                    )}
                  </td>
                  <td className="pr-2">{s.rawMessageCount}</td>
                  <td className="whitespace-nowrap text-right">
                    <button className="mr-1 rounded border border-slate-300 px-2 py-0.5 dark:border-slate-600" onClick={() => startEdit(s)}>
                      Редагувати
                    </button>
                    <button className="mr-1 rounded border border-slate-300 px-2 py-0.5 dark:border-slate-600" onClick={() => void run(() => admin.updateSource(s.id, { enabled: !s.enabled }))}>
                      {s.enabled ? 'Вимкнути' : 'Увімкнути'}
                    </button>
                    {s.rawMessageCount === 0 && (
                      <button className="rounded border border-red-300 px-2 py-0.5 text-red-600 dark:border-red-800" onClick={() => void run(() => admin.deleteSource(s.id), 'Джерело видалено')}>
                        Видалити
                      </button>
                    )}
                  </td>
                </tr>
              ),
            )}
          </tbody>
        </table>
      </div>
    </Section>
  )
}
