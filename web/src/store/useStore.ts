import { create } from 'zustand'
import type { AlertDto, DisplayMode, ObservationDto, RegionDto, TrackDto } from '../api/types'
import type { Home } from '../eta/computeEta'

export type Mode = 'live' | 'history'
export type Theme = 'light' | 'dark' | 'system'
export type Connection = 'connected' | 'reconnecting' | 'disconnected'

export interface Filters {
  uav: boolean
  cruise: boolean
  ballistic: boolean
  aircraft: boolean
  alerts: boolean
  activeOnly: boolean
}

interface State {
  tracks: Record<number, TrackDto>
  alerts: Record<number, AlertDto>
  regions: RegionDto[]
  mode: Mode
  at: Date | null
  now: Date
  connection: Connection
  filters: Filters
  home: Home | null
  theme: Theme
  selectedTrackId: number | null
  /** Feed of recent observations, newest first (live mode only). */
  observations: ObservationDto[]
  /** Oblast clicked on the map: highlighted border + feed filter. */
  selectedRegionId: number | null
  loading: boolean
  error: string | null

  setSnapshot: (tracks: TrackDto[], alerts: AlertDto[]) => void
  upsertTrack: (t: TrackDto) => void
  upsertAlert: (a: AlertDto) => void
  setRegions: (r: RegionDto[]) => void
  setMode: (mode: Mode, at?: Date | null) => void
  tick: () => void
  setConnection: (c: Connection) => void
  setFilter: (key: keyof Filters, value: boolean) => void
  setHome: (h: Home | null) => void
  setTheme: (t: Theme) => void
  select: (id: number | null) => void
  setObservations: (list: ObservationDto[]) => void
  addObservation: (o: ObservationDto) => void
  selectRegion: (id: number | null) => void
  setLoading: (v: boolean) => void
  setError: (e: string | null) => void
}

const HOME_KEY = 'puluj.home'
const THEME_KEY = 'puluj.theme'
const FILTERS_KEY = 'puluj.filters'

function load<T>(key: string, fallback: T): T {
  try {
    const raw = localStorage.getItem(key)
    if (!raw) return fallback
    const parsed = JSON.parse(raw) as T
    // Objects are merged over the defaults so new keys get their default; primitives are taken as-is.
    return typeof fallback === 'object' && fallback !== null && typeof parsed === 'object' && parsed !== null ? { ...fallback, ...parsed } : parsed
  } catch {
    return fallback
  }
}

function save(key: string, value: unknown) {
  try {
    if (value === null) localStorage.removeItem(key)
    else localStorage.setItem(key, JSON.stringify(value))
  } catch {
    /* private mode etc. */
  }
}

export const defaultFilters: Filters = { uav: true, cruise: true, ballistic: true, aircraft: false, alerts: true, activeOnly: true }

export const useStore = create<State>((set) => ({
  tracks: {},
  alerts: {},
  regions: [],
  mode: 'live',
  at: null,
  now: new Date(),
  connection: 'disconnected',
  filters: load(FILTERS_KEY, defaultFilters),
  home: load<Home | null>(HOME_KEY, null),
  theme: load<Theme>(THEME_KEY, 'system'),
  selectedTrackId: null,
  observations: [],
  selectedRegionId: null,
  loading: false,
  error: null,

  setSnapshot: (tracks, alerts) =>
    set({
      tracks: Object.fromEntries(tracks.map((t) => [t.id, t])),
      alerts: Object.fromEntries(alerts.map((a) => [a.id, a])),
    }),
  upsertTrack: (t) => set((s) => (s.mode === 'live' ? { tracks: { ...s.tracks, [t.id]: t } } : {})),
  upsertAlert: (a) =>
    set((s) => {
      if (s.mode !== 'live') return {}
      const alerts = { ...s.alerts }
      if (a.endedAt) delete alerts[a.id]
      else alerts[a.id] = a
      return { alerts }
    }),
  setRegions: (regions) => set({ regions }),
  // Scrubbing inside history keeps the selected track; crossing live<->history drops it (ids may not exist there).
  setMode: (mode, at = null) => set((s) => ({ mode, at, selectedTrackId: s.mode === mode ? s.selectedTrackId : null })),
  tick: () => set({ now: new Date() }),
  setConnection: (connection) => set({ connection }),
  setFilter: (key, value) =>
    set((s) => {
      const filters = { ...s.filters, [key]: value }
      save(FILTERS_KEY, filters)
      return { filters }
    }),
  setHome: (home) => {
    save(HOME_KEY, home)
    set({ home })
  },
  setTheme: (theme) => {
    save(THEME_KEY, theme)
    set({ theme })
  },
  select: (selectedTrackId) => set({ selectedTrackId }),
  setObservations: (observations) => set({ observations }),
  addObservation: (o) =>
    set((s) => (s.mode === 'live' && !s.observations.some((x) => x.id === o.id) ? { observations: [o, ...s.observations].slice(0, 500) } : {})),
  selectRegion: (selectedRegionId) => set({ selectedRegionId }),
  setLoading: (loading) => set({ loading }),
  setError: (error) => set({ error }),
}))

export function displayModeEnabled(mode: DisplayMode, f: Filters): boolean {
  switch (mode) {
    case 'uav':
      return f.uav
    case 'cruise':
      return f.cruise
    case 'ballistic':
      return f.ballistic
    case 'aircraft':
      return f.aircraft
  }
}

/** The "clock" of the map: wall time in live mode, the selected instant in history mode. */
export function effectiveNow(s: Pick<State, 'mode' | 'at' | 'now'>): Date {
  return s.mode === 'history' && s.at ? s.at : s.now
}
