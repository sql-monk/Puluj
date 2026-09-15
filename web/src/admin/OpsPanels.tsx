import { useCallback, useEffect, useMemo, useState } from 'react'
import { admin, type CollectorStatusDto, type DbReportDto, type LogFileDto, type LogTailDto, type OpsOverviewDto, type ProcessingReportDto } from '../api/admin'
import { Badge, Section } from '../components/settings/fields'

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

function fmtBytes(b: number): string {
  if (b < 1024) return `${b} Б`
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(0)} КБ`
  if (b < 1024 * 1024 * 1024) return `${(b / 1024 / 1024).toFixed(1)} МБ`
  return `${(b / 1024 / 1024 / 1024).toFixed(2)} ГБ`
}

function fmtTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return d.toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function ago(iso?: string): string {
  if (!iso) return '—'
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (s < 90) return `${Math.round(s)} с тому`
  if (s < 5400) return `${Math.round(s / 60)} хв тому`
  if (s < 172800) return `${(s / 3600).toFixed(1)} год тому`
  return `${Math.round(s / 86400)} д тому`
}

function statusOk(status: string): boolean | null {
  return status === 'ok' ? true : status === 'unknown' ? null : false
}

/** Poll a loader every `intervalMs`; the error is shown instead of stale data. */
function usePolled<T>(load: () => Promise<T>, intervalMs: number, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const run = useCallback(() => {
    load()
      .then((d) => {
        setData(d)
        setError(null)
      })
      .catch((e: Error) => setError(e.message))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)
  useEffect(() => {
    run()
    const id = window.setInterval(run, intervalMs)
    return () => window.clearInterval(id)
  }, [run, intervalMs])
  return { data, error, reload: run }
}

/** Tiny inline bar chart: one bar per hour, oldest first. */
function Bars({ values, color = 'bg-sky-500', height = 28, title }: { values: number[]; color?: string; height?: number; title?: (i: number, v: number) => string }) {
  const max = Math.max(1, ...values)
  return (
    <div className="flex items-end gap-px" style={{ height }} aria-hidden>
      {values.map((v, i) => (
        <div key={i} className={`w-1.5 rounded-sm ${v > 0 ? color : 'bg-slate-200 dark:bg-slate-700'}`} style={{ height: `${Math.max(2, (v / max) * 100)}%` }} title={title ? title(i, v) : String(v)} />
      ))}
    </div>
  )
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
              {data.services.map((s) => (
                <tr key={s.name} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1.5 pr-2">{SERVICE_LABEL[s.name] ?? s.name}</td>
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

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg bg-slate-50 px-3 py-2 dark:bg-slate-800/60">
      <div className="text-[10px] uppercase tracking-wide text-slate-500">{label}</div>
      <div className="truncate font-mono" title={value}>
        {value}
      </div>
    </div>
  )
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
                    <span className="font-mono">{c.messages24h}</span>
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

export function ProcessingPanel() {
  const { data, error } = usePolled(() => admin.ops.processing(), 15_000)
  const hours = data?.hours ?? []
  const totals = useMemo(() => {
    const t = { received: 0, processed: 0, targets: 0, links: 0, errors: 0 }
    for (const h of hours) {
      t.received += h.received
      t.processed += h.processed
      t.targets += h.targets
      t.links += h.links
      t.errors += h.errors
    }
    return t
  }, [hours])
  const pending = data ? (data.queue['Pending'] ?? 0) + (data.queue['InProgress'] ?? 0) : 0
  return (
    <>
      <Section title="Обробка повідомлень" badge={data && <Badge ok={totals.errors === 0 ? true : totals.errors < 20 ? null : false} text={`${totals.errors} помилок за 24 год`} />}>
        <p className="text-xs text-slate-500">Що робить Worker: скільки повідомлень прийшло і скільки оброблено за годину, скільки цілей і зв’язків з них вийшло, скільки помилок.</p>
        {error && <div className="text-xs text-red-600">{error}</div>}
        {data && (
          <>
            <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4 lg:grid-cols-7">
              <Stat label="Прийнято / 24 год" value={String(totals.received)} />
              <Stat label="Оброблено / 24 год" value={String(totals.processed)} />
              <Stat label="Цілей / 24 год" value={String(data.targets24h)} />
              <Stat label="з них дублікатів" value={String(data.duplicates24h)} />
              <Stat label="Зв’язків / 24 год" value={String(data.links24h)} />
              <Stat label="У черзі" value={String(pending)} />
              <Stat label="Статуси" value={Object.entries(data.queue).map(([k, v]) => `${k} ${v}`).join(', ') || '—'} />
            </div>
            <table className="w-full text-xs">
              <tbody>
                <HourRow label="Прийнято" values={hours.map((h) => h.received)} color="bg-sky-500" />
                <HourRow label="Оброблено" values={hours.map((h) => h.processed)} color="bg-emerald-500" />
                <HourRow label="Цілей" values={hours.map((h) => h.targets)} color="bg-amber-500" />
                <HourRow label="Зв’язків" values={hours.map((h) => h.links)} color="bg-violet-500" />
                <HourRow label="Помилок" values={hours.map((h) => h.errors)} color="bg-red-500" />
              </tbody>
            </table>
            {Object.keys(data.errorsByStage24h).length > 0 && (
              <div className="flex flex-wrap gap-2 text-xs">
                {Object.entries(data.errorsByStage24h).map(([stage, n]) => (
                  <span key={stage} className="rounded bg-red-100 px-2 py-0.5 text-red-900 dark:bg-red-900/40 dark:text-red-100">
                    {stage}: {n}
                  </span>
                ))}
              </div>
            )}
          </>
        )}
      </Section>
      {data && (
        <Section title="Останні помилки" badge={<Badge ok={data.recentErrors.length === 0 ? true : null} text={`${data.recentErrors.length}`} />}>
          <ErrorList report={data} />
        </Section>
      )}
    </>
  )
}

function HourRow({ label, values, color }: { label: string; values: number[]; color: string }) {
  return (
    <tr className="border-t border-slate-100 dark:border-slate-800">
      <td className="w-24 py-1 pr-2 text-slate-500">{label}</td>
      <td className="py-1">
        <div className="flex items-center gap-2">
          <Bars values={values} color={color} height={32} title={(i, v) => `${23 - i} год тому: ${v}`} />
          <span className="font-mono text-slate-500">{values.reduce((a, b) => a + b, 0)}</span>
        </div>
      </td>
    </tr>
  )
}

function ErrorList({ report }: { report: ProcessingReportDto }) {
  const [open, setOpen] = useState<number | null>(null)
  if (report.recentErrors.length === 0) return <div className="text-xs text-slate-500">Помилок не зафіксовано.</div>
  return (
    <table className="w-full text-xs">
      <thead className="text-left text-slate-500">
        <tr>
          <th className="py-1 pr-2">Коли</th>
          <th className="pr-2">Етап</th>
          <th className="pr-2">Повідомлення</th>
          <th className="pr-2">Джерело</th>
        </tr>
      </thead>
      <tbody>
        {report.recentErrors.map((e) => (
          <tr key={e.id} className="cursor-pointer border-t border-slate-100 align-top hover:bg-slate-50 dark:border-slate-800 dark:hover:bg-slate-800/50" onClick={() => setOpen(open === e.id ? null : e.id)}>
            <td className="whitespace-nowrap py-1 pr-2 text-slate-500">{fmtTime(e.occurredAt)}</td>
            <td className="pr-2">{e.stage}</td>
            <td className="pr-2">
              <div className={open === e.id ? 'whitespace-pre-wrap break-all font-mono' : 'max-w-xl truncate'}>{open === e.id && e.exception ? `${e.message}\n\n${e.exception}` : e.message}</div>
            </td>
            <td className="pr-2 text-slate-500">
              {e.sourceId ?? '—'}
              {e.rawMessageId ? ` · msg ${e.rawMessageId}` : ''}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
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
