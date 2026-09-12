import { useMemo } from 'react'
import type { TrackDto } from '../api/types'
import { effectiveNow, useStore } from '../store/useStore'
import { computeEta, type EtaResult } from './computeEta'

/** ETA for one track to the viewer's own point. Runs in the browser only (see docs/README.md#карта-і-eta). */
export function useEta(track: TrackDto | null | undefined): EtaResult | null {
  const home = useStore((s) => s.home)
  const mode = useStore((s) => s.mode)
  const at = useStore((s) => s.at)
  const now = useStore((s) => s.now)
  const clock = effectiveNow({ mode, at, now })
  return useMemo(() => (track && home ? computeEta(track, home, clock) : null), [track, home, clock])
}
