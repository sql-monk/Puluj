import { create } from 'zustand'
import type { AlertDto, DisplayMode, ObservationDto, RegionDto, SourceDto, TrackDto } from '../api/types'
import type { Home } from '../eta/computeEta'
import { getPalette, type MapPalette } from '../map/palette'

export type Mode = 'live' | 'history'
export type Theme = 'light' | 'sepia' | 'graphite' | 'dark' | 'midnight' | 'olive'

/**
 * Colour themes: the palette lives in index.css (data-theme on <html>). `dark` = light text on dark panels
 * (the `dark` class), `mapDark` = the dark basemap. Two light, one in between (dark panels over a light map), two dark.
 */
export const THEMES: { id: Theme; label: string; dark: boolean; mapDark: boolean }[] = [
  { id: 'light', label: 'Світла', dark: false, mapDark: false },
  { id: 'sepia', label: 'Сепія (світла, тепла)', dark: false, mapDark: false },
  { id: 'graphite', label: 'Графіт (середня)', dark: true, mapDark: false },
  { id: 'dark', label: 'Темна', dark: true, mapDark: true },
  { id: 'midnight', label: 'Опівнічна (темна, синя)', dark: true, mapDark: true },
  { id: 'olive', label: 'Олива (темна, зелена)', dark: true, mapDark: true },
]

export function themeIsDark(theme: Theme): boolean {
  return THEMES.find((t) => t.id === theme)?.dark ?? false
}

export function themeMapIsDark(theme: Theme): boolean {
  return THEMES.find((t) => t.id === theme)?.mapDark ?? false
}
export type Connection = 'connected' | 'reconnecting' | 'disconnected'

export interface Filters {
  uav: boolean
  cruise: boolean
  ballistic: boolean
  aircraft: boolean
  alerts: boolean
  activeOnly: boolean
  /** Crumbs (earlier reported positions with times) for every target, not only the selected one. */
  crumbs: boolean
  /** Forecast cone and dashed centreline ahead of the marker. */
  forecast: boolean
  /** Highlight tracks near the viewer's point or heading towards it (needs a home point). */
  threats: boolean
  /** Source ids to show; null = every source. Tracks need at least one selected source, feed items their own. */
  sources: number[] | null
  /** How long after its last message a target stays on the map, minutes. */
  lifetimeMinutes: number
}

interface State {
  tracks: Record<number, TrackDto>
  alerts: Record<number, AlertDto>
  regions: RegionDto[]
  sources: SourceDto[]
  mode: Mode
  at: Date | null
  now: Date
  connection: Connection
  filters: Filters
  home: Home | null
  theme: Theme
  /** Left panel (filters) shown; persisted so it stays hidden once the viewer folds it. */
  panelOpen: boolean
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
  setSources: (s: SourceDto[]) => void
  setMode: (mode: Mode, at?: Date | null) => void
  tick: () => void
  setConnection: (c: Connection) => void
  setFilter: <K extends keyof Filters>(key: K, value: Filters[K]) => void
  setHome: (h: Home | null) => void
  setTheme: (t: Theme) => void
  setPanelOpen: (open: boolean) => void
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
const PANEL_KEY = 'puluj.panel'

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

export const defaultFilters: Filters = {
  uav: true,
  cruise: true,
  ballistic: true,
  aircraft: false,
  alerts: true,
  activeOnly: true,
  crumbs: false,
  forecast: true,
  threats: true,
  sources: null,
  lifetimeMinutes: 15,
}

export const useStore = create<State>((set) => ({
  tracks: {},
  alerts: {},
  regions: [],
  sources: [],
  mode: 'live',
  at: null,
  now: new Date(),
  connection: 'disconnected',
  filters: load(FILTERS_KEY, defaultFilters),
  home: load<Home | null>(HOME_KEY, null),
  // Unknown or retired ids (the old "system") fall back to the plain dark theme.
  theme: ((t) => (THEMES.some((x) => x.id === t) ? t : 'dark'))(load<Theme>(THEME_KEY, 'dark')),
  // Phones start with the panel folded (it is a bottom sheet there); desktops start with it open.
  panelOpen: load<boolean>(PANEL_KEY, typeof window !== 'undefined' && window.innerWidth >= 768),
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
  setSources: (sources) => set({ sources }),
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
  setPanelOpen: (panelOpen) => {
    save(PANEL_KEY, panelOpen)
    set({ panelOpen })
  },
  select: (selectedTrackId) => set({ selectedTrackId }),
  setObservations: (observations) => set({ observations }),
  addObservation: (o) =>
    set((s) => (s.mode === 'live' && !s.observations.some((x) => x.id === o.id) ? { observations: [o, ...s.observations].slice(0, 500) } : {})),
  selectRegion: (selectedRegionId) => set({ selectedRegionId }),
  setLoading: (loading) => set({ loading }),
  setError: (error) => set({ error }),
}))

if (import.meta.env.DEV) Object.assign(window, { __store: useStore })

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

/** Does the source filter let this source through? */
export function sourceEnabled(sourceId: number, f: Filters): boolean {
  return f.sources === null || f.sources.includes(sourceId)
}

/** The "clock" of the map: wall time in live mode, the selected instant in history mode. */
export function effectiveNow(s: Pick<State, 'mode' | 'at' | 'now'>): Date {
  return s.mode === 'history' && s.at ? s.at : s.now
}

/** The map palette of the current theme (markers, vectors, alert fills, land). */
export function usePalette(): MapPalette {
  return getPalette(useStore((s) => s.theme))
}
