import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../api/client'
import type { TimelineBucketDto } from '../api/types'
import { useStore } from '../store/useStore'

const PRESETS_H = [1, 3, 6, 12, 24]
/** Minutes of history per real second. */
const SPEEDS = [
  { label: '1 хв/с', value: 1 },
  { label: '5 хв/с', value: 5 },
  { label: '15 хв/с', value: 15 },
  { label: '60 хв/с', value: 60 },
]
const TICK_MS = 500
const STEP_MIN = 1

function fmtTime(d: Date) {
  return d.toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' })
}
function fmtDate(d: Date) {
  return d.toLocaleDateString('uk-UA', { day: '2-digit', month: '2-digit' })
}
function toLocalInput(d: Date) {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`
}

/**
 * Replay mode (spec §20): a window of history, a scrubber over it and a transport. Every position asks the API for
 * the state at that instant; the feed shows what had been reported by then. Works on both map pages.
 */
export default function ReplayBar({ onClose }: { onClose: () => void }) {
  const at = useStore((s) => s.at)
  const setMode = useStore((s) => s.setMode)
  const loading = useStore((s) => s.loading)
  const [hours, setHours] = useState(6)
  const [to, setTo] = useState(() => new Date())
  const from = useMemo(() => new Date(to.getTime() - hours * 3600_000), [to, hours])
  const [speed, setSpeed] = useState(5)
  const [playing, setPlaying] = useState(false)
  const [buckets, setBuckets] = useState<TimelineBucketDto[]>([])
  const timer = useRef<number | null>(null)

  const totalMin = hours * 60
  const cursor = at ?? to
  const posMin = Math.min(totalMin, Math.max(0, (cursor.getTime() - from.getTime()) / 60000))
  const isLiveEdge = to.getTime() >= Date.now() - 60_000

  const seek = useCallback(
    (d: Date) => {
      const clamped = new Date(Math.min(to.getTime(), Math.max(from.getTime(), d.getTime())))
      setMode('history', clamped)
    },
    [from, to, setMode],
  )

  // Entering replay freezes the map at the end of the window; the window's data (histogram + feed) loads once per range.
  useEffect(() => {
    setPlaying(false)
    const current = useStore.getState().at
    seek(current && current >= from && current <= to ? current : to)
    const bucketMin = Math.max(1, Math.round(totalMin / 72))
    api.timeline(from, to, bucketMin).then(setBuckets).catch(() => setBuckets([]))
    api
      .observationsBetween(from, to)
      .then((list) => useStore.getState().setObservations(list))
      .catch(() => useStore.getState().setObservations([]))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [from, to])

  useEffect(() => {
    if (!playing) {
      if (timer.current) window.clearInterval(timer.current)
      timer.current = null
      return
    }
    timer.current = window.setInterval(() => {
      const current = useStore.getState().at ?? from
      const next = new Date(current.getTime() + speed * (TICK_MS / 1000) * 60_000)
      if (next >= to) {
        setMode('history', to)
        setPlaying(false)
      } else {
        setMode('history', next)
      }
    }, TICK_MS)
    return () => {
      if (timer.current) window.clearInterval(timer.current)
    }
  }, [playing, speed, from, to, setMode])

  // Keyboard transport: space = play/pause, arrows = step, Home/End = edges. Ignored while typing in a field.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') return
      const current = useStore.getState().at ?? to
      if (e.key === ' ') {
        e.preventDefault()
        setPlaying((p) => !p)
      } else if (e.key === 'ArrowLeft') seek(new Date(current.getTime() - (e.shiftKey ? 10 : STEP_MIN) * 60_000))
      else if (e.key === 'ArrowRight') seek(new Date(current.getTime() + (e.shiftKey ? 10 : STEP_MIN) * 60_000))
      else if (e.key === 'Home') seek(from)
      else if (e.key === 'End') seek(to)
      else if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [seek, from, to, onClose])

  const maxObs = Math.max(1, ...buckets.map((b) => b.observations))
  const btn = 'rounded px-2 py-1 text-sm hover:bg-slate-200 disabled:opacity-40 dark:hover:bg-slate-700'
  const chip = (active: boolean) => `rounded px-2 py-0.5 text-xs ${active ? 'bg-blue-600 text-white' : 'bg-slate-200 hover:bg-slate-300 dark:bg-slate-700 dark:hover:bg-slate-600'}`

  return (
    <div className="pointer-events-auto absolute bottom-3 left-1/2 z-20 w-[min(100%-1.5rem,42rem)] -translate-x-1/2 rounded-xl bg-white/95 p-3 text-slate-800 shadow-lg backdrop-blur dark:bg-slate-900/95 dark:text-slate-100">
      <div className="flex flex-wrap items-center gap-2">
        <span className="rounded bg-indigo-600 px-2 py-0.5 text-xs font-medium text-white">ВІДТВОРЕННЯ</span>
        <span className="text-xs text-slate-500">вікно:</span>
        {PRESETS_H.map((h) => (
          <button key={h} className={chip(hours === h)} onClick={() => setHours(h)}>
            {h} год
          </button>
        ))}
        <label className="ml-1 flex items-center gap-1 text-xs text-slate-500">
          до
          <input
            type="datetime-local"
            className="rounded border border-slate-300 bg-white px-1 py-0.5 text-xs text-slate-800 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
            value={toLocalInput(to)}
            max={toLocalInput(new Date())}
            onChange={(e) => {
              const d = new Date(e.target.value)
              if (!Number.isNaN(d.getTime())) setTo(d > new Date() ? new Date() : d)
            }}
          />
          {!isLiveEdge && (
            <button className="underline" onClick={() => setTo(new Date())}>
              зараз
            </button>
          )}
        </label>
        <span className="ml-auto font-mono text-lg tabular-nums">
          {fmtTime(cursor)} <span className="text-xs text-slate-500">{fmtDate(cursor)}</span>
        </span>
        <button className={btn} title="Вийти з відтворення (Esc)" onClick={onClose}>
          ✕
        </button>
      </div>

      <div className="relative mt-2 h-10" title="повідомлень / тривог у проміжку">
        <div className="absolute inset-0 flex items-end gap-px">
          {buckets.map((b) => (
            <div key={b.from} className="relative flex-1">
              <div className="bg-slate-400/60 dark:bg-slate-500/60" style={{ height: `${Math.max(2, (b.observations / maxObs) * 40)}px` }} />
              {b.alerts > 0 && <div className="absolute bottom-0 left-0 right-0 h-0.5 bg-red-500" />}
            </div>
          ))}
        </div>
        <div className="pointer-events-none absolute bottom-0 top-0 w-0.5 bg-indigo-600" style={{ left: `${(posMin / totalMin) * 100}%` }} />
      </div>
      <input
        type="range"
        className="w-full"
        min={0}
        max={totalMin}
        step={STEP_MIN}
        value={Math.round(posMin)}
        onChange={(e) => {
          setPlaying(false)
          seek(new Date(from.getTime() + Number(e.target.value) * 60_000))
        }}
      />
      <div className="flex items-center gap-1">
        <span className="w-24 text-[11px] text-slate-500">
          {fmtTime(from)} {fmtDate(from)}
        </span>
        <span className="mx-auto flex items-center gap-1">
          <button className={btn} title="На початок (Home)" onClick={() => seek(from)}>
            ⏮
          </button>
          <button className={btn} title="−1 хв (←, Shift: −10)" onClick={() => seek(new Date(cursor.getTime() - STEP_MIN * 60_000))}>
            ◀
          </button>
          <button className={`${btn} min-w-24 bg-indigo-600 text-white hover:bg-indigo-700`} title="Пробіл" onClick={() => setPlaying((p) => !p)} disabled={posMin >= totalMin && !playing}>
            {playing ? '⏸ пауза' : '▶ відтворити'}
          </button>
          <button className={btn} title="+1 хв (→, Shift: +10)" onClick={() => seek(new Date(cursor.getTime() + STEP_MIN * 60_000))}>
            ▶
          </button>
          <button className={btn} title="В кінець (End)" onClick={() => seek(to)}>
            ⏭
          </button>
          <select className="ml-2 rounded border border-slate-300 bg-white px-1 py-0.5 text-xs dark:border-slate-600 dark:bg-slate-800" value={speed} onChange={(e) => setSpeed(Number(e.target.value))} title="Швидкість">
            {SPEEDS.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
          {loading && <span className="ml-1 text-[11px] text-slate-400">…</span>}
        </span>
        <span className="w-24 text-right text-[11px] text-slate-500">
          {fmtTime(to)} {fmtDate(to)}
        </span>
      </div>
    </div>
  )
}
