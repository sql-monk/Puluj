import { useMemo, useState } from 'react'
import type { ObservationDto } from '../api/types'
import { clock, confidenceLabel } from '../lib/format'
import { colors } from '../map/geojson'
import { useStore } from '../store/useStore'

const eventLabel: Record<string, string> = {
  ThreatObserved: 'загроза',
  AirRaidAlert: 'тривога',
  AlertCancelled: 'відбій',
  ThreatCancelled: 'загроза минула',
  ExplosionReport: 'вибухи',
  AirDefenseActivity: 'ППО',
  Unknown: '',
}

/** Right-hand feed of all observations (newest first). With a region selected on the map, only its messages are listed. */
export default function FeedPanel({ open, onToggle }: { open: boolean; onToggle: () => void }) {
  const observations = useStore((s) => s.observations)
  const regions = useStore((s) => s.regions)
  const selectedRegionId = useStore((s) => s.selectedRegionId)
  const selectRegion = useStore((s) => s.selectRegion)
  const alerts = useStore((s) => s.alerts)
  const mode = useStore((s) => s.mode)
  const at = useStore((s) => s.at)
  const [expanded, setExpanded] = useState<number | null>(null)

  const region = regions.find((r) => r.id === selectedRegionId)
  // In replay the feed only shows what had been reported by the cursor instant.
  const cutoff = mode === 'history' && at ? at.getTime() : null
  const list = useMemo(() => {
    const byTime = cutoff === null ? observations : observations.filter((o) => new Date(o.observedAt).getTime() <= cutoff)
    if (!selectedRegionId) return byTime
    const hit = (o: ObservationDto) =>
      o.location?.regionId === selectedRegionId || o.location?.placeId === selectedRegionId || o.origin?.regionId === selectedRegionId || o.destination?.regionId === selectedRegionId
    return byTime.filter(hit)
  }, [observations, selectedRegionId, cutoff])
  const fresh = (o: ObservationDto) => cutoff !== null && cutoff - new Date(o.observedAt).getTime() < 3 * 60_000
  // Alerts of the oblast itself plus levelled raion alerts inside it.
  const regionAlerts = selectedRegionId
    ? Object.values(alerts).filter((a) => !a.endedAt && (a.placeId === selectedRegionId || a.location?.regionId === selectedRegionId))
    : []
  const alertText = regionAlerts.some((a) => a.level !== 'Yellow') ? 'тривога' : regionAlerts.length > 0 ? 'жовтий рівень' : null

  if (!open) {
    return (
      <button className="pointer-events-auto absolute right-3 top-14 z-10 rounded-lg bg-white/95 px-3 py-1.5 text-sm shadow dark:bg-slate-900/95 dark:text-slate-100" onClick={onToggle}>
        Повідомлення ({list.length})
      </button>
    )
  }

  return (
    <aside className="pointer-events-auto absolute bottom-0 right-0 top-12 z-10 flex w-full flex-col bg-white/95 shadow-lg backdrop-blur md:right-3 md:top-14 md:bottom-3 md:w-96 md:rounded-xl dark:bg-slate-900/95 dark:text-slate-100">
      <div className="flex items-center gap-2 border-b border-slate-200 px-3 py-2 text-sm dark:border-slate-700">
        <span className="font-medium">Повідомлення</span>
        <span className="text-xs text-slate-500">{list.length}</span>
        {region && (
          <span className="ml-1 flex items-center gap-1 rounded bg-blue-100 px-2 py-0.5 text-xs text-blue-900 dark:bg-blue-900/50 dark:text-blue-100">
            {region.name}
            {alertText && <span className={alertText === 'тривога' ? 'text-red-600 dark:text-red-300' : 'text-yellow-700 dark:text-yellow-300'}>· {alertText}</span>}
            <button className="ml-1 text-blue-700 hover:text-blue-900 dark:text-blue-200" onClick={() => selectRegion(null)} title="Скинути фільтр">
              ✕
            </button>
          </span>
        )}
        <button className="ml-auto text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" onClick={onToggle} aria-label="Згорнути">
          ›
        </button>
      </div>
      {!region && <div className="border-b border-slate-100 px-3 py-1 text-[11px] text-slate-500 dark:border-slate-800">Клікніть по області на карті, щоб бачити лише її повідомлення.</div>}
      <ol className="flex-1 overflow-y-auto text-xs">
        {list.length === 0 && <li className="p-3 text-slate-500">Немає повідомлень{region ? ` для ${region.name}` : ''} за останні години.</li>}
        {list.map((o) => {
          const color = o.threat ? (colors[o.threat.displayMode] ?? colors.uav) : o.eventType === 'AirRaidAlert' ? '#dc2626' : o.eventType === 'AlertCancelled' || o.eventType === 'ThreatCancelled' ? '#16a34a' : '#64748b'
          const isOpen = expanded === o.id
          const text = o.rawMessage.text ?? o.segmentText ?? ''
          return (
            <li key={o.id} className={`border-b border-slate-100 px-3 py-2 dark:border-slate-800 ${o.duplicateOfObservationId ? 'opacity-70' : ''} ${fresh(o) ? 'bg-indigo-50 dark:bg-indigo-950/40' : ''}`}>
              <div className="flex items-baseline gap-2">
                <span className="font-mono">{clock(o.observedAt)}</span>
                <span className="inline-block h-2 w-2 shrink-0 rounded-full" style={{ background: color }} />
                <span className="truncate font-medium">{o.threat?.label ?? eventLabel[o.eventType] ?? o.eventType}</span>
                <span className="ml-auto shrink-0 text-slate-400">{o.source.name}</span>
              </div>
              <div className="text-slate-500">
                {o.location?.placeName ?? '—'}
                {o.destination && ` → ${o.destination.placeName}`}
                {o.direction && ` · ${Math.round(o.direction.degrees)}°`}
                {o.objectCount && ` · ${o.objectCountIsApproximate ? '~' : ''}${o.objectCount}`}
                {o.threat && ` · ${confidenceLabel[o.modelConfidence]}`}
                {o.duplicateOfObservationId && ' · дубль'}
              </div>
              <button className={`mt-0.5 block w-full text-left text-[12px] leading-snug text-slate-800 dark:text-slate-200 ${isOpen ? 'whitespace-pre-wrap' : 'truncate'}`} onClick={() => setExpanded(isOpen ? null : o.id)} title={isOpen ? 'Згорнути' : 'Розгорнути'}>
                {text || '(без тексту)'}
              </button>
              {isOpen && o.rawMessage.url && (
                <a className="text-[11px] text-blue-600 underline dark:text-blue-300" href={o.rawMessage.url} target="_blank" rel="noreferrer">
                  оригінал ↗
                </a>
              )}
            </li>
          )
        })}
      </ol>
    </aside>
  )
}
