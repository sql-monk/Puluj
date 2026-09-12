import * as maplibregl from 'maplibre-gl'
import type { MapLayerMouseEvent } from 'maplibre-gl'
import type { Feature, FeatureCollection, Geometry, Position } from 'geojson'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { RegionDto } from '../api/types'
import type { MapPalette } from './palette'
import RegionPopup from '../components/RegionPopup'
import TrackPopup from '../components/TrackPopup'
import { effectiveNow, useStore, type Theme } from '../store/useStore'
import { getPalette } from './palette'
import { buildAlertLayer, buildTrackLayers, emptyCollection, isPolygonAlert, visibleTracks } from './geojson'
import { ATTRIBUTION, STYLE_DARK, STYLE_LIGHT, TEXT_FONT, TRACK_HIT_LAYERS, addIcons, addTrackLayers, addTrackSources, alertPaint, pointerCursor, setData, setTrackData, trackAt } from './layers'

/** The city itself; the view opens on it with a margin of surroundings. */
const KYIV_BOUNDS: [[number, number], [number, number]] = [
  [30.23, 50.21],
  [30.83, 50.6],
]
/** How far the page lets you pan: ~80 km west/east and ~50 km north/south of the city, enough to see vectors coming in. */
const PAGE_BOUNDS: [[number, number], [number, number]] = [
  [29.3, 49.7],
  [31.8, 51.1],
]
/** Tracks are shown when their position or their forecast end falls inside this box (a bit wider than the page). */
const DATA_BOUNDS: [[number, number], [number, number]] = [
  [29.0, 49.5],
  [32.1, 51.3],
]

interface Props {
  dark: boolean
  theme: Theme
  onDetails: (trackId: number) => void
}

