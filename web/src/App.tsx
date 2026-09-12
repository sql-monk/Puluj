import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from './api/client'
import { connectMapHub } from './api/signalr'
import FeedPanel from './components/FeedPanel'
import FilterPanel from './components/FilterPanel'
import KyivPanel from './components/KyivPanel'
import ReplayBar from './components/ReplayBar'
import TopBar, { type Page } from './components/TopBar'
import TrackCard from './components/TrackCard'
import TrackDetailsDrawer from './components/TrackDetailsDrawer'
import SettingsPage from './components/settings/SettingsPage'
import { admin } from './api/admin'
import KyivMapView from './map/KyivMapView'
import MapView from './map/MapView'
import { useStore } from './store/useStore'

const TICK_MS = 15_000

export default function App() {
  const theme = useStore((s) => s.theme)
  const mode = useStore((s) => s.mode)
  const at = useStore((s) => s.at)
  const selectedTrackId = useStore((s) => s.selectedTrackId)
  const selectedTrack = useStore((s) => (s.selectedTrackId ? s.tracks[s.selectedTrackId] : undefined))
  const select = useStore((s) => s.select)
  const setHome = useStore((s) => s.setHome)
  const [menuOpen, setMenuOpen] = useState(false)
  const [feedOpen, setFeedOpen] = useState(() => window.innerWidth >= 1024)
  const [replay, setReplay] = useState(false)
  const [picking, setPicking] = useState(false)
  const [detailsId, setDetailsId] = useState<number | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(() => window.location.hash === '#/settings')
  const [page, setPage] = useState<Page>(() => (window.location.hash === '#/kyiv' ? 'kyiv' : 'ukraine'))
  const regionsLoaded = useStore((s) => s.regions.length > 0)
  const [setupHint, setSetupHint] = useState(false)
  const [systemDark, setSystemDark] = useState(() => window.matchMedia('(prefers-color-scheme: dark)').matches)
  const dark = theme === 'dark' || (theme === 'system' && systemDark)

  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const handler = (e: MediaQueryListEvent) => setSystemDark(e.matches)
    mq.addEventListener('change', handler)
    return () => mq.removeEventListener('change', handler)
  }, [])

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
  }, [dark])

  // Fade / ETA depend on wall time: re-render every 15 s.
  useEffect(() => {
    const id = window.setInterval(() => useStore.getState().tick(), TICK_MS)
    return () => window.clearInterval(id)
  }, [])

  // Hash routes: #/settings (page over the map), #/kyiv (the Kyiv page), anything else = the country map.
  // Back/forward and reloads keep working.
  useEffect(() => {
    const onHash = () => {
      setSettingsOpen(window.location.hash === '#/settings')
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
  const openSettings = () => {
    window.location.hash = '#/settings'
  }
  const closeSettings = () => {
    window.location.hash = ''
  }

  // First-run hint: only when the admin API is reachable (localhost or token) and no source is configured yet.
  useEffect(() => {
    admin
      .status()
      .then((st) => setSetupHint(!st.alertsConfigured && !st.telegramConfigured))
      .catch(() => setSetupHint(false))
  }, [settingsOpen])

  // Region polygons (alerts, region-level markers) are loaded once.
  useEffect(() => {
    api
      .regions()
      .then((r) => useStore.getState().setRegions(r))
      .catch((e: Error) => useStore.getState().setError(`Регіони: ${e.message}`))
  }, [])

  // Snapshot requests can overlap while the timeline slider moves; only the latest one may land in the store.
  const snapshotSeq = useRef(0)
  const loadSnapshot = useCallback(async () => {
    const s = useStore.getState()
    const seq = ++snapshotSeq.current
    s.setLoading(true)
    try {
      const [snap, feed] = await Promise.all([api.snapshot(s.mode === 'history' && s.at ? s.at : undefined, false), s.mode === 'live' ? api.observations() : Promise.resolve(null)])
      if (seq !== snapshotSeq.current) return
      if (feed) useStore.getState().setObservations(feed)
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
      observationCreated: (o) => useStore.getState().addObservation(o),
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
    <div className="relative h-full w-full overflow-hidden bg-slate-100 dark:bg-slate-950">
      {page === 'kyiv' ? <KyivMapView dark={dark} /> : <MapView dark={dark} onPickHome={pickHome} />}
      <TopBar page={page} onPage={goPage} onToggleMenu={() => setMenuOpen((o) => !o)} onOpenSettings={openSettings} onReplay={toggleReplay} replay={replay} setupHint={setupHint} />
      {page === 'kyiv' ? <KyivPanel open={menuOpen} /> : <FilterPanel open={menuOpen} picking={picking} onPickingChange={setPicking} onReplay={() => !replay && toggleReplay()} />}
      {replay && !settingsOpen && <ReplayBar onClose={toggleReplay} />}
      {!settingsOpen && <FeedPanel open={feedOpen} onToggle={() => setFeedOpen((o) => !o)} />}
      {selectedTrack && !detailsId && (
        <TrackCard track={selectedTrack} onDetails={() => setDetailsId(selectedTrack.id)} onClose={() => select(null)} />
      )}
      {detailsId && <TrackDetailsDrawer trackId={detailsId} onClose={() => setDetailsId(null)} />}
      {settingsOpen && <SettingsPage onClose={closeSettings} />}
      {selectedTrackId && !selectedTrack && !detailsId && (
        <div className="pointer-events-auto absolute bottom-3 left-1/2 z-20 -translate-x-1/2 rounded bg-white/90 px-3 py-1 text-xs shadow dark:bg-slate-900/90 dark:text-slate-100">
          Трек більше не відображається.{' '}
          <button className="underline" onClick={() => setDetailsId(selectedTrackId)}>
            Деталі
          </button>
        </div>
      )}
    </div>
  )
}
