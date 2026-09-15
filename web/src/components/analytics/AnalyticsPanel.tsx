import { useCallback, useEffect, useMemo, useState } from 'react'
import { analytics, type AnalyticsReportDto, type AnalyticsStatusDto, type CopyPairDto, type RecentCopyDto, type SourceAnalyticsDto } from '../../api/analytics'
import { Badge, Section } from '../settings/fields'

const HEARTBEAT_STALE_MS = 90_000

function fmtTime(iso?: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function ago(iso?: string): string {
  if (!iso) return '—'
  const s = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000)
  if (s < 90) return `${Math.round(s)} с тому`
  if (s < 5400) return `${Math.round(s / 60)} хв тому`
  if (s < 172800) return `${(s / 3600).toFixed(1)} год тому`
  return `${Math.round(s / 86400)} д тому`
}

function fmtDelay(s?: number | null): string {
  if (s === undefined || s === null) return '—'
  if (s < 90) return `${Math.round(s)} с`
  if (s < 5400) return `${Math.round(s / 60)} хв`
  return `${(s / 3600).toFixed(1)} год`
}

function fmtDuration(from: string, to?: string): string {
  const ms = (to ? new Date(to).getTime() : Date.now()) - new Date(from).getTime()
  return ms < 1000 ? `${ms} мс` : ms < 90_000 ? `${(ms / 1000).toFixed(1)} с` : `${Math.round(ms / 60_000)} хв`
}

