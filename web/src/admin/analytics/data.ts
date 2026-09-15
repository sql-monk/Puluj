// Loading with a module-level cache: the admin shell re-mounts the panel when the hash flips between #/analytics and
// #/analytics-service, and the cache lets the new mount paint the last answer instantly while it refreshes.
import { useCallback, useEffect, useRef, useState } from 'react'
import { AdminError } from '../../api/admin'

export interface LoadError {
  message: string
  status?: number
}

const cache = new Map<string, unknown>()

export function useLoad<T>(key: string | null, load: () => Promise<T>, pollMs?: number) {
  const [data, setData] = useState<T | null>(() => (key && cache.has(key) ? (cache.get(key) as T) : null))
  const [error, setError] = useState<LoadError | null>(null)
  const [loading, setLoading] = useState(false)
  /** When the current `data` arrived (ms since epoch); 0 before the first answer. */
  const [at, setAt] = useState(0)
  const loadRef = useRef(load)
  loadRef.current = load
  const keyRef = useRef(key)
  keyRef.current = key

  const run = useCallback(() => {
    if (!key) return
    setLoading(true)
    loadRef
      .current()
      .then((d) => {
        cache.set(key, d)
        if (keyRef.current !== key) return
        setData(d)
        setAt(Date.now())
        setError(null)
      })
      .catch((e: unknown) => {
        if (keyRef.current !== key) return
        setError({ message: e instanceof Error ? e.message : String(e), status: e instanceof AdminError ? e.status : undefined })
      })
      .finally(() => {
        if (keyRef.current === key) setLoading(false)
      })
  }, [key])

  useEffect(() => {
    setData(key && cache.has(key) ? (cache.get(key) as T) : null)
    setError(null)
    run()
    if (!key || !pollMs) return
    const id = window.setInterval(run, pollMs)
    return () => window.clearInterval(id)
  }, [key, run, pollMs])

  return { data, error, loading, at, reload: run }
}

export function invalidate(prefix: string) {
  for (const k of [...cache.keys()]) if (k.startsWith(prefix)) cache.delete(k)
}
