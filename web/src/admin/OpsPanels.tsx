import { useCallback, useEffect, useMemo, useState } from 'react'
import { admin, type CollectorStatusDto, type DbReportDto, type LogFileDto, type LogTailDto, type OpsOverviewDto } from '../api/admin'
import { Badge, Section } from '../components/settings/fields'
import { Bars, Stat, ago, fmtBytes, fmtNum, fmtTime, usePolled } from './shared'

const SERVICE_LABEL: Record<string, string> = {
  worker: 'Worker (збір і обробка)',
  'worker:worker': 'Worker (усе в одному процесі)',
  'worker:processor': 'Processor (обробка повідомлень)',
  'worker:collector-telegram': 'Колектор Telegram',
  'worker:collector-alerts': 'Колектор alerts.in.ua',
  'worker:analytics': 'Analytics (аналітика джерел)',
  api: 'Api (карта, публічна частина)',
  admin: 'Admin (ця панель)',
  collectors: 'Колектори джерел',
  telegram: 'Telegram-сесія',
  postgres: 'PostgreSQL / PostGIS',
}

/** Scaled processor replicas are `worker:processor-<container id>`: labelled by the prefix, the id kept as detail. */
function serviceLabel(name: string): string {
  if (SERVICE_LABEL[name]) return SERVICE_LABEL[name]
  const m = /^worker:(processor|collector-telegram|collector-alerts|analytics|worker)-(.+)$/.exec(name)
  return m ? `${SERVICE_LABEL[`worker:${m[1]}`]} · ${m[2]}` : name
}

function statusOk(status: string): boolean | null {
  return status === 'ok' ? true : status === 'unknown' ? null : false
}

