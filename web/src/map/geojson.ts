import destination from '@turf/destination'
import { point } from '@turf/helpers'
import type { Feature, FeatureCollection, GeoJsonProperties, Geometry, LineString, Point, Polygon, MultiPolygon, Position } from 'geojson'
import type { AlertDto, AlertLevel, RegionDto, TrackDto } from '../api/types'
import { fadeOpacity } from '../eta/computeEta'
import { displayModeEnabled, type Filters } from '../store/useStore'

export const colors: Record<string, string> = {
  uav: '#f59e0b',
  cruise: '#ef4444',
  ballistic: '#a855f7',
  aircraft: '#3b82f6',
}

/** Saturated variants for the movement vectors (path + forecast + arrowhead): they must read at a glance over any fill. */
export const vectorColors: Record<string, string> = {
  uav: '#ffd400',
  cruise: '#ff2d2d',
  ballistic: '#e040fb',
  aircraft: '#00c2ff',
}

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
  ageMin: number
}

export interface TrackLayers {
  points: FeatureCollection<Point, TrackProps>
  paths: FeatureCollection<LineString, TrackProps>
  forecasts: FeatureCollection<LineString | Point, TrackProps>
  areas: FeatureCollection<Polygon | MultiPolygon, TrackProps>
}

const FORECAST_MINUTES = 30
const DEFAULT_FORECAST_KM = 100

export function visibleTracks(tracks: Record<number, TrackDto>, filters: Filters, now: Date): TrackDto[] {
  return Object.values(tracks).filter((t) => {
    if (!displayModeEnabled(t.threat.displayMode, filters)) return false
    if (filters.activeOnly && t.status !== 'Active') return false
    // Even closed tracks disappear once fully faded.
    return fadeOpacity(t.lastSeenAt, t.threat.fadeMinutes, now) > 0 || t.status === 'Active'
  })
}

export function buildTrackLayers(tracks: TrackDto[], now: Date, regionsById: Map<number, RegionDto>): TrackLayers {
  const points: Feature<Point, TrackProps>[] = []
  const paths: Feature<LineString, TrackProps>[] = []
  const forecasts: Feature<LineString | Point, TrackProps>[] = []
  const areas: Feature<Polygon | MultiPolygon, TrackProps>[] = []

  for (const t of tracks) {
    // Active tracks never fade below 0.45: a faint marker is unreadable, and the age is written on the card anyway.
    const opacity = t.status === 'Active' ? Math.max(0.45, fadeOpacity(t.lastSeenAt, t.threat.fadeMinutes, now)) : 0.25
    const props: TrackProps = {
      id: t.id,
      label: t.threat.label,
      mode: t.threat.displayMode,
      color: colors[t.threat.displayMode] ?? colors.uav,
      vector: vectorColors[t.threat.displayMode] ?? vectorColors.uav,
      opacity,
      rotation: t.direction?.degrees ?? 0,
      hasDirection: !!t.direction,
      status: t.status,
      kind: t.lastLocation?.kind ?? 'Unknown',
      ageMin: Math.round((now.getTime() - new Date(t.lastSeenAt).getTime()) / 60000),
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

      // Forecast corridor: reported direction projected for 30 minutes at the class speed (dashed, clearly "forecast").
      if (t.direction && t.threat.displayMode !== 'ballistic' && t.status === 'Active') {
        const speed = t.threat.speedProfile.maxKmh
        const km = speed ? (speed * FORECAST_MINUTES) / 60 : DEFAULT_FORECAST_KM
        const end = destination(point(loc.point.coordinates), km, t.direction.degrees, { units: 'kilometers' })
        forecasts.push({
          type: 'Feature',
          id: t.id,
          geometry: { type: 'LineString', coordinates: [loc.point.coordinates, end.geometry.coordinates] },
          properties: props,
        })
        // Arrowhead at the end of the corridor so the course reads at a glance.
        forecasts.push({ type: 'Feature', id: t.id, geometry: end.geometry, properties: props })
      }
    }
    if (t.trackGeometry && t.trackGeometry.coordinates.length >= 2) {
      paths.push({ type: 'Feature', id: t.id, geometry: t.trackGeometry, properties: props })
    }
  }
  return {
    points: { type: 'FeatureCollection', features: points },
    paths: { type: 'FeatureCollection', features: paths },
    forecasts: { type: 'FeatureCollection', features: forecasts },
    areas: { type: 'FeatureCollection', features: areas },
  }
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