/** Kyiv page: the city with its ten districts and a ring of surroundings, its own MapLibre instance and layer set. */
export default function KyivMapView({ dark, theme, onDetails }: Props) {
  const container = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const [mapInstance, setMapInstance] = useState<maplibregl.Map | null>(null)
  // Where the viewer clicked to select the current track: the popup opens there, not at the marker.
  const [clickAt, setClickAt] = useState<[number, number] | null>(null)
  const styleLoaded = useRef(false)
  const palette = getPalette(theme)
  const paletteRef = useRef(palette)
  paletteRef.current = palette
  // Where the viewer clicked to select a region: the region window opens there.
  const [regionClickAt, setRegionClickAt] = useState<[number, number] | null>(null)

  const tracks = useStore((s) => s.tracks)
  const alerts = useStore((s) => s.alerts)
  const regions = useStore((s) => s.regions)
  const filters = useStore((s) => s.filters)
  const home = useStore((s) => s.home)
  const mode = useStore((s) => s.mode)
  const at = useStore((s) => s.at)
  const now = useStore((s) => s.now)
  const select = useStore((s) => s.select)
  const selectedTrackId = useStore((s) => s.selectedTrackId)
  const selectedTrack = useStore((s) => (s.selectedTrackId ? s.tracks[s.selectedTrackId] : undefined))
  const selectedRegionId = useStore((s) => s.selectedRegionId)
  const selectRegion = useStore((s) => s.selectRegion)
  const clock = effectiveNow({ mode, at, now })

  const regionsById = useMemo(() => new Map<number, RegionDto>(regions.map((r) => [r.id, r])), [regions])
  const kyiv = useMemo(() => regions.find((r) => r.level === 'City' && r.countryCode === 'UA' && r.name === 'Київ'), [regions])
  const districts = useMemo(() => regions.filter((r) => r.level === 'District' && r.parentId === kyiv?.id), [regions, kyiv])

  useEffect(() => {
    if (!container.current || mapRef.current) return
    const map = new maplibregl.Map({
      container: container.current,
      style: dark ? STYLE_DARK : STYLE_LIGHT,
      bounds: KYIV_BOUNDS,
      fitBoundsOptions: { padding: { top: 70, bottom: 40, left: 300, right: 400 } },
      maxBounds: PAGE_BOUNDS,
      minZoom: 8,
      attributionControl: { compact: true, customAttribution: ATTRIBUTION },
    })
    map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right')
    map.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-left')
    map.on('style.load', () => {
      addIcons(map, paletteRef.current)
      addLayers(map, paletteRef.current)
      styleLoaded.current = true
    })
    map.on('click', (e: MapLayerMouseEvent) => {
      const trackId = trackAt(map, e.point)
      select(trackId)
      setClickAt(trackId === null ? null : [e.lngLat.lng, e.lngLat.lat])
      setRegionClickAt(trackId === null ? [e.lngLat.lng, e.lngLat.lat] : null)
      if (trackId !== null) return
      // District under the cursor (also through an alert fill), else the city, else nothing.
      const district = map.queryRenderedFeatures(e.point, { layers: ['districts-hit'] })[0]?.properties?.id
      if (district !== undefined) {
        selectRegion(Number(district))
        return
      }
      const alert = map.queryRenderedFeatures(e.point, { layers: ['alerts-fill'] })[0]?.properties?.placeId
      selectRegion(alert === undefined ? null : Number(alert))
    })
    pointerCursor(map, [...TRACK_HIT_LAYERS, 'districts-hit', 'alerts-fill'])
    map.on('error', (e) => console.error('[kyiv-map]', e.error?.message ?? e))
    mapRef.current = map
    setMapInstance(map)
    return () => {
      map.remove()
      mapRef.current = null
      setMapInstance(null)
      styleLoaded.current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Theme switch: reload the base style so icons and layers are re-added in the new palette. Skipped on mount.
  const appliedTheme = useRef(theme)
  useEffect(() => {
    const map = mapRef.current
    if (!map || appliedTheme.current === theme) return
    appliedTheme.current = theme
    styleLoaded.current = false
    map.setStyle(dark ? STYLE_DARK : STYLE_LIGHT)
  }, [theme, dark])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const apply = () => {
      if (!map.getSource('track-points')) return
      const visible = visibleTracks(tracks, filters, clock)
      const layers = buildTrackLayers(visible, clock, regionsById, filters, { home, selectedId: selectedTrackId, palette })
      // Keep only tracks that touch the page: their marker or the end of their forecast lies inside the data box.
      const near = new Set<number>()
      for (const f of layers.points.features) if (inBox(f.geometry.coordinates)) near.add(Number(f.id))
      for (const f of layers.forecasts.features) if (f.geometry.type === 'Point' && inBox(f.geometry.coordinates)) near.add(Number(f.id))
      const only = <G extends Geometry, P>(fc: FeatureCollection<G, P>): FeatureCollection<G, P> => ({ type: 'FeatureCollection', features: fc.features.filter((f) => near.has(Number((f.properties as { id: number }).id))) })
      setTrackData(map, { points: only(layers.points), fixes: only(layers.fixes), forecasts: only(layers.forecasts), areas: only(layers.areas) })

      const alertList = filters.alerts ? Object.values(alerts) : []
      const alerted = new Set(alertList.filter((a) => isPolygonAlert(a, regionsById)).map((a) => a.placeId))
      setData(map, 'alerts', buildAlertLayer(alertList, regionsById))
      setData(map, 'kyiv', kyiv ? { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: kyiv.geometry, properties: {} }] } : emptyCollection())
      const cityAlerted = kyiv ? alerted.has(kyiv.id) : false
      setData(map, 'districts', {
        type: 'FeatureCollection',
        features: districts.map(
          (r): Feature<Geometry, { id: number; name: string; alerted: boolean }> => ({
            type: 'Feature',
            id: r.id,
            geometry: r.geometry,
            properties: { id: r.id, name: r.name.replace(' район', ''), alerted: cityAlerted || alerted.has(r.id) },
          }),
        ),
      })
      const selected = regionsById.get(selectedRegionId ?? -1)
      setData(map, 'selected-region', selected ? { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: selected.geometry, properties: {} }] } : emptyCollection())
      setData(map, 'home', home ? { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: { type: 'Point', coordinates: [home.lon, home.lat] }, properties: {} }] } : emptyCollection())
    }
    if (styleLoaded.current) apply()
    else map.once('style.load', apply)
  }, [tracks, alerts, regions, regionsById, kyiv, districts, filters, home, clock, selectedRegionId, selectedTrackId, palette])

  return (
    <div className="absolute inset-0">
      <div ref={container} className="h-full w-full" />
      {mapInstance && selectedTrack && <TrackPopup map={mapInstance} track={selectedTrack} anchor={clickAt} onDetails={() => onDetails(selectedTrack.id)} onClose={() => select(null)} />}
      {mapInstance && !selectedTrack && selectedRegionId !== null && regionClickAt && <RegionPopup map={mapInstance} placeId={selectedRegionId} anchor={regionClickAt} onClose={() => selectRegion(null)} />}
    </div>
  )
}

