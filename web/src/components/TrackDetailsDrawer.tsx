import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ObservationDto, TrackDetailsDto } from '../api/types'
import { useEta } from '../eta/useEta'
import { clock, confidenceLabel, dateTime, directionText, etaText, locationKindLabel } from '../lib/format'

interface Props {
  trackId: number
  onClose: () => void
}

const eventLabel: Record<string, string> = {
  ThreatObserved: 'спостереження',
  AirRaidAlert: 'тривога',
  AlertCancelled: 'відбій тривоги',
  ThreatCancelled: 'загроза минула',
  ExplosionReport: 'вибухи',
  AirDefenseActivity: 'робота ППО',
}

const methodLabel: Record<string, string> = { Rule: 'правила', Llm: 'LLM', Structured: 'структуроване джерело', Manual: 'вручну' }

/** Spec §18 / §31: the full provenance chain — every observation, its source and the original message. */
export default function TrackDetailsDrawer({ trackId, onClose }: Props) {
  const [data, setData] = useState<TrackDetailsDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const eta = useEta(data?.track)

  useEffect(() => {
    setData(null)
    setError(null)
    api
      .track(trackId)
      .then(setData)
      .catch((e: Error) => setError(e.message))
  }, [trackId])

  return (
    <div className="pointer-events-auto absolute inset-y-0 right-0 z-30 flex w-full max-w-md flex-col bg-white shadow-2xl dark:bg-slate-900 dark:text-slate-100">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3 dark:border-slate-700">
        <div className="font-semibold">Джерела / Деталі · трек #{trackId}</div>
        <button className="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" onClick={onClose} aria-label="Закрити">
          ✕
        </button>
      </div>
      <div className="flex-1 overflow-y-auto px-4 py-3 text-sm">
        {error && <div className="text-red-600">{error}</div>}
        {!data && !error && <div className="text-slate-500">Завантаження…</div>}
        {data && (
          <>
            <section className="mb-4 rounded-lg bg-slate-50 p-3 text-xs dark:bg-slate-800">
              <div className="mb-1 text-sm font-semibold">{data.track.threat.label}</div>
              <div>Тип: {data.track.threat.categoryName}{data.track.threat.className ? ` / ${data.track.threat.className}` : ''}</div>
              <div>Модель: {data.track.threat.modelName ?? data.track.threat.familyName ?? '—'} · впевненість: {confidenceLabel[data.track.modelConfidence]}</div>
              <div>Останнє повідомлення: {clock(data.track.lastSeenAt)}</div>
              <div>Напрямок: {directionText(data.track.direction)}</div>
              <div>Впевненість треку: {confidenceLabel[data.track.trackConfidence]}</div>
              <div>ETA до вас: {etaText(eta)}</div>
              {data.track.closedReason && <div>Закрито: {data.track.closedReason}</div>}
            </section>
            <ol className="space-y-3">
              {data.observations.map((o) => (
                <ObservationItem key={o.id} o={o} />
              ))}
            </ol>
          </>
        )}
      </div>
    </div>
  )
}

function ObservationItem({ o }: { o: ObservationDto }) {
  return (
    <li className={`rounded-lg border p-3 text-xs ${o.duplicateOfObservationId ? 'border-dashed border-slate-300 opacity-80 dark:border-slate-600' : 'border-slate-200 dark:border-slate-700'}`}>
      <div className="mb-1 flex flex-wrap items-baseline justify-between gap-x-2">
        <span className="font-mono text-sm">{clock(o.observedAt)}</span>
        <span className="font-medium">{o.source.name}</span>
        <span className="text-slate-400">довіра {Math.round(o.source.trustLevel * 100)}%</span>
      </div>
      <div className="mb-1 text-slate-600 dark:text-slate-300">
        {eventLabel[o.eventType] ?? o.eventType}
        {o.threat && <> · {o.threat.label} ({confidenceLabel[o.modelConfidence]})</>}
        {o.objectCount && <> · {o.objectCountIsApproximate ? '~' : ''}{o.objectCount} од.</>}
        {o.location && <> · {o.location.placeName} <span className="text-slate-400">({locationKindLabel[o.location.kind]})</span></>}
        {o.destination && <> → {o.destination.placeName}</>}
        {o.direction && <> · {directionText(o.direction)}</>}
      </div>
      <div className="mb-1 text-[11px] text-slate-400">
        розпізнано: {methodLabel[o.identificationMethod] ?? o.identificationMethod}
        {o.identificationSource && <> («{o.identificationSource}»)</>} · впевненість спостереження {confidenceLabel[o.observationConfidence]}
        {o.associationConfidence !== undefined && <> · зв'язок з треком {Math.round(o.associationConfidence * 100)}%</>}
        {o.duplicateOfObservationId && <> · дубль #{o.duplicateOfObservationId}</>}
      </div>
      <blockquote className="whitespace-pre-wrap rounded bg-slate-50 p-2 text-[12px] leading-snug text-slate-800 dark:bg-slate-800 dark:text-slate-200">
        {o.rawMessage.text ?? o.segmentText ?? '(без тексту)'}
      </blockquote>
      <div className="mt-1 flex justify-between text-[11px] text-slate-400">
        <span>
          опубліковано {dateTime(o.rawMessage.publishedAt)} · отримано {clock(o.rawMessage.receivedAt)}
        </span>
        {o.rawMessage.url && (
          <a className="underline" href={o.rawMessage.url} target="_blank" rel="noreferrer">
            оригінал ↗
          </a>
        )}
      </div>
    </li>
  )
}
