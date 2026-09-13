import destination from '@turf/destination'
import distance from '@turf/distance'
import { point } from '@turf/helpers'
import type { Feature, FeatureCollection, GeoJsonProperties, Geometry, LineString, Point, Polygon, MultiPolygon, Position } from 'geojson'
import type { AlertDto, AlertLevel, Confidence, RegionDto, TrackDto } from '../api/types'
import { computeEta, distanceToRegionKm, type Home } from '../eta/computeEta'
import { displayModeEnabled, type Filters } from '../store/useStore'

import { getPalette, type MapPalette } from './palette'

/** Why a track is highlighted for the viewer's own point: it is close by, or it is heading this way. */
export type HazardKind = 'near' | 'towards' | ''

export interface TrackProps {
  id: number
  label: string
  mode: string
  color: string
  /** Colour of the movement vector (brighter than the marker colour). */
  vector: string
  opacity: number
  rotation: number
  hasDirection: boolean
  status: string
  kind: string
  /** Position is an approach-zone anchor ("на Конотоп"), not a fix. */
  approx: boolean
  ageMin: number
  /** Independent sources behind the track (badge). */
  sources: number
  /** Objects in the group when the report counted more than one (badge), else 0. */
  count: number
  hazard: HazardKind
  selected: boolean
  /** Reported in the same message as the selected target. */
  neighbor: boolean
}

/** A crumb: where the target was reported earlier, with the time; or the dotted link between crumbs. */
export interface FixProps {
  id: number
  mode: string
  vector: string
  /** "Ромни 21:40" — the place (if known) and the time of that report. */
  label: string
  opacity: number
  approach: boolean
  selected: boolean
}

export interface TrackLayers {
  points: FeatureCollection<Point, TrackProps>
  /** Crumbs (points) and the dotted links from crumb to crumb to the marker. */
  fixes: FeatureCollection<Point | LineString, FixProps>
  /** Dashed forecast centreline, hatched probability cone and the chevron at its end. */
  forecasts: FeatureCollection<LineString | Point | Polygon, TrackProps>
  areas: FeatureCollection<Polygon | MultiPolygon, TrackProps>
}

/** The forecast reaches this far ahead: a short pointer, not a flight plan. */
const FORECAST_MINUTES = 6
const MIN_FORECAST_KM = 8
const DEFAULT_FORECAST_KM = 20
/** The tail shows at most this much of the observed path behind the marker, and at most TAIL_SEGMENTS legs. */
export const TAIL_MAX_KM = 25
export const TAIL_SEGMENTS = 3
/** Half-angle of the forecast cone by confidence in the reported course. */
const CONE_HALF_ANGLE: Record<Confidence, number> = { Confirmed: 10, High: 12, Medium: 18, Low: 28, Unknown: 28 }
/** A track this close to the viewer's point counts as "near" regardless of its course. */
const NEAR_KM = 25

export interface TrackLayerOptions {
  home?: Home | null
  selectedId?: number | null
  /** Theme palette; the light one when omitted. */
  palette?: MapPalette
}

/** Tracks that pass the class, status and source filters and are not fully faded. */
/** Minutes since the track's last message (never negative). */
export function ageMinutes(t: TrackDto, now: Date): number {
  return Math.max(0, (now.getTime() - new Date(t.lastSeenAt).getTime()) / 60000)
}

export function visibleTracks(tracks: Record<number, TrackDto>, filters: Filters, now: Date): TrackDto[] {
  return Object.values(tracks).filter((t) => {
    if (!displayModeEnabled(t.type.displayMode, filters)) return false
    if (filters.activeOnly && t.status !== 'Active') return false
    if (filters.sources !== null && !t.sourceIds.some((id) => filters.sources!.includes(id))) return false
    // A target lives on the map for the viewer's chosen time after its last message, whatever its status.
    return ageMinutes(t, now) <= filters.lifetimeMinutes
  })
}

/**
 * Is the track close to the viewer's point, or plausibly heading towards it? Pure, per track: cheap enough per render.
 * "Near" for a region-level report means the point lies inside that region (its polygon, when known): the region's
 * covering radius would otherwise call a whole neighbouring oblast "near".
 */
