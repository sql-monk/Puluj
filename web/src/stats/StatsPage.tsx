import { useMemo, useState } from 'react'
import type { StatsDto } from '../api/types'
import { themeIsDark, useStore } from '../store/useStore'
import Bars from './charts/Bars'
import ChartCard, { LegendItem } from './charts/ChartCard'
import Heatmap from './charts/Heatmap'
import StackedColumns from './charts/StackedColumns'
import StatTile from './charts/StatTile'
import { accent, categoryColor, muted } from './palette'
import { PRESETS, bucketTitle, compact, hoursText, pct, presetPeriod, routeMatrix, toLocalInput, type Period, type Preset } from './period'
import SourcesTable from './SourcesTable'
import { useStats } from './useStats'

const WEEKDAYS = ['пн', 'вт', 'ср', 'чт', 'пт', 'сб', 'нд']
const HOURS = Array.from({ length: 24 }, (_, h) => String(h).padStart(2, '0'))

/**
 * The statistics page: one period, every chart. Sits over the map (which stays mounted underneath), scrolls on its
 * own. Every section reads the same payload, so the numbers always agree; while a new period loads the old charts
 * stay, dimmed.
 */
export default function StatsPage() {
  const { period, setPeriod, data, loading, error } = useStats()
  const theme = useStore((s) => s.theme)
  const dark = themeIsDark(theme)
  return (
    <div className="pointer-events-auto absolute inset-0 z-10 overflow-y-auto bg-slate-100 pt-12 text-slate-900 md:pt-14 dark:bg-slate-950 dark:text-slate-100">
      <div className="mx-auto flex max-w-6xl flex-col gap-3 px-3 pb-8">
        <PeriodBar period={period} onChange={setPeriod} loading={loading} />
        {error && <div className="rounded bg-red-600 px-3 py-1.5 text-xs text-white">Не вдалося завантажити статистику: {error}</div>}
        {!data && !error && <div className="py-10 text-center text-sm text-slate-500">Завантаження…</div>}
        {data && (
          <div className={`flex flex-col gap-3 transition-opacity ${loading ? 'opacity-60' : ''}`}>
            <Dashboard data={data} dark={dark} />
          </div>
        )}
      </div>
    </div>
  )
}

function PeriodBar({ period, onChange, loading }: { period: Period; onChange: (p: Period) => void; loading: boolean }) {
  const [from, setFrom] = useState(toLocalInput(period.from))
  const [to, setTo] = useState(toLocalInput(period.to))
  const pick = (id: Preset) => {
    if (id === 'custom') {
      setFrom(toLocalInput(period.from))
      setTo(toLocalInput(period.to))
      onChange({ preset: 'custom', from: period.from, to: period.to })
    } else {
      onChange(presetPeriod(id))
    }
  }
  const apply = () => {
    const f = new Date(from)
    const t = new Date(to)
    if (Number.isNaN(f.getTime()) || Number.isNaN(t.getTime()) || t <= f) return
    onChange({ preset: 'custom', from: f, to: t })
  }
  const range = `${period.from.toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })} — ${period.to.toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })}`
  return (
    <div className="sticky top-0 z-20 -mx-3 flex flex-wrap items-center gap-2 bg-slate-100/95 px-3 py-2 text-sm backdrop-blur dark:bg-slate-950/95">
      <h2 className="mr-2 font-semibold">Статистика</h2>
      <span className="inline-flex overflow-hidden rounded-md border border-slate-300 text-xs dark:border-slate-600" role="tablist" aria-label="Період">
        {PRESETS.map((p) => (
          <button key={p.id} role="tab" aria-selected={period.preset === p.id} className={`px-2.5 py-1 ${period.preset === p.id ? 'bg-blue-600 text-white' : 'hover:bg-slate-200 dark:hover:bg-slate-700'}`} onClick={() => pick(p.id)}>
            {p.label}
          </button>
        ))}
      </span>
      {period.preset === 'custom' && (
        <span className="flex flex-wrap items-center gap-1 text-xs">
          <input type="datetime-local" className="rounded border border-slate-300 bg-white px-1.5 py-0.5 dark:border-slate-600 dark:bg-slate-800" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="Початок" />
          <span>—</span>
          <input type="datetime-local" className="rounded border border-slate-300 bg-white px-1.5 py-0.5 dark:border-slate-600 dark:bg-slate-800" value={to} onChange={(e) => setTo(e.target.value)} aria-label="Кінець" />
          <button className="rounded bg-blue-600 px-2 py-0.5 text-white hover:bg-blue-700" onClick={apply}>
            Показати
          </button>
        </span>
      )}
      <span className="ml-auto text-xs text-slate-500 dark:text-slate-400">
        {range}
        {loading && ' · оновлення…'}
      </span>
    </div>
  )
}

