import { useStore } from '../store/useStore'
import { num, plural } from './period'

/**
 * "Now" in one line, from what the store already holds for the map (tracks, alerts, the feed of recent reports) —
 * no extra endpoint. In history mode the store is frozen at the chosen instant, so the line says so.
 */
export default function LiveLine() {
  const tracks = useStore((s) => s.tracks)
  const alerts = useStore((s) => s.alerts)
  const targets = useStore((s) => s.targets)
  const now = useStore((s) => s.now)
  const mode = useStore((s) => s.mode)
  const connection = useStore((s) => s.connection)
  const active = Object.values(tracks).filter((t) => t.status === 'Active').length
  const places = Object.keys(alerts).length
  const hourAgo = now.getTime() - 3600_000
  const facts = targets.filter((o) => o.eventType === 'TargetObserved' && !o.duplicateOfTargetId && new Date(o.observedAt).getTime() >= hourAgo).length
  return (
    <div className="text-xs text-slate-600 dark:text-slate-300" aria-live="polite">
      <span className="font-medium">{mode === 'history' ? 'на обраний момент' : 'зараз'}:</span> {num(active)} {plural(active, 'активна ціль', 'активні цілі', 'активних цілей')} · {num(places)} {plural(places, 'місце', 'місця', 'місць')} під тривогою · {num(facts)} {plural(facts, 'факт', 'факти', 'фактів')} за останню годину
      {connection !== 'connected' && mode === 'live' && <span className="text-slate-400"> · без живого з’єднання</span>}
    </div>
  )
}
