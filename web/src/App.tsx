import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from './api/client'
import { connectMapHub } from './api/signalr'
import FeedPanel from './components/FeedPanel'
import FilterPanel from './components/FilterPanel'
import KyivPanel from './components/KyivPanel'
import ReplayBar from './components/ReplayBar'
import TopBar, { type Page } from './components/TopBar'
import TrackDetailsDrawer from './components/TrackDetailsDrawer'
import KyivMapView from './map/KyivMapView'
import MapView from './map/MapView'
import { themeIsDark, themeMapIsDark, useStore } from './store/useStore'

const TICK_MS = 15_000

export default function App() {
  const theme = useStore((s) => s.theme)
  const mode = useStore((s) => s.mode)
  const at = useStore((s) => s.at)
  const selectedTrackId = useStore((s) => s.selectedTrackId)
  const selectedTrack = useStore((s) => (s.selectedTrackId ? s.tracks[s.selectedTrackId] : undefined))
  const setHome = useStore((s) => s.setHome)
  const panelOpen = useStore((s) => s.panelOpen)
  const setPanelOpen = useStore((s) => s.setPanelOpen)
  // The feed opens folded too: a "Повідомлення (N)" button in the top-right corner unfolds it.
  const [feedOpen, setFeedOpen] = useState(false)
  const [replay, setReplay] = useState(false)
  const [picking, setPicking] = useState(false)
  // The details panel (left) shows whichever track is selected on the map, as long as it is open.
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [page, setPage] = useState<Page>(() => (window.location.hash === '#/kyiv' ? 'kyiv' : 'ukraine'))
  const regionsLoaded = useStore((s) => s.regions.length > 0)
  const dark = themeIsDark(theme)
  const mapDark = themeMapIsDark(theme)

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.dataset.theme = theme
  }, [dark, theme])

  // Fade / ETA depend on wall time: re-render every 15 s.
  useEffect(() => {
    const id = window.setInterval(() => useStore.getState().tick(), TICK_MS)
    return () => window.clearInterval(id)
  }, [])

  // Hash routes: #/kyiv (the Kyiv page), anything else = the country map.
  // Back/forward and reloads keep working.
  useEffect(() => {
    const onHash = () => {
      setPage(window.location.hash === '#/kyiv' ? 'kyiv' : 'ukraine')
    }
    window.addEventListener('hashchange', onHash)
    return () => window.removeEventListener('hashchange', onHash)
  }, [])
  // Entering the Kyiv page scopes the feed to the city; leaving it clears the scope.
  useEffect(() => {
    const s = useStore.getState()
    const kyiv = s.regions.find((r) => r.level === 'City' && r.countryCode === 'UA' && r.name === 'Київ')
    s.selectRegion(page === 'kyiv' ? (kyiv?.id ?? null) : null)
    s.select(null)
  }, [page, regionsLoaded])
  // Replay is a mode over whichever map page is open; closing it returns to live (which reloads the live feed).
  const toggleReplay = () => {
    setReplay((r) => {
      if (r) useStore.getState().setMode('live')
      return !r
    })
  }
  const goPage = (p: Page) => {
    window.location.hash = p === 'kyiv' ? '#/kyiv' : ''
  }

  // Region polygons (alerts, region-level markers) and the source list (per-source filter) are loaded once.
  useEffect(() => {
    api
      .regions()
      .then((r) => useStore.getState().setRegions(r))
      .catch((e: Error) => useStore.getState().setError(`Регіони: ${e.message}`))
    api
      .sources()
      .then((s) => useStore.getState().setSources(s))
      .catch((e: Error) => useStore.getState().setError(`Джерела: ${e.message}`))
  }, [])

  // Snapshot requests can overlap while the timeline slider moves; only the latest one may land in the store.
  const snapshotSeq = useRef(0)
  const loadSnapshot = useCallback(async () => {
    const s = useStore.getState()
    const seq = ++snapshotSeq.current
    s.setLoading(true)
    try {
      const [snap, feed] = await Promise.all([api.snapshot(s.mode === 'history' && s.at ? s.at : undefined, false), s.mode === 'live' ? api.targets() : Promise.resolve(null)])
      if (seq !== snapshotSeq.current) return
      if (feed) useStore.getState().setTargets(feed)
      // The store applies "active only" itself so toggling the filter needs no round-trip.
      useStore.getState().setSnapshot(snap.tracks, snap.alerts)
      s.setError(null)
    } catch (e) {
      if (seq === snapshotSeq.current) s.setError(`Не вдалося завантажити стан: ${(e as Error).message}`)
    } finally {
      if (seq === snapshotSeq.current) s.setLoading(false)
    }
  }, [])

  // Live: snapshot + realtime. History: snapshot at the selected instant, realtime ignored.
  useEffect(() => {
    void loadSnapshot()
  }, [loadSnapshot, mode, at])

  useEffect(() => {
    const store = useStore.getState()
    const connection = connectMapHub({
      trackUpserted: (t) => useStore.getState().upsertTrack(t),
      trackClosed: (t) => useStore.getState().upsertTrack(t),
      alertChanged: (a) => useStore.getState().upsertAlert(a),
      targetCreated: (o) => useStore.getState().addTarget(o),
      connectionChanged: (state) => {
        store.setConnection(state)
        if (state === 'connected' && useStore.getState().mode === 'live') void loadSnapshot()
      },
    })
    return () => {
      void connection.stop()
    }
  }, [loadSnapshot])

  const pickHome = picking
    ? (lon: number, lat: number) => {
        setHome({ lon, lat })
        setPicking(false)
      }
    : null

  return (
    <div className="relative h-full w-full overflow-hidden bg-slate-100 dark:bg-slate-950" data-feed={feedOpen ? 'open' : 'closed'}>
      {page === 'kyiv' ? <KyivMapView dark={mapDark} theme={theme} onDetails={() => setDetailsOpen(true)} /> : <MapView dark={mapDark} theme={theme} onPickHome={pickHome} onDetails={() => setDetailsOpen(true)} />}
      <TopBar page={page} onPage={goPage} menuOpen={panelOpen} onToggleMenu={() => setPanelOpen(!panelOpen)} onReplay={toggleReplay} replay={replay} />
      {page === 'kyiv' ? (
        <KyivPanel open={panelOpen} onClose={() => setPanelOpen(false)} />
      ) : (
        <FilterPanel open={panelOpen} onClose={() => setPanelOpen(false)} picking={picking} onPickingChange={setPicking} onReplay={() => !replay && toggleReplay()} />
      )}
      {!panelOpen && !detailsOpen && (
        <button
          className="pointer-events-auto absolute left-3 top-14 z-10 hidden rounded-lg bg-white/95 px-3 py-1.5 text-sm shadow md:block dark:bg-slate-900/95 dark:text-slate-100"
          onClick={() => setPanelOpen(true)}
          title="Показати панель фільтрів"
        >
          ☰ Фільтри
        </button>
      )}
      {replay && <ReplayBar onClose={toggleReplay} />}
      <FeedPanel open={feedOpen} onToggle={() => setFeedOpen((o) => !o)} />
      {detailsOpen && <TrackDetailsDrawer onClose={() => setDetailsOpen(false)} />}
      {selectedTrackId && !selectedTrack && !detailsOpen && (
        <div className="pointer-events-auto absolute bottom-3 left-1/2 z-20 -translate-x-1/2 rounded bg-white/90 px-3 py-1 text-xs shadow dark:bg-slate-900/90 dark:text-slate-100">
          Трек більше не відображається.{' '}
          <button className="underline" onClick={() => setDetailsOpen(true)}>
            Деталі
          </button>
        </div>
      )}
    </div>
  )
}