export function OverviewPanel() {
  const { data, error } = usePolled(() => admin.ops.overview(), 10_000)
  return (
    <Section title="Стан компонентів" badge={data && <Badge ok={data.services.every((s) => s.status === 'ok') ? true : data.services.some((s) => s.status === 'down') ? false : null} text={`оновлено ${fmtTime(data.generatedAt)}`} />}>
      {error && <div className="text-xs text-red-600">{error}</div>}
      {!data && !error && <div className="text-xs text-slate-500">Завантаження…</div>}
      {data && (
        <>
          <table className="w-full text-xs">
            <thead className="text-left text-slate-500">
              <tr>
                <th className="py-1 pr-2">Компонент</th>
                <th className="pr-2">Стан</th>
                <th className="pr-2">Деталі</th>
                <th className="pr-2">Востаннє</th>
              </tr>
            </thead>
            <tbody>
              <tr className="border-t border-slate-100 dark:border-slate-800">
                <td className="py-1.5 pr-2">Процесори повідомлень</td>
                <td className="pr-2">
                  <Badge ok={data.processorCount > 0 ? true : false} text={data.processorCount > 0 ? 'ok' : 'down'} />
                </td>
                <td className="pr-2 text-slate-600 dark:text-slate-300">
                  {fmtNum(data.processorCount)} {plural(data.processorCount, 'репліка', 'репліки', 'реплік')} з живим heartbeat ·{' '}
                  <a className="underline" href="#/workers">
                    Воркери
                  </a>
                </td>
                <td className="pr-2 text-slate-500">—</td>
              </tr>
              {data.services.map((s) => (
                <tr key={s.name} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1.5 pr-2">{serviceLabel(s.name)}</td>
                  <td className="pr-2">
                    <Badge ok={statusOk(s.status)} text={s.status} />
                  </td>
                  <td className="pr-2 text-slate-600 dark:text-slate-300">{s.detail ?? '—'}</td>
                  <td className="pr-2 text-slate-500" title={s.lastSeen ? fmtTime(s.lastSeen) : undefined}>
                    {ago(s.lastSeen)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
            <Stat label="Розмір БД" value={fmtBytes(data.db.sizeBytes)} />
            <Stat label="З’єднань" value={String(data.db.connections)} />
            <Stat label="Міграцій" value={String(data.db.migrationCount)} />
            <Stat label="Остання міграція" value={data.db.lastMigration?.replace(/^\d+_/, '') ?? '—'} />
          </div>
        </>
      )}
    </Section>
  )
}

function plural(n: number, one: string, few: string, many: string): string {
  const m10 = n % 10
  const m100 = n % 100
  if (m10 === 1 && m100 !== 11) return one
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few
  return many
}

export function CollectorsPanel() {
  const { data, error } = usePolled(() => admin.ops.collectors(), 15_000)
  const list: CollectorStatusDto[] = data ?? []
  return (
    <Section title="Колектори" badge={<Badge ok={list.length ? list.filter((c) => c.enabled).every((c) => c.consecutiveFailures === 0) : null} text={`${list.filter((c) => c.enabled).length} увімкнених`} />}>
      <p className="text-xs text-slate-500">Кожне джерело окремо: коли опитано, коли останній успіх і повідомлення, помилки поспіль, і скільки повідомлень прийшло за кожну з останніх 24 годин.</p>
      {error && <div className="text-xs text-red-600">{error}</div>}
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-left text-slate-500">
            <tr>
              <th className="py-1 pr-2">Джерело</th>
              <th className="pr-2">Тип</th>
              <th className="pr-2">Стан</th>
              <th className="pr-2">Опитано</th>
              <th className="pr-2">Успіх</th>
              <th className="pr-2">Повідомлення</th>
              <th className="pr-2">За 24 год</th>
              <th className="pr-2">Помилка</th>
            </tr>
          </thead>
          <tbody>
            {list.map((c) => (
              <tr key={c.sourceId} className={`border-t border-slate-100 dark:border-slate-800 ${c.enabled ? '' : 'opacity-50'}`}>
                <td className="py-1.5 pr-2">
                  {c.name} <span className="text-slate-400">{c.code}</span>
                </td>
                <td className="pr-2">{c.type}</td>
                <td className="pr-2">
                  <Badge ok={!c.enabled ? null : c.consecutiveFailures > 0 ? false : c.lastSuccessAt ? true : null} text={!c.enabled ? 'вимкнено' : c.consecutiveFailures > 0 ? `${c.consecutiveFailures} помилок поспіль` : c.lastSuccessAt ? 'ok' : 'ще не опитано'} />
                </td>
                <td className="pr-2 text-slate-500">{ago(c.lastPolledAt)}</td>
                <td className="pr-2 text-slate-500">{ago(c.lastSuccessAt)}</td>
                <td className="pr-2 text-slate-500">{ago(c.lastMessageAt)}</td>
                <td className="pr-2">
                  <div className="flex items-center gap-2">
                    <Bars values={c.perHour} title={(i, v) => `${23 - i} год тому: ${v}`} />
                    <span className="font-mono">{fmtNum(c.messages24h)}</span>
                  </div>
                </td>
                <td className="max-w-xs truncate pr-2 text-red-600" title={c.lastError}>
                  {c.lastError ?? ''}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Section>
  )
}

export function DbPanel() {
  const { data, error } = usePolled(() => admin.ops.db(), 30_000)
  const d: DbReportDto | null = data
  return (
    <>
      <Section title="База даних" badge={d && <Badge ok={true} text={fmtBytes(d.sizeBytes)} />}>
        {error && <div className="text-xs text-red-600">{error}</div>}
        {d && (
          <>
            <div className="text-xs text-slate-500">{d.version}</div>
            <div className="flex flex-wrap gap-2 text-xs">
              {d.connections.map((c) => (
                <span key={c.role} className="rounded bg-slate-100 px-2 py-0.5 dark:bg-slate-800">
                  {c.role}: {c.connections} з’єдн.
                </span>
              ))}
            </div>
            <table className="w-full text-xs">
              <thead className="text-left text-slate-500">
                <tr>
                  <th className="py-1 pr-2">Таблиця</th>
                  <th className="pr-2 text-right">Рядків</th>
                  <th className="pr-2 text-right">Розмір</th>
                </tr>
              </thead>
              <tbody>
                {d.tables.map((t) => (
                  <tr key={t.name} className="border-t border-slate-100 dark:border-slate-800">
                    <td className="py-1 pr-2 font-mono">{t.name}</td>
                    <td className="pr-2 text-right font-mono">{t.rows.toLocaleString('uk-UA')}</td>
                    <td className="pr-2 text-right font-mono">{fmtBytes(t.bytes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </Section>
      {d && (
        <Section title="Міграції" badge={<Badge ok={null} text={`${d.migrations.length}`} />}>
          <ol className="list-inside list-decimal text-xs text-slate-600 dark:text-slate-300">
            {d.migrations.map((m) => (
              <li key={m} className="font-mono">
                {m}
              </li>
            ))}
          </ol>
        </Section>
      )}
    </>
  )
}

const LEVELS = ['', 'Warning', 'Error']

export function LogsPanel() {
  const [files, setFiles] = useState<LogFileDto[]>([])
  const [file, setFile] = useState<string>('')
  const [filter, setFilter] = useState('')
  const [level, setLevel] = useState('')
  const [lines, setLines] = useState(200)
  const [tail, setTail] = useState<LogTailDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [auto, setAuto] = useState(true)

  useEffect(() => {
    admin.ops
      .logFiles()
      .then((f) => {
        setFiles(f)
        setFile((cur) => cur || f[0]?.name || '')
      })
      .catch((e: Error) => setError(e.message))
  }, [])

  const load = useCallback(() => {
    if (!file) return
    admin.ops
      .logTail(file, lines, filter, level)
      .then((t) => {
        setTail(t)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
  }, [file, lines, filter, level])

  useEffect(() => {
    load()
    if (!auto) return
    const id = window.setInterval(load, 5_000)
    return () => window.clearInterval(id)
  }, [load, auto])

  const byService = useMemo(() => {
    const m = new Map<string, LogFileDto[]>()
    for (const f of files) m.set(f.service, [...(m.get(f.service) ?? []), f])
    return m
  }, [files])

  return (
    <Section title="Логи" badge={tail && <Badge ok={null} text={`${tail.lines.length} рядків · ${fmtBytes(tail.bytes)}`} />}>
      <p className="text-xs text-slate-500">Кожен сервіс пише свій файл на день у спільну теку logs/. Тут — хвіст файлу; фільтр шукає підрядок у записі, рівень — за позначкою [WRN] / [ERR].</p>
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <select className="rounded border border-slate-300 bg-white px-1 py-0.5 dark:border-slate-600 dark:bg-slate-800" value={file} onChange={(e) => setFile(e.target.value)}>
          {[...byService.entries()].map(([svc, fs]) => (
            <optgroup key={svc} label={svc}>
              {fs.map((f) => (
                <option key={f.name} value={f.name}>
                  {f.name} · {fmtBytes(f.bytes)}
                </option>
              ))}
            </optgroup>
          ))}
          {files.length === 0 && <option value="">(файлів немає)</option>}
        </select>
        <input className="w-48 rounded border border-slate-300 px-2 py-0.5 dark:border-slate-600 dark:bg-slate-800" placeholder="фільтр…" value={filter} onChange={(e) => setFilter(e.target.value)} />
        <select className="rounded border border-slate-300 bg-white px-1 py-0.5 dark:border-slate-600 dark:bg-slate-800" value={level} onChange={(e) => setLevel(e.target.value)}>
          {LEVELS.map((l) => (
            <option key={l} value={l}>
              {l || 'усі рівні'}
            </option>
          ))}
        </select>
        <select className="rounded border border-slate-300 bg-white px-1 py-0.5 dark:border-slate-600 dark:bg-slate-800" value={lines} onChange={(e) => setLines(Number(e.target.value))}>
          {[100, 200, 500, 1000, 2000].map((n) => (
            <option key={n} value={n}>
              {n} рядків
            </option>
          ))}
        </select>
        <label className="flex items-center gap-1">
          <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} /> авто-оновлення
        </label>
        <button className="rounded bg-slate-200 px-2 py-0.5 dark:bg-slate-700" onClick={load}>
          Оновити
        </button>
      </div>
      {error && <div className="text-xs text-red-600">{error}</div>}
      <pre className="max-h-[70vh] overflow-auto rounded-lg bg-slate-900 p-3 text-[11px] leading-snug text-slate-100">
        {tail?.lines.map((l, i) => (
          <div key={i} className={l.includes('[ERR]') || l.includes('[FTL]') ? 'text-red-300' : l.includes('[WRN]') ? 'text-amber-200' : l.includes('[DBG]') || l.includes('[VRB]') ? 'text-slate-400' : ''}>
            {l}
          </div>
        ))}
        {tail && tail.lines.length === 0 && <span className="text-slate-400">порожньо</span>}
      </pre>
    </Section>
  )
}

export type { OpsOverviewDto }