function fmtBytes(b: number): string {
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(0)} КБ`
  if (b < 1024 * 1024 * 1024) return `${(b / 1024 / 1024).toFixed(1)} МБ`
  return `${(b / 1024 / 1024 / 1024).toFixed(2)} ГБ`
}

function pct(v?: number | null): string {
  return v === undefined || v === null ? '—' : `${Math.round(v * 100)}%`
}

/** Same as the ops panels' inline bar chart; copied rather than shared so this page has no dependency on them. */
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

/**
 * The source analytics page: what the Puluj.Analytics service found in the message texts (who copies whom, how fast,
 * how literally; forwards; activity by hour; who reports tracks first) and how the service itself is doing (last run,
 * backlog, errors). Distinct from the "Рейтинг джерел" tab, which counts repeated *facts* from the parser: this one
 * compares the *texts*, parser or no parser.
 */
export default function AnalyticsPanel() {
  const [days, setDays] = useState(14)
  // The clock is read when the poll answers, so liveness does not depend on when the page re-renders.
  const status = usePolled(() => analytics.status().then((s): PolledStatus => ({ ...s, fetchedAt: Date.now() })), 10_000)
  const report = usePolled(() => analytics.report(days), 60_000, [days])
  const [resetting, setResetting] = useState(false)
  const [confirmReset, setConfirmReset] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const reset = async () => {
    setResetting(true)
    try {
      await analytics.reset()
      setMessage('Індекс очищено; сервіс перебудує його на наступному циклі (усі повідомлення — приблизно 20 хвилин).')
      status.reload()
      report.reload()
    } catch (e) {
      setMessage((e as Error).message)
    } finally {
      setResetting(false)
      setConfirmReset(false)
    }
  }

  const s = status.data
  return (
    <>
      <StatusSection status={s} error={status.error} onReset={() => setConfirmReset(true)} />
      {confirmReset && (
        <div className="rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm dark:border-amber-700 dark:bg-amber-900/30">
          <p>Видалити індекс, знайдені пари і статистику треків? Сервіс перебудує все з нуля з поточними порогами; поки триває перебудова, звіт неповний.</p>
          <div className="mt-2 flex gap-2">
            <button className="rounded bg-red-600 px-3 py-1 text-xs text-white disabled:opacity-50" onClick={() => void reset()} disabled={resetting}>
              {resetting ? 'Очищую…' : 'Так, перебудувати'}
            </button>
            <button className="rounded border border-slate-300 px-3 py-1 text-xs dark:border-slate-600" onClick={() => setConfirmReset(false)}>
              Скасувати
            </button>
          </div>
        </div>
      )}
      {message && <div className="text-xs text-slate-600 dark:text-slate-300">{message}</div>}
      {s && !s.initialized ? (
        <Section title="Джерела">
          <p className="text-xs text-slate-500">Сервіс аналітики ще не створив свою схему в базі — запустіть контейнер `analytics` (або `Puluj.Analytics.Worker` локально). Сторінка оживе після першого прогону.</p>
        </Section>
      ) : (
        <ReportSections report={report.data} error={report.error} days={days} setDays={setDays} />
      )}
    </>
  )
}

type PolledStatus = AnalyticsStatusDto & { fetchedAt: number }

function StatusSection({ status, error, onReset }: { status: PolledStatus | null; error: string | null; onReset: () => void }) {
  const alive = !!status?.heartbeatAt && status.fetchedAt - new Date(status.heartbeatAt).getTime() < HEARTBEAT_STALE_MS
  const last = status?.lastRun
  const badge = !status ? null : !status.initialized ? { ok: false as boolean | null, text: 'ще не запускався' } : !alive ? { ok: false as boolean | null, text: 'heartbeat застарів' } : last?.status === 'failed' ? { ok: false as boolean | null, text: 'останній прогін з помилкою' } : status.backlog > 5000 ? { ok: null, text: `наздоганяє: ${status.backlog} повідомлень` } : { ok: true as boolean | null, text: 'працює' }
  return (
    <Section title="Сервіс аналітики" badge={badge && <Badge ok={badge.ok} text={badge.text} />}>
      <p className="text-xs text-slate-500">
        Окремий сервіс (контейнер <code>analytics</code>) читає нові повідомлення, нормалізує текст, рахує MinHash-відбиток і шукає серед інших джерел пости з тим самим змістом у вікні ±6 год. Раніший пост — оригінал, пізніший — копія. Пересилання Telegram з каналу, який ми збираємо, позначаються окремо.
      </p>
      {error && <div className="text-xs text-red-600">{error}</div>}
      {status && (
        <div className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
          <Stat label="Heartbeat" value={ago(status.heartbeatAt)} title={fmtTime(status.heartbeatAt)} />
          <Stat label="Відставання" value={`${status.backlog} повідомл.`} title={`курсор ${status.watermark} з ${status.latestRawMessageId}`} />
          <Stat label="Проіндексовано" value={`${status.messagesIndexed} (${status.messagesFingerprinted} з відбитком)`} />
          <Stat label="Пар знайдено" value={String(status.pairsTotal)} />
          <Stat label="Останній прогін" value={last ? `${ago(last.startedAt)} · ${last.status}` : '—'} title={last ? fmtTime(last.startedAt) : undefined} />
          <Stat label="Тривалість" value={last ? fmtDuration(last.startedAt, last.finishedAt) : '—'} />
          <Stat label="За прогін" value={last ? `${last.messagesScanned} повідомл., ${last.pairsFound} пар` : '—'} />
          <Stat label="Схема analytics" value={`${fmtBytes(status.schemaBytes)} · ${status.migrations.length} мігр.`} />
        </div>
      )}
      {status && status.runs.length > 0 && (
        <details className="text-xs">
          <summary className="cursor-pointer text-slate-500">Останні прогони ({status.runs.length})</summary>
          <table className="mt-2 w-full">
            <thead className="text-left text-slate-500">
              <tr>
                <th className="py-1 pr-2">Початок</th>
                <th className="pr-2">Стан</th>
                <th className="pr-2">Тривалість</th>
                <th className="pr-2">Курсор</th>
                <th className="pr-2">Скановано</th>
                <th className="pr-2">З відбитком</th>
                <th className="pr-2">Пар</th>
                <th className="pr-2">Помилка</th>
              </tr>
            </thead>
            <tbody>
              {status.runs.map((r) => (
                <tr key={r.id} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1 pr-2 text-slate-500">{fmtTime(r.startedAt)}</td>
                  <td className="pr-2">
                    <Badge ok={r.status === 'ok' ? true : r.status === 'running' ? null : false} text={r.status} />
                  </td>
                  <td className="pr-2">{fmtDuration(r.startedAt, r.finishedAt)}</td>
                  <td className="pr-2 font-mono">
                    {r.watermarkFrom} → {r.watermarkTo}
                  </td>
                  <td className="pr-2">{r.messagesScanned}</td>
                  <td className="pr-2">{r.messagesFingerprinted}</td>
                  <td className="pr-2">{r.pairsFound}</td>
                  <td className="max-w-xs truncate pr-2 text-red-600" title={r.error}>
                    {r.error ?? ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      )}
      {status?.initialized && (
        <div>
          <button className="rounded border border-slate-300 px-3 py-1 text-xs dark:border-slate-600" onClick={onReset}>
            Скинути й перебудувати індекс
          </button>
        </div>
      )}
    </Section>
  )
}

function Stat({ label, value, title }: { label: string; value: string; title?: string }) {
  return (
    <div className="rounded-lg bg-slate-50 px-3 py-2 dark:bg-slate-800/60" title={title}>
      <div className="text-[10px] uppercase tracking-wide text-slate-500">{label}</div>
      <div className="truncate font-mono">{value}</div>
    </div>
  )
}

function ReportSections({ report, error, days, setDays }: { report: AnalyticsReportDto | null; error: string | null; days: number; setDays: (d: number) => void }) {
  const nameOf = useMemo(() => new Map((report?.sources ?? []).map((s) => [s.id, s.name])), [report])
  const active = useMemo(() => (report?.sources ?? []).filter((s) => s.posts > 0), [report])
  const [recentSource, setRecentSource] = useState<number | undefined>(undefined)
  const recent = usePolled(() => analytics.recent(30, recentSource), 60_000, [recentSource])
  const periodPicker = (
    <select className="rounded border border-slate-300 bg-white px-1 py-0.5 text-xs dark:border-slate-600 dark:bg-slate-800" value={days} onChange={(e) => setDays(Number(e.target.value))}>
      {[7, 14, 30, 60].map((d) => (
        <option key={d} value={d}>
          {d} днів
        </option>
      ))}
    </select>
  )
  return (
    <>
      <Section
        title="Джерела"
        badge={
          <span className="flex items-center gap-2">
            {periodPicker}
            <Badge ok={null} text={`${active.length} активних`} />
          </span>
        }
      >
        <p className="text-xs text-slate-500">
          Пости — логічні (редагування одного поста рахується раз). Копія — пост, що повторює раніший пост іншого джерела; «скопійовано» — скільки разів інші повторили це джерело; унікальність = 1 − копії/пости. Затримка — від оригіналу до копії; випередження — на скільки інші відстали від цього джерела. Дослівних — частка копій із майже тотожним текстом або пересланих. Активність — пости за годиною доби (Київ).
        </p>
        {error && <div className="text-xs text-red-600">{error}</div>}
        {!report && !error && <div className="text-xs text-slate-500">Завантаження…</div>}
        {report && (
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead className="text-left text-slate-500">
                <tr>
                  <th className="py-1 pr-2">Джерело</th>
                  <th className="pr-2">Постів</th>
                  <th className="pr-2">Копій</th>
                  <th className="pr-2">Скопійовано</th>
                  <th className="pr-2">Унікальність</th>
                  <th className="pr-2">Затримка (сер./мед.)</th>
                  <th className="pr-2">Випередження</th>
                  <th className="pr-2">Дослівних</th>
                  <th className="pr-2">Пересилань</th>
                  <th className="pr-2">Активність за добу</th>
                </tr>
              </thead>
              <tbody>
                {[...active].sort((a, b) => b.posts - a.posts).map((s) => (
                  <tr key={s.id} className={`border-t border-slate-100 dark:border-slate-800 ${s.enabled ? '' : 'opacity-50'}`}>
                    <td className="py-1.5 pr-2">
                      {s.name} <span className="text-slate-400">{s.code}</span>
                    </td>
                    <td className="pr-2 font-mono">
                      {s.posts}
                      {s.edits > 0 && <span className="text-slate-400"> +{s.edits} ред.</span>}
                    </td>
                    <td className="pr-2">
                      {s.copies} <span className="text-slate-400">({s.posts ? Math.round((s.copies / s.posts) * 100) : 0}%)</span>
                    </td>
                    <td className="pr-2">{s.copiedBy}</td>
                    <td className="pr-2">
                      <UniqueBar value={s.uniqueShare} />
                    </td>
                    <td className="pr-2">
                      {fmtDelay(s.avgCopyDelaySeconds)} <span className="text-slate-400">/ {fmtDelay(s.medianCopyDelaySeconds)}</span>
                    </td>
                    <td className="pr-2">{fmtDelay(s.avgLeadSeconds)}</td>
                    <td className="pr-2">{pct(s.verbatimShare)}</td>
                    <td className="pr-2" title="з наших джерел / із зовнішніх каналів">
                      {s.forwardsInternal} <span className="text-slate-400">/ {s.forwardsExternal}</span>
                    </td>
                    <td className="pr-2">
                      <Bars values={s.perHour} title={(i, v) => `${String(i).padStart(2, '0')}:00 — ${v}`} />
                    </td>
                  </tr>
                ))}
                {active.length === 0 && (
                  <tr>
                    <td colSpan={10} className="py-1 text-slate-500">
                      За період нічого не проіндексовано.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
        {report && active.length > 0 && <DailyChart sources={active} days={report.days} />}
      </Section>

      {report && (
        <Section title="Хто кого копіює" badge={<Badge ok={null} text={`${report.pairs.filter((p) => p.count > 0).length} пар`} />}>
          <p className="text-xs text-slate-500">
            Рядок — хто копіює, стовпець — кого. У клітинці — копії, для яких це джерело було першим; тон — частка від постів копіювальника. У підказці — усі пари з проміжними оригіналами («у кого читає»), середня затримка, дослівні та пересилання.
          </p>
          <Matrix sources={active} pairs={report.pairs} />
          <PairsTable pairs={report.pairs} nameOf={nameOf} />
        </Section>
      )}

      {report && report.firsts.length > 0 && <FirstsSection report={report} nameOf={nameOf} />}

      {report && report.externalForwards.length > 0 && (
        <Section title="Пересилання з-поза системи" badge={<Badge ok={null} text={`${report.externalForwards.length} каналів`} />}>
          <p className="text-xs text-slate-500">Канали, з яких наші джерела пересилають найчастіше, але яких ми не збираємо — кандидати на додавання у «Джерела».</p>
          <table className="w-full text-xs">
            <thead className="text-left text-slate-500">
              <tr>
                <th className="py-1 pr-2">Джерело</th>
                <th className="pr-2">Пересилає з</th>
                <th className="pr-2">Постів</th>
              </tr>
            </thead>
            <tbody>
              {report.externalForwards.map((f) => (
                <tr key={`${f.sourceId}-${f.channelRef}`} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1 pr-2">{nameOf.get(f.sourceId) ?? `#${f.sourceId}`}</td>
                  <td className="pr-2 font-mono">{f.channelRef}</td>
                  <td className="pr-2">{f.count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
      )}

      <Section
        title="Останні знахідки"
        badge={
          <select className="rounded border border-slate-300 bg-white px-1 py-0.5 text-xs dark:border-slate-600 dark:bg-slate-800" value={recentSource ?? ''} onChange={(e) => setRecentSource(e.target.value ? Number(e.target.value) : undefined)}>
            <option value="">усі джерела</option>
            {(report?.sources ?? []).map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        }
      >
        <p className="text-xs text-slate-500">Пари в порядку виявлення, з обома текстами — щоб бачити, що саме алгоритм вважає копією (пороги: Жаккар ≥ 0.7 або вкладення ≥ 0.85 для довгих текстів).</p>
        {recent.error && <div className="text-xs text-red-600">{recent.error}</div>}
        <RecentList items={recent.data ?? []} nameOf={nameOf} />
      </Section>
    </>
  )
}

