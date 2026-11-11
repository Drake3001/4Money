import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from './client'

/** Loads data from the API. `error` is the HTTP status (0 = network failure; 501 has already opened the unavailable dialog). */
export function useRemote<T>(fn: () => Promise<T>, deps: unknown[], enabled = true) {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(enabled)
  const [error, setError] = useState<number | null>(null)
  const [n, setN] = useState(0)
  const seq = useRef(0)
  useEffect(() => {
    if (!enabled) { setLoading(false); return }
    const id = ++seq.current
    setLoading(true); setError(null)
    fn().then((d) => { if (id === seq.current) { setData(d); setLoading(false) } })
      .catch((e) => { if (id !== seq.current) return; setError(e instanceof ApiError ? e.status : 0); setLoading(false) })
  }, [...deps, n, enabled]) // eslint-disable-line react-hooks/exhaustive-deps
  const reload = useCallback(() => setN((x) => x + 1), [])
  return { data, loading, error, reload }
}