export function hazardKind(t: TrackDto, home: Home, now: Date, regionsById?: Map<number, RegionDto>): HazardKind {
  const loc = t.lastLocation
  if (!loc?.point || t.status !== 'Active') return ''
  const km = distance(point(loc.point.coordinates), point([home.lon, home.lat]), { units: 'kilometers' })
  let near: boolean
  if (loc.kind === 'Region' || loc.kind === 'Area') {
    // Inside the region, or within NEAR_KM of its edge (Kyiv city is a hole in the Kyiv oblast polygon).
    const region = loc.placeId ? regionsById?.get(loc.placeId) : undefined
    const edge = region ? distanceToRegionKm(home, region.geometry) : null
    near = edge !== null ? edge <= NEAR_KM : km <= NEAR_KM
  } else {
    near = km <= NEAR_KM + Math.min(loc.accuracyKm ?? 0, NEAR_KM)
  }
  if (near) return 'near'
  const eta = computeEta(t, home, now, regionsById)
  return eta.kind === 'range' || eta.kind === 'imminent' ? 'towards' : ''
}

export function buildTrackLayers(tracks: TrackDto[], now: Date, regionsById: Map<number, RegionDto>, filters: Filters, opts: TrackLayerOptions = {}): TrackLayers {
  const points: Feature<Point, TrackProps>[] = []
  const fixes: Feature<Point | LineString, FixProps>[] = []
  const forecasts: Feature<LineString | Point | Polygon, TrackProps>[] = []
  const areas: Feature<Polygon | MultiPolygon, TrackProps>[] = []
  const home = filters.highlightTargets ? (opts.home ?? null) : null
  const palette = opts.palette ?? getPalette('light')
  // Targets listed in the same message as the selected one light up with it.
  const selectedTrack = opts.selectedId == null ? undefined : tracks.find((t) => t.id === opts.selectedId)
  const selectedMessages = new Set(selectedTrack?.messageIds ?? [])

  for (const t of tracks) {
    // Fades over the viewer's lifetime setting, never below 0.45: a faint marker is unreadable, and the age is on the card.
    const opacity = t.status === 'Active' ? Math.max(0.45, 1 - 0.55 * (ageMinutes(t, now) / Math.max(1, filters.lifetimeMinutes))) : 0.25
    const selected = opts.selectedId === t.id
    const props: TrackProps = {
      id: t.id,
      label: t.type.label,
      mode: t.type.displayMode,
      color: palette.marker[t.type.displayMode] ?? palette.marker.uav,
      vector: palette.vector[t.type.displayMode] ?? palette.vector.uav,
      opacity,
      rotation: t.direction?.degrees ?? 0,
      hasDirection: !!t.direction,
      status: t.status,
      kind: t.lastLocation?.kind ?? 'Unknown',
      approx: t.lastLocation?.kind === 'DirectionOnly',
      ageMin: Math.round((now.getTime() - new Date(t.lastSeenAt).getTime()) / 60000),
      sources: Math.max(t.distinctSourceCount, t.sourceIds.length),
      count: t.objectCount && t.objectCount > 1 ? t.objectCount : 0,
      hazard: home ? hazardKind(t, home, now, regionsById) : '',
      selected,
      neighbor: !selected && selectedMessages.size > 0 && t.messageIds.some((m) => selectedMessages.has(m)),
    }
    const loc = t.lastLocation
    if (loc?.point) {
      points.push({ type: 'Feature', id: t.id, geometry: loc.point, properties: props })

      // Region / area level locations are drawn as the area itself, never as a precise dot (spec §6).
      if ((loc.kind === 'Region' || loc.kind === 'Area') && loc.placeId) {
        const region = regionsById.get(loc.placeId)
        if (region && (region.geometry.type === 'Polygon' || region.geometry.type === 'MultiPolygon')) {
          areas.push({ type: 'Feature', id: t.id, geometry: region.geometry as Polygon | MultiPolygon, properties: props })
        }
      }

      // Forecast: reported course projected a few minutes ahead at the class speed. Dashed centreline (clearly "forecast")
      // inside a hatched cone whose width says how sure the course is; chevron at the end for the heading.
      if (filters.forecast && t.direction && t.type.displayMode !== 'ballistic' && t.status === 'Active') {
        const speed = t.type.speedProfile.maxKmh
        const km = speed ? Math.max(MIN_FORECAST_KM, (speed * FORECAST_MINUTES) / 60) : DEFAULT_FORECAST_KM
        const origin = loc.point.coordinates
        const end = destination(point(origin), km, t.direction.degrees, { units: 'kilometers' })
        forecasts.push({ type: 'Feature', id: t.id, geometry: cone(origin, km, t.direction.degrees, CONE_HALF_ANGLE[t.direction.confidence]), properties: props })
        forecasts.push({ type: 'Feature', id: t.id, geometry: { type: 'LineString', coordinates: [origin, end.geometry.coordinates] }, properties: props })
        forecasts.push({ type: 'Feature', id: t.id, geometry: end.geometry, properties: props })
      }
    }
    // Crumbs: the earlier reported positions, each with its time, linked by a dotted line up to the marker.
    // Shown for the selected target, or for every target when the viewer asks for it.
    if ((selected || filters.crumbs) && loc?.point && t.fixes.length >= 2) {
      const previous = t.fixes.slice(0, -1)
      const chain: Position[] = [...previous.map((f) => f.point.coordinates), loc.point.coordinates]
      previous.forEach((f, i) => {
        const rank = previous.length - i // 1 = the most recent crumb
        const time = new Date(f.at).toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' })
        fixes.push({
          type: 'Feature',
          id: t.id * 10 + i,
          geometry: f.point,
          properties: { id: t.id, mode: props.mode, vector: props.vector, label: `${f.approach ? '→ ' : ''}${f.placeName ?? ''} ${time}`.trim(), opacity: Math.max(0.35, 0.85 - 0.2 * (rank - 1)), approach: f.approach, selected },
        })
      })
      fixes.push({ type: 'Feature', id: t.id, geometry: { type: 'LineString', coordinates: chain }, properties: { id: t.id, mode: props.mode, vector: props.vector, label: '', opacity: 0.7, approach: false, selected } })
    }
  }
  return {
    points: { type: 'FeatureCollection', features: points },
    fixes: { type: 'FeatureCollection', features: fixes },
    forecasts: { type: 'FeatureCollection', features: forecasts },
    areas: { type: 'FeatureCollection', features: areas },
  }
}

