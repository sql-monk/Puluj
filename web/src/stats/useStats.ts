import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../api/client'
import type { StatsDto } from '../api/types'
import { parsePeriodHash, periodHash, type Period } from './period'

/**
 * The period comes from the hash (`#/stats`, `#/stats?p=7d`, `#/stats?from=…&to=…`) and every change goes back into
 * it, so a view can be linked. While a new period loads the previous payload stays on screen (the page dims it);
 * a response that arrives after a newer request was sent is dropped.
 */
export function useStats() {
  const [period, setPeriodState] = useState<Period>(() => parsePeriodHash(window.location.hash))
  const [data, setData] = useState<StatsDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const seq = useRef(0)

  useEffect(() => {
    const onHash = () => {
      if (window.location.hash.startsWith('#/stats')) setPeriodState(parsePeriodHash(window.location.hash))
    }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])

  const setPeriod = useCallback((p: Period) => {
    const hash = periodHash(p)
    if (window.location.hash === hash) {
      setPeriodState(p)
    } else {
      window.location.hash = hash
    }
  }, [])

  useEffect(() => {
    const id = ++seq.current
    setLoading(true)
    api
      .stats(period.from, period.to)
      .then((d) => {
        if (id !== seq.current) return
        setData(d)
        setError(null)
      })
      .catch((e: Error) => {
        if (id === seq.current) setError(e.message)
      })
      .finally(() => {
        if (id === seq.current) setLoading(false)
      })
  }, [period])

  return { period, setPeriod, data, loading, error }
}