function inBox([lon, lat]: Position): boolean {
  return lon >= DATA_BOUNDS[0][0] && lon <= DATA_BOUNDS[1][0] && lat >= DATA_BOUNDS[0][1] && lat <= DATA_BOUNDS[1][1]
}

function addLayers(map: maplibregl.Map, p: MapPalette) {
  const empty = emptyCollection()
  map.addSource('kyiv', { type: 'geojson', data: empty })
  map.addSource('districts', { type: 'geojson', data: empty })
  map.addSource('alerts', { type: 'geojson', data: empty })
  addTrackSources(map, false)

  const firstSymbol = map.getStyle().layers.find((l) => l.type === 'symbol')?.id
  const alert = alertPaint(p)
  // Districts: own land colour, alternating slightly so neighbours are told apart; an alert replaces the fill.
  map.addLayer(
    {
      id: 'districts-fill',
      type: 'fill',
      source: 'districts',
      filter: ['!', ['get', 'alerted']],
      paint: { 'fill-color': ['case', ['==', ['%', ['get', 'id'], 2], 0], p.landAlt, p.land], 'fill-opacity': 0.75 },
    },
    firstSymbol,
  )
  map.addLayer({ id: 'alerts-fill', type: 'fill', source: 'alerts', paint: { 'fill-color': alert.fill, 'fill-opacity': 0.7 } }, firstSymbol)
  map.addLayer({ id: 'alerts-line', type: 'line', source: 'alerts', paint: { 'line-color': alert.line, 'line-opacity': 0.9, 'line-width': 1.6 } }, firstSymbol)
  map.addLayer(
    { id: 'districts-line', type: 'line', source: 'districts', layout: { 'line-join': 'round' }, paint: { 'line-color': p.oblastLine, 'line-width': 1.8, 'line-opacity': 0.95 } },
    firstSymbol,
  )
  map.addLayer({ id: 'kyiv-halo', type: 'line', source: 'kyiv', paint: { 'line-color': p.borderHalo, 'line-width': 8, 'line-opacity': 0.9 } }, firstSymbol)
  map.addLayer(
    { id: 'kyiv-line', type: 'line', source: 'kyiv', layout: { 'line-join': 'round' }, paint: { 'line-color': p.border, 'line-width': 3.5, 'line-opacity': 1 } },
    firstSymbol,
  )
  // Invisible full-coverage fill so clicks resolve to a district even where an alert fill sits on top.
  map.addLayer({ id: 'districts-hit', type: 'fill', source: 'districts', paint: { 'fill-color': '#000', 'fill-opacity': 0 } })
  map.addLayer({
    id: 'districts-label',
    type: 'symbol',
    source: 'districts',
    layout: { 'text-field': ['get', 'name'], 'text-size': 13, 'text-font': TEXT_FONT, 'text-letter-spacing': 0.04, 'text-allow-overlap': false, 'text-padding': 4 },
    paint: { 'text-color': p.border, 'text-halo-color': p.borderHalo, 'text-halo-width': 1.8 },
  })

  addTrackLayers(map, p, { labelMinZoom: 0, iconScale: 1.15 })
}