/** Sector of a circle: where the object may be after the forecast period if it holds roughly the reported course. */
function cone(origin: Position, km: number, bearing: number, halfAngle: number, steps = 8): Polygon {
  const ring: Position[] = [origin]
  for (let i = 0; i <= steps; i++) {
    const b = bearing - halfAngle + (2 * halfAngle * i) / steps
    ring.push(destination(point(origin), km, b, { units: 'kilometers' }).geometry.coordinates)
  }
  ring.push(origin)
  return { type: 'Polygon', coordinates: [ring] }
}

export interface AlertProps {
  id: number
  placeId: number
  placeName: string
  alertType: string
  level: AlertLevel
  startedAt: string
}

/** Alerts drawn as their oblast polygon; the rest (raion towns) become circles of the stated radius. */
export function isPolygonAlert(a: AlertDto, regionsById: Map<number, RegionDto>): boolean {
  return regionsById.has(a.placeId)
}

function circle(center: Position, km: number, steps = 48): Polygon {
  const ring: Position[] = []
  for (let i = 0; i <= steps; i++) {
    ring.push(destination(point(center), km, (i * 360) / steps, { units: 'kilometers' }).geometry.coordinates)
  }
  return { type: 'Polygon', coordinates: [ring] }
}

export function buildAlertLayer(alerts: AlertDto[], regionsById: Map<number, RegionDto>): FeatureCollection<Geometry, AlertProps> {
  const features: Feature<Geometry, AlertProps>[] = []
  for (const a of alerts) {
    const region = regionsById.get(a.placeId)
    const geometry: Geometry | null = region
      ? region.geometry
      : a.location?.point
        ? circle(a.location.point.coordinates, Math.max(a.location.accuracyKm ?? 0, 20))
        : null
    if (!geometry) continue
    features.push({
      type: 'Feature',
      id: a.id,
      geometry,
      properties: { id: a.id, placeId: a.placeId, placeName: a.placeName, alertType: a.alertType, level: a.level ?? 'Unknown', startedAt: a.startedAt },
    })
  }
  return { type: 'FeatureCollection', features }
}

export function emptyCollection(): FeatureCollection<Geometry, GeoJsonProperties> {
  return { type: 'FeatureCollection', features: [] }
}
