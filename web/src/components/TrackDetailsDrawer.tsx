import { useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { ObservationDto, TrackDetailsDto } from '../api/types'
import { useEta } from '../eta/useEta'
import { clock, confidenceLabel, dateTime, directionText, etaText, fixChain, locationKindLabel } from '../lib/format'
import { usePalette, useStore } from '../store/useStore'
import Highlight from './Highlight'

interface Props {
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

/**
 * Spec §18 / §31: the full provenance chain of the selected track — every observation, its source and the original
 * message. A left-hand panel that stays open and follows the selection: pick another target on the map and it
 * shows that one. Only one target is selected at a time.
 */
export default function TrackDetailsDrawer({ onClose }: Props) {
  const trackId = useStore((s) => s.selectedTrackId)
  const live = useStore((s) => (s.selectedTrackId ? s.tracks[s.selectedTrackId] : undefined))
  const sourceFilter = useStore((s) => s.filters.sources)
  const palette = usePalette()
  const [data, setData] = useState<TrackDetailsDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const eta = useEta(live ?? data?.track)
  const version = live?.observationCount ?? 0

  // Loaded for the selected track and refreshed when it gains observations (debounced; an older reply still lands).
  const seq = useRef(0)
  const applied = useRef(0)
  useEffect(() => {
    applied.current = 0
    setData(null)
    setError(null)
  }, [trackId])
  useEffect(() => {
    if (!trackId) return
    const id = ++seq.current
    const handle = window.setTimeout(() => {
      api
        .track(trackId)
        .then((d) => {
          if (id > applied.current) {
            applied.current = id
            setData(d)
            setError(null)
          }
        })
        .catch((e: Error) => {
          if (id > applied.current) setError(e.message)
        })
    }, applied.current === 0 ? 0 : 1500)
    return () => window.clearTimeout(handle)
  }, [trackId, version])

  const track = live ?? data?.track
  const observations = (data?.observations ?? []).filter((o) => sourceFilter === null || sourceFilter.includes(o.source.id)).slice().reverse()

  return (
    <div className="pointer-events-auto absolute inset-y-0 left-0 top-12 z-30 flex w-full max-w-md flex-col bg-white shadow-2xl md:top-14 md:bottom-3 md:left-3 md:rounded-xl dark:bg-slate-900 dark:text-slate-100">
      <div className="flex items-center justify-between border-b border-slate-200 px-4 py-2.5 dark:border-slate-700">
        <div className="flex min-w-0 items-center gap-2 font-semibold">
          {track && <span className="inline-block h-3 w-3 shrink-0 rounded-full" style={{ background: palette.marker[track.threat.displayMode] }} />}
          <span className="truncate">{track ? `${track.threat.label} · трек #${track.id}` : 'Виділена ціль'}</span>
        </div>
        <button className="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200" onClick={onClose} aria-label="Закрити">
          ✕
        </button>
      </div>
      <div className="flex-1 overflow-y-auto px-4 py-3 text-sm">
        {!trackId && <div className="text-slate-500">Клікніть по цілі на карті — тут з'являться її дані та всі повідомлення, з яких вона побудована. Панель слідує за виділенням.</div>}
        {error && <div className="text-red-600">{error}</div>}
        {trackId && !data && !error && <div className="text-slate-500">Завантаження…</div>}
        {track && (
          <section className="mb-4 rounded-lg bg-slate-50 p-3 text-xs dark:bg-slate-800">
            <div>Тип: {track.threat.categoryName}{track.threat.className ? ` / ${track.threat.className}` : ''}</div>
            <div>Модель: {track.threat.modelName ?? track.threat.familyName ?? '—'} · впевненість: {confidenceLabel[track.modelConfidence]}</div>
            <div>Останнє повідомлення: {clock(track.lastSeenAt)}</div>
            <div>
              Район: {track.lastLocation?.placeName ?? '—'} <span className="text-slate-400">({locationKindLabel[track.lastLocation?.kind ?? 'Unknown']})</span>
            </div>
            <div>Напрямок: {directionText(track.direction)}</div>
            {track.fixes.length >= 2 && <div>Був: {fixChain(track)}</div>}
            <div>Впевненість треку: {confidenceLabel[track.trackConfidence]} · {track.observationCount} повід. · {Math.max(track.distinctSourceCount, track.sourceIds.length)} джерел</div>
            {track.objectCount && track.objectCount > 1 && <div>Цілей у групі: {track.objectCount}</div>}
            <div>ETA до вас: {etaText(eta)}</div>
            {track.status !== 'Active' && <div>Стан: {track.status === 'Cancelled' ? 'відбій' : 'закрито'}{track.closedReason ? ` (${track.closedReason})` : ''}</div>}
          </section>
        )}
        {data && (
          <>
            <div className="mb-1 text-[11px] font-medium text-slate-500">Повідомлення, з яких побудовано трек (новіші зверху)</div>
            <ol className="space-y-3">
              {observations.map((o) => (
                <ObservationItem key={o.id} o={o} />
              ))}
              {observations.length === 0 && <li className="text-xs text-slate-500">Немає повідомлень від вибраних джерел.</li>}
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
        {o.location?.placeName && <> · {o.location.placeName} <span className="text-slate-400">({locationKindLabel[o.location.kind]})</span></>}
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
        {o.rawMessage.text ? <Highlight text={o.rawMessage.text} part={o.segmentText ?? ''} /> : (o.segmentText ?? '(без тексту)')}
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