function UniqueBar({ value }: { value?: number | null }) {
  if (value === undefined || value === null) return <span className="text-slate-400">—</span>
  return (
    <span className="flex items-center gap-1">
      <span className="inline-block h-2 w-16 overflow-hidden rounded bg-slate-200 dark:bg-slate-700">
        <span className="block h-full bg-emerald-500" style={{ width: `${Math.round(value * 100)}%` }} />
      </span>
      <span className="font-mono">{Math.round(value * 100)}%</span>
    </span>
  )
}

const COLORS = ['#2563eb', '#dc2626', '#16a34a', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#4b5563', '#65a30d', '#9333ea']

/** Posts and copies per day for every active source: one line per source, copies as a dashed line of the same color. */
function DailyChart({ sources, days }: { sources: SourceAnalyticsDto[]; days: string[] }) {
  const w = 720
  const h = 140
  const pad = { l: 34, r: 8, t: 8, b: 18 }
  const max = Math.max(1, ...sources.flatMap((s) => s.perDay.map((d) => d.posts)))
  const x = (i: number) => pad.l + (days.length <= 1 ? 0 : (i / (days.length - 1)) * (w - pad.l - pad.r))
  const y = (v: number) => pad.t + (1 - v / max) * (h - pad.t - pad.b)
  const path = (vals: number[]) => vals.map((v, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ')
  return (
    <div className="space-y-1">
      <svg viewBox={`0 0 ${w} ${h}`} className="w-full" role="img" aria-label="Пости й копії за днями">
        {[0, 0.5, 1].map((f) => (
          <g key={f}>
            <line x1={pad.l} x2={w - pad.r} y1={y(max * f)} y2={y(max * f)} className="stroke-slate-200 dark:stroke-slate-700" strokeWidth={1} />
            <text x={pad.l - 4} y={y(max * f) + 3} textAnchor="end" className="fill-slate-400" fontSize={9}>
              {Math.round(max * f)}
            </text>
          </g>
        ))}
        {days.map((d, i) => (i % Math.max(1, Math.ceil(days.length / 10)) === 0 || i === days.length - 1) && (
          <text key={d} x={x(i)} y={h - 4} textAnchor="middle" className="fill-slate-400" fontSize={9}>
            {d.slice(5)}
          </text>
        ))}
        {sources.map((s, i) => (
          <g key={s.id}>
            <path d={path(s.perDay.map((d) => d.posts))} fill="none" stroke={COLORS[i % COLORS.length]} strokeWidth={1.5} />
            <path d={path(s.perDay.map((d) => d.copies))} fill="none" stroke={COLORS[i % COLORS.length]} strokeWidth={1} strokeDasharray="3 3" opacity={0.8} />
          </g>
        ))}
      </svg>
      <div className="flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-slate-500">
        {sources.map((s, i) => (
          <span key={s.id} className="flex items-center gap-1">
            <span className="inline-block h-2 w-2 rounded-full" style={{ background: COLORS[i % COLORS.length] }} />
            {s.name}
          </span>
        ))}
        <span className="text-slate-400">суцільна — пости, пунктир — з них копії</span>
      </div>
    </div>
  )
}

function Matrix({ sources, pairs }: { sources: SourceAnalyticsDto[]; pairs: CopyPairDto[] }) {
  const byKey = useMemo(() => new Map(pairs.map((p) => [`${p.copierId}-${p.originalId}`, p])), [pairs])
  if (sources.length === 0) return null
  return (
    <div className="overflow-x-auto">
      <table className="text-xs">
        <thead className="text-slate-500">
          <tr>
            <th className="py-1 pr-2 text-left font-normal">копіює ↓ · кого →</th>
            {sources.map((s) => (
              <th key={s.id} className="px-1 pb-1 text-center font-normal" title={s.name}>
                <span className="inline-block max-w-16 truncate align-bottom">{s.code.replace(/^tg_/, '')}</span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sources.map((copier) => (
            <tr key={copier.id} className="border-t border-slate-100 dark:border-slate-800">
              <td className="py-0.5 pr-2 whitespace-nowrap">{copier.name}</td>
              {sources.map((orig) => {
                if (orig.id === copier.id) return <td key={orig.id} className="bg-slate-100 text-center text-slate-300 dark:bg-slate-800/60 dark:text-slate-600">·</td>
                const p = byKey.get(`${copier.id}-${orig.id}`)
                const share = p && copier.posts ? p.count / copier.posts : 0
                const alpha = p && p.count > 0 ? 0.15 + Math.min(0.85, share * 3) : 0
                return (
                  <td
                    key={orig.id}
                    className="h-7 min-w-10 text-center font-mono"
                    style={alpha ? { background: `rgba(217, 119, 6, ${alpha.toFixed(2)})` } : undefined}
                    title={p ? `${copier.name} → ${orig.name}: ${p.count} копій (${Math.round(share * 100)}% постів), з проміжними ${p.countAll}; затримка сер. ${fmtDelay(p.avgDelaySeconds)}, мед. ${fmtDelay(p.medianDelaySeconds)}; дослівних ${p.verbatim}, пересилань ${p.forwards}` : undefined}
                  >
                    {p && p.count > 0 ? p.count : p && p.countAll > 0 ? <span className="text-slate-400">({p.countAll})</span> : ''}
                  </td>
                )
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function PairsTable({ pairs, nameOf }: { pairs: CopyPairDto[]; nameOf: Map<number, string> }) {
  const top = pairs.filter((p) => p.count > 0).slice(0, 40)
  return (
    <table className="w-full text-xs">
      <thead className="text-left text-slate-500">
        <tr>
          <th className="py-1 pr-2">Копіювальник</th>
          <th className="pr-2">Оригінал</th>
          <th className="pr-2">Копій</th>
          <th className="pr-2">З проміжними</th>
          <th className="pr-2">Дослівних</th>
          <th className="pr-2">Пересилань</th>
          <th className="pr-2">Затримка сер. / мед. / мін.</th>
          <th className="pr-2">Схожість</th>
        </tr>
      </thead>
      <tbody>
        {top.map((p) => (
          <tr key={`${p.copierId}-${p.originalId}`} className="border-t border-slate-100 dark:border-slate-800">
            <td className="py-1 pr-2">{nameOf.get(p.copierId) ?? `#${p.copierId}`}</td>
            <td className="pr-2">{nameOf.get(p.originalId) ?? `#${p.originalId}`}</td>
            <td className="pr-2 font-mono">{p.count}</td>
            <td className="pr-2 text-slate-500">{p.countAll}</td>
            <td className="pr-2">{p.verbatim}</td>
            <td className="pr-2">{p.forwards}</td>
            <td className="pr-2">
              {fmtDelay(p.avgDelaySeconds)} / {fmtDelay(p.medianDelaySeconds)} / {fmtDelay(p.minDelaySeconds)}
            </td>
            <td className="pr-2 font-mono">{p.avgJaccard.toFixed(2)}</td>
          </tr>
        ))}
        {top.length === 0 && (
          <tr>
            <td colSpan={8} className="py-1 text-slate-500">
              Копіювань за період не знайдено.
            </td>
          </tr>
        )}
      </tbody>
    </table>
  )
}

function FirstsSection({ report, nameOf }: { report: AnalyticsReportDto; nameOf: Map<number, string> }) {
  const categories = useMemo(() => [...new Set(report.firsts.map((f) => f.categoryCode))].sort(), [report])
  const sourceIds = useMemo(() => [...new Set(report.firsts.map((f) => f.sourceId))], [report])
  const byKey = useMemo(() => new Map(report.firsts.map((f) => [`${f.sourceId}-${f.categoryCode}`, f])), [report])
  const totalFirsts = useMemo(() => {
    const m = new Map<string, number>()
    for (const f of report.firsts) m.set(f.categoryCode, (m.get(f.categoryCode) ?? 0) + f.firsts)
    return m
  }, [report])
  return (
    <Section title="Хто перший бачить цілі" badge={<Badge ok={null} text={`${categories.length} категорій`} />}>
      <p className="text-xs text-slate-500">
        За треками конвеєра: джерело, чиє повідомлення відкрило трек, — «перше». У клітинці — скільки треків категорії джерело відкрило (частка від усіх треків категорії за період) і на скільки в середньому воно відставало, коли першим було інше.
      </p>
      <div className="overflow-x-auto">
        <table className="w-full text-xs">
          <thead className="text-left text-slate-500">
            <tr>
              <th className="py-1 pr-2">Джерело</th>
              {categories.map((c) => (
                <th key={c} className="pr-2">
                  {c}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {sourceIds
              .map((id) => ({ id, total: categories.reduce((n, c) => n + (byKey.get(`${id}-${c}`)?.firsts ?? 0), 0) }))
              .sort((a, b) => b.total - a.total)
              .map(({ id }) => (
                <tr key={id} className="border-t border-slate-100 dark:border-slate-800">
                  <td className="py-1 pr-2">{nameOf.get(id) ?? `#${id}`}</td>
                  {categories.map((c) => {
                    const f = byKey.get(`${id}-${c}`)
                    const total = totalFirsts.get(c) ?? 0
                    return (
                      <td key={c} className="pr-2" title={f ? `участь у ${f.participations} треках` : undefined}>
                        {f ? (
                          <>
                            <span className="font-mono">{f.firsts}</span> <span className="text-slate-400">({total ? Math.round((f.firsts / total) * 100) : 0}%)</span>
                            {f.avgLagSeconds !== undefined && f.avgLagSeconds !== null && <span className="block text-[10px] text-slate-400">відстає на {fmtDelay(f.avgLagSeconds)}</span>}
                          </>
                        ) : (
                          <span className="text-slate-300">—</span>
                        )}
                      </td>
                    )
                  })}
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </Section>
  )
}

function RecentList({ items, nameOf }: { items: RecentCopyDto[]; nameOf: Map<number, string> }) {
  if (items.length === 0) return <div className="text-xs text-slate-500">Пар ще немає.</div>
  return (
    <div className="space-y-2">
      {items.map((r) => (
        <div key={`${r.copyRawMessageId}-${r.originalRawMessageId}`} className="rounded border border-slate-100 p-2 text-xs dark:border-slate-800">
          <div className="mb-1 flex flex-wrap items-center gap-2 text-slate-500">
            <span>
              <b className="text-slate-700 dark:text-slate-200">{nameOf.get(r.copierId) ?? `#${r.copierId}`}</b> ← {nameOf.get(r.originalId) ?? `#${r.originalId}`}
            </span>
            <Badge ok={r.kind === 'forward' ? true : null} text={r.kind === 'forward' ? 'пересилання' : r.kind === 'verbatim' ? 'дослівно' : 'схоже'} />
            <span>через {fmtDelay(r.delaySeconds)}</span>
            <span className="font-mono">J {r.jaccard.toFixed(2)} · C {r.containment.toFixed(2)}</span>
            {!r.isPrimary && <span className="text-slate-400">проміжний оригінал</span>}
            <span className="ml-auto">{fmtTime(r.copyPublishedAt)}</span>
          </div>
          <div className="grid gap-2 sm:grid-cols-2">
            <Quote label="оригінал" text={r.originalText} url={r.originalUrl} at={r.originalPublishedAt} />
            <Quote label="копія" text={r.copyText} url={r.copyUrl} at={r.copyPublishedAt} />
          </div>
        </div>
      ))}
    </div>
  )
}

function Quote({ label, text, url, at }: { label: string; text?: string; url?: string; at: string }) {
  return (
    <div className="rounded bg-slate-50 p-2 dark:bg-slate-800/60">
      <div className="mb-0.5 text-[10px] uppercase tracking-wide text-slate-400">
        {label} · {fmtTime(at)}
        {url && (
          <a className="ml-1 underline" href={url} target="_blank" rel="noreferrer">
            відкрити
          </a>
        )}
      </div>
      <div className="whitespace-pre-wrap break-words text-slate-700 dark:text-slate-200">{text ?? '—'}</div>
    </div>
  )
}