function Dashboard({ data, dark }: { data: StatsDto; dark: boolean }) {
  const [measure, setMeasure] = useState<'targets' | 'tracks'>('targets')
  const t = data.totals
  const buckets = data.bucketStarts
  const categorySeries = useMemo(
    () =>
      data.categories
        .map((c, j) => ({ key: c.code, label: c.name, color: categoryColor(c.code, dark), values: data.timeline.map((b) => (measure === 'targets' ? b.targets[j] : b.tracks[j]) ?? 0) }))
        .filter((s) => s.values.some((v) => v > 0)),
    [data, dark, measure],
  )
  const categoryName = useMemo(() => new Map(data.categories.map((c) => [c.code, c.name])), [data])
  const alertsSeries = useMemo(() => [{ key: 'alertHours', label: 'годин під тривогою', color: accent(dark), values: data.timeline.map((b) => b.alertHours) }], [data, dark])
  const matrix = useMemo(() => routeMatrix(data.routes, 8), [data])
  const hwMax = Math.max(0, ...data.hourWeekday.flat())
  const one = accent(dark)
  const noTargets = t.targets === 0
  return (
    <>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-6">
        <StatTile label="Фактів про цілі" value={compact(t.targets)} note="без повторів між джерелами" />
        <StatTile label="Окремих обʼєктів (треків)" value={compact(t.tracks)} note={`заявлено: ${compact(t.objectsDeclared)}`} />
        <StatTile label="Тривог по областях" value={compact(t.alerts)} note={`${hoursText(t.alertHours)} під тривогою`} />
        <StatTile label="Повідомлень" value={compact(t.messages)} note={`оброблено ${pct(t.messagesProcessed, t.messages)}`} />
        <StatTile label="З розпізнаними фактами" value={pct(t.messagesWithTargets, t.messagesProcessed)} note={`${compact(t.messagesWithTargets)} з оброблених`} />
        <StatTile label="Активних джерел" value={String(t.activeSources)} note="з ≥ 1 повідомленням" />
      </div>

      <ChartCard
        title="Що летіло"
        subtitle={`${measure === 'targets' ? 'факти' : 'окремі обʼєкти (треки)'} за ${data.bucket === 'hour' ? 'годину' : data.bucket === 'day' ? 'добу' : 'тиждень'}, за категоріями`}
        empty={noTargets && t.tracks === 0}
        legend={
          <>
            {categorySeries.map((s) => (
              <LegendItem key={s.key} color={s.color} label={s.label} />
            ))}
            <span className="ml-auto inline-flex overflow-hidden rounded border border-slate-300 dark:border-slate-600">
              {(['targets', 'tracks'] as const).map((m) => (
                <button key={m} className={`px-2 py-0.5 ${measure === m ? 'bg-slate-200 dark:bg-slate-700' : ''}`} onClick={() => setMeasure(m)} aria-pressed={measure === m}>
                  {m === 'targets' ? 'факти' : 'обʼєкти'}
                </button>
              ))}
            </span>
          </>
        }
        table={{ head: ['Час', ...categorySeries.map((s) => s.label), 'Разом'], rows: buckets.map((b, i) => [bucketTitle(b, data.bucket), ...categorySeries.map((s) => s.values[i]), categorySeries.reduce((n, s) => n + s.values[i], 0)]) }}
      >
        <StackedColumns buckets={buckets} unit={data.bucket} series={categorySeries} valueLabel={measure === 'targets' ? 'фактів' : 'обʼєктів'} />
      </ChartCard>

      <div className="grid gap-3 lg:grid-cols-2">
        <ChartCard title="Тривоги в часі" subtitle="годин під тривогою (сума по областях) за бакет; скільки оголошено — у підказці" empty={t.alerts === 0} table={{ head: ['Час', 'Годин', 'Оголошено'], rows: buckets.map((b, i) => [bucketTitle(b, data.bucket), Math.round(data.timeline[i].alertHours), data.timeline[i].alerts]) }}>
          <StackedColumns buckets={buckets} unit={data.bucket} series={alertsSeries} valueLabel="годин" height={180} format={(v) => `${Math.round(v).toLocaleString('uk-UA')} год`} extra={(i) => [{ value: String(data.timeline[i].alerts), label: 'тривог оголошено' }]} />
          <div className="text-[11px] text-slate-500 dark:text-slate-400">Сумарно годин під тривогою по всіх областях: {hoursText(t.alertHours)}</div>
        </ChartCard>
        <ChartCard title="Тривалість тривог" subtitle="завершені тривоги по областях за період" empty={data.alertDurations.every((d) => d.count === 0)} table={{ head: ['Тривалість', 'Тривог'], rows: data.alertDurations.map((d) => [d.label, d.count]) }}>
          <Bars rows={data.alertDurations.map((d) => ({ key: d.key, label: d.label, value: d.count }))} color={one} labelWidth={100} />
        </ChartCard>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <ChartCard title="За класом" subtitle="факти; поряд — окремі обʼєкти (треки) і заявлена кількість" empty={noTargets} table={{ head: ['Клас', 'Фактів', 'Обʼєктів', 'Заявлено'], rows: data.byClass.map((c) => [c.name, c.targets, c.tracks, c.objectsDeclared]) }}>
          <Bars
            rows={data.byClass.slice(0, 14).map((c) => ({ key: c.code, label: c.name, value: c.targets, hint: c.tracks > 0 ? `· ${c.tracks.toLocaleString('uk-UA')} об.` : undefined, swatch: categoryColor(c.categoryCode, dark), details: [{ value: c.tracks.toLocaleString('uk-UA'), label: 'окремих обʼєктів' }, { value: c.objectsDeclared.toLocaleString('uk-UA'), label: 'заявлено у повідомленнях' }, { value: categoryName.get(c.categoryCode) ?? c.categoryCode, label: 'категорія' }] }))}
            color={one}
            labelWidth={170}
          />
        </ChartCard>
        <ChartCard title="По областях" subtitle="де зафіксовано факти (область локації)" empty={data.byRegion.length === 0} table={{ head: ['Область', 'Фактів'], rows: data.byRegion.map((r) => [r.name, r.targets]) }}>
          <Bars rows={data.byRegion.filter((r) => r.id !== undefined).map((r) => ({ key: String(r.id), label: r.name, value: r.targets }))} color={one} labelWidth={170} />
          <div className="text-[11px] text-slate-500 dark:text-slate-400">
            Решта областей разом: {compact(data.byRegion.find((r) => r.id === undefined)?.targets ?? 0)} · без локації або поза областями: {compact(Math.max(0, t.targets - data.byRegion.reduce((n, r) => n + r.targets, 0)))}
          </div>
        </ChartCard>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <ChartCard title="Звідки → куди" subtitle="повідомлення з напрямком «з області А на область Б»; топ-8 × топ-8" empty={data.routes.length === 0} table={{ head: ['Звідки', 'Куди', 'Фактів'], rows: data.routes.slice(0, 60).map((r) => [r.fromName, r.toName, r.count]) }}>
          <Heatmap rows={matrix.rows.map((r) => r.name)} cols={matrix.cols.map((c) => short(c.name))} values={matrix.values} dark={dark} labelWidth={150} cellHeight={24} rotateCols title={(i, j) => `${matrix.rows[i].name} → ${matrix.cols[j].name}`} />
          <ol className="mt-1 columns-2 text-[11px] text-slate-600 dark:text-slate-300">
            {data.routes.slice(0, 10).map((r) => (
              <li key={`${r.fromId}-${r.toId}`} className="truncate">
                <span className="tabular-nums text-slate-400">{r.count}</span> {short(r.fromName)} → {short(r.toName)}
              </li>
            ))}
          </ol>
        </ChartCard>
        <ChartCard title="Година × день тижня" subtitle="факти за київським часом" empty={hwMax === 0} table={{ head: ['День', ...HOURS], rows: data.hourWeekday.map((row, i) => [WEEKDAYS[i], ...row]) }}>
          <Heatmap rows={WEEKDAYS} cols={HOURS} values={data.hourWeekday} dark={dark} labelWidth={30} cellHeight={22} title={(i, j) => `${WEEKDAYS[i]}, ${HOURS[j]}:00–${HOURS[(j + 1) % 24]}:00`} />
        </ChartCard>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <SliceCard title="Типи подій" subtitle="усі події, не лише цілі" slices={data.eventTypes} color={one} />
        <SliceCard title="Метод ідентифікації" subtitle="факти" slices={data.methods} color={one} />
        <SliceCard title="Впевненість" subtitle="факти" slices={data.confidence} color={one} />
        <SliceCard title="Точність локації" subtitle="факти" slices={data.locationKinds} color={one} />
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        <ChartCard title="Тривоги по областях" subtitle="годин під тривогою за період; кількість — у підписі" empty={data.alertsByRegion.length === 0} table={{ head: ['Область', 'Годин', 'Тривог'], rows: data.alertsByRegion.map((a) => [a.name, Math.round(a.hours), a.count]) }}>
          <Bars rows={data.alertsByRegion.slice(0, 15).map((a) => ({ key: String(a.id), label: a.name, value: a.hours, hint: `· ${a.count}`, details: [{ value: String(a.count), label: 'тривог' }] }))} color={one} format={(v) => hoursText(v)} labelWidth={170} />
        </ChartCard>
        <ChartCard title="Джерела" subtitle="активність, обсяг, корисність, затримка" empty={data.sources.length === 0}>
          <SourcesTable sources={data.sources} sparkColor={muted(dark)} />
        </ChartCard>
      </div>
    </>
  )
}

function SliceCard({ title, subtitle, slices, color }: { title: string; subtitle: string; slices: { key: string; label: string; count: number }[]; color: string }) {
  return (
    <ChartCard title={title} subtitle={subtitle} empty={slices.length === 0} table={{ head: ['Значення', 'К-сть'], rows: slices.map((s) => [s.label, s.count]) }}>
      <Bars rows={slices.map((s) => ({ key: s.key, label: s.label, value: s.count }))} color={color} labelWidth={120} />
    </ChartCard>
  )
}

/** "Сумська область" → "Сумська" for narrow column headers; names without the word stay as they are. */
function short(name: string): string {
  return name.replace(/ область$/, '').replace(/^Автономна Республіка /, 'АР ')
}
