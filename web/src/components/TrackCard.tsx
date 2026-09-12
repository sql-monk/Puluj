import type { TrackDto } from '../api/types'
import { useEta } from '../eta/useEta'
import { confidenceLabel, directionText, etaConfidence, etaText, locationKindLabel, timeAgo } from '../lib/format'
import { colors } from '../map/geojson'
import { effectiveNow, useStore } from '../store/useStore'

interface Props {
  track: TrackDto
  onDetails: () => void
  onClose: () => void
}

/** Spec §12 marker card: type/model, age, course, ETA to the viewer's point, confidence. */
export default function TrackCard({ track, onDetails, onClose }: Props) {
  const eta = useEta(track)
  const home = useStore((s) => s.home)
  const clock = effectiveNow(useStore.getState())
  const loc = track.lastLocation

  return (
    <div className="pointer-events-auto absolute bottom-3 left-1/2 z-20 w-[min(92vw,22rem)] -translate-x-1/2 rounded-xl bg-white/95 p-3 text-sm shadow-xl backdrop-blur md:bottom-6 md:left-auto md:right-3 md:translate-x-0 dark:bg-slate-900/95 dark:text-slate-100">
      <div className="mb-1 flex items-start justify-between gap-2">
        <div>
          <div className="flex items-center gap-2 font-semibold">
            <span className="inline-block h-3 w-3 rounded-full" style={{ background: colors[track.threat.displayMode] }} />
            {track.threat.label}
            {track.objectCount && <span className="text-xs font-normal text-slate-500">×{track.objectCount}</span>}
          </div>
          <div className="text-xs text-slate-500 dark:text-slate-400">
            {track.threat.categoryName}
            {track.threat.className ? ` · ${track.threat.className}` : ''} · впевненість у моделі: {confidenceLabel[track.modelConfidence]}
          </div>
        </div>
        <button className="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" onClick={onClose} aria-label="Закрити">
          ✕
        </button>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-0.5 text-xs">
        <dt className="text-slate-500">Останнє</dt>
        <dd>
          {timeAgo(track.lastSeenAt, clock)}
          {track.status !== 'Active' && <span className="ml-1 rounded bg-slate-200 px-1 dark:bg-slate-700">{track.status === 'Cancelled' ? 'відбій' : 'закрито'}</span>}
        </dd>
        <dt className="text-slate-500">Район</dt>
        <dd>
          {loc?.placeName ?? '—'} <span className="text-slate-400">({locationKindLabel[loc?.kind ?? 'Unknown']})</span>
        </dd>
        <dt className="text-slate-500">Курс</dt>
        <dd>{directionText(track.direction)}</dd>
        <dt className="text-slate-500">ETA до вас</dt>
        <dd className={eta?.kind === 'imminent' ? 'font-semibold text-red-600' : ''}>
          {etaText(eta)}
          {home && etaConfidence(eta) && <span className="text-slate-400"> · {etaConfidence(eta)}</span>}
        </dd>
        <dt className="text-slate-500">Трек</dt>
        <dd>
          впевненість {confidenceLabel[track.trackConfidence]} · {track.observationCount} повід. · {track.distinctSourceCount} джерел
        </dd>
      </dl>
      <button className="mt-2 w-full rounded bg-slate-800 px-2 py-1.5 text-xs font-medium text-white hover:bg-slate-700 dark:bg-slate-100 dark:text-slate-900" onClick={onDetails}>
        Джерела / Деталі
      </button>
    </div>
  )
}
