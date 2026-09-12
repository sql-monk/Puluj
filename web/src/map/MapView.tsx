import * as maplibregl from 'maplibre-gl'
import type { MapLayerMouseEvent } from 'maplibre-gl'
// maplibre-gl v6 resolves its worker with a dynamic `new URL(...)` that bundlers cannot follow; Vite bundles the
// worker entry explicitly here and MapLibre is pointed at it.
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { RegionDto } from '../api/types'
import type { MapPalette } from './palette'
import RegionPopup from '../components/RegionPopup'
import TrackPopup from '../components/TrackPopup'
import { effectiveNow, useStore, type Theme } from '../store/useStore'
import { getPalette } from './palette'
import { buildAlertLayer, buildTrackLayers, emptyCollection, isPolygonAlert, visibleTracks } from './geojson'
import { ATTRIBUTION, STYLE_DARK, STYLE_LIGHT, TRACK_HIT_LAYERS, addIcons, addTrackLayers, addTrackSources, alertPaint, pointerCursor, setData, setTrackData, trackAt } from './layers'

maplibregl.setWorkerUrl(maplibreWorkerUrl)

const UKRAINE_CENTER: [number, number] = [31.2, 48.8]

interface Props {
  dark: boolean
  theme: Theme
  onPickHome: ((lon: number, lat: number) => void) | null
  onDetails: (trackId: number) => void
}

/** Country-wide MapLibre map with all Puluj layers. Data flows one way: store -> GeoJSON sources. */
export default function MapView({ dark, theme, onPickHome, onDetails }: Props) {
  const container = useRef<HTMLDivElement>(null)
  const mapRef = useRef<maplibregl.Map | null>(null)
  const [mapInstance, setMapInstance] = useState<maplibregl.Map | null>(null)
  // Where the viewer clicked to select the current track: the popup opens there, not at the marker.
  const [clickAt, setClickAt] = useState<[number, number] | null>(null)
  const styleLoaded = useRef(false)
  const pickRef = useRef(onPickHome)
  pickRef.current = onPickHome
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

  useEffect(() => {
    if (!container.current || mapRef.current) return
    const map = new maplibregl.Map({
      container: container.current,
      style: dark ? STYLE_DARK : STYLE_LIGHT,
      center: UKRAINE_CENTER,
      zoom: 5.2,
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
      if (pickRef.current) {
        pickRef.current(e.lngLat.lng, e.lngLat.lat)
        return
      }
      const trackId = trackAt(map, e.point)
      select(trackId)
      setClickAt(trackId === null ? null : [e.lngLat.lng, e.lngLat.lat])
      setRegionClickAt(trackId === null ? [e.lngLat.lng, e.lngLat.lat] : null)
      if (trackId !== null) return
      // No marker under the cursor: (de)select the oblast for the feed filter and outline highlight.
      const ob = map.queryRenderedFeatures(e.point, { layers: ['alerts-fill', 'oblasts-fill'] })[0]
      const regionId = ob?.layer.id === 'alerts-fill' ? ob.properties?.placeId : ob?.properties?.id
      selectRegion(regionId === undefined ? null : Number(regionId))
    })
    pointerCursor(map, [...TRACK_HIT_LAYERS, 'oblasts-fill', 'alerts-fill'])
    map.on('error', (e) => console.error('[map]', e.error?.message ?? e))
    if (import.meta.env.DEV) Object.assign(window, { __map: map, __maplibre: maplibregl })
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

  // Theme switch: swap the base style, layers are re-added on style.load. Skipped on the initial mount.
  // Theme switch: reload the base style so icons and layers are re-added in the new palette. Skipped on mount.
  const appliedTheme = useRef(theme)
  useEffect(() => {
    const map = mapRef.current
    if (!map || appliedTheme.current === theme) return
    appliedTheme.current = theme
    styleLoaded.current = false
    map.setStyle(dark ? STYLE_DARK : STYLE_LIGHT)
  }, [theme, dark])

  // Crosshair cursor while picking a home point.
  useEffect(() => {
    const map = mapRef.current
    if (map) map.getCanvas().style.cursor = onPickHome ? 'crosshair' : ''
  }, [onPickHome])

  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const apply = () => {
      if (!map.getSource('track-points')) return
      const visible = visibleTracks(tracks, filters, clock)
      setTrackData(map, buildTrackLayers(visible, clock, regionsById, filters, { home, selectedId: selectedTrackId, palette }))
      // An alerted oblast is drawn by the alert layer instead of the base fill, so the colours never blend.
      const alertList = filters.alerts ? Object.values(alerts) : []
      const alerted = new Set(alertList.filter((a) => isPolygonAlert(a, regionsById)).map((a) => a.placeId))
      setData(map, 'alerts', buildAlertLayer(alertList, regionsById))
      const ukraine = regions.find((r) => r.level === 'Country' && r.countryCode === 'UA')
      setData(map, 'ukraine', ukraine ? { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: ukraine.geometry, properties: {} }] } : emptyCollection())
      // Oblast polygons inside Ukraine (regions + Kyiv/Sevastopol city-regions): filled with our own land colour.
      const oblasts = regions.filter((r) => r.countryCode === 'UA' && (r.level === 'Region' || r.level === 'City'))
      setData(map, 'oblasts', { type: 'FeatureCollection', features: oblasts.map((r) => ({ type: 'Feature', geometry: r.geometry, properties: { id: r.id, alerted: alerted.has(r.id) } })) })
      const selected = regionsById.get(selectedRegionId ?? -1)
      setData(map, 'selected-region', selected ? { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: selected.geometry, properties: {} }] } : emptyCollection())
      setData(map, 'home', home ? { type: 'FeatureCollection', features: [{ type: 'Feature', geometry: { type: 'Point', coordinates: [home.lon, home.lat] }, properties: {} }] } : emptyCollection())
    }
    if (styleLoaded.current) apply()
    else map.once('style.load', apply)
  }, [tracks, alerts, regions, regionsById, filters, home, clock, selectedRegionId, selectedTrackId, palette])

  // MapLibre's own (unlayered) CSS sets position on .maplibregl-map and would override Tailwind's layered
  // utilities, so the positioned wrapper is a separate element.
  return (
    <div className="absolute inset-0">
      <div ref={container} className="h-full w-full" />
      {mapInstance && selectedTrack && <TrackPopup map={mapInstance} track={selectedTrack} anchor={clickAt} onDetails={() => onDetails(selectedTrack.id)} onClose={() => select(null)} />}
      {mapInstance && !selectedTrack && selectedRegionId !== null && regionClickAt && <RegionPopup map={mapInstance} placeId={selectedRegionId} anchor={regionClickAt} onClose={() => selectRegion(null)} />}
    </div>
  )
}

function addLayers(map: maplibregl.Map, p: MapPalette) {
  const empty = emptyCollection()
  map.addSource('ukraine', { type: 'geojson', data: empty })
  map.addSource('oblasts', { type: 'geojson', data: empty })
  map.addSource('alerts', { type: 'geojson', data: empty })
  addTrackSources(map)

  // Ukraine gets its own land colour, oblast borders and a firm state border with a contrasting halo.
  // These go *under* the basemap's label layers so place names stay readable.
  const firstSymbol = map.getStyle().layers.find((l) => l.type === 'symbol')?.id
  map.addLayer(
    { id: 'oblasts-fill', type: 'fill', source: 'oblasts', filter: ['!', ['get', 'alerted']], paint: { 'fill-color': p.land, 'fill-opacity': 0.7 } },
    firstSymbol,
  )
  map.addLayer(
    { id: 'oblasts-line', type: 'line', source: 'oblasts', paint: { 'line-color': p.oblastLine, 'line-width': 0.9, 'line-opacity': 0.6 } },
    firstSymbol,
  )
  map.addLayer({ id: 'ukraine-halo', type: 'line', source: 'ukraine', paint: { 'line-color': p.borderHalo, 'line-width': 7, 'line-opacity': 0.9 } }, firstSymbol)
  map.addLayer(
    {
      id: 'ukraine-line',
      type: 'line',
      source: 'ukraine',
      layout: { 'line-join': 'round', 'line-cap': 'round' },
      paint: { 'line-color': p.border, 'line-width': 3.2, 'line-opacity': 1 },
    },
    firstSymbol,
  )

  // Air-raid alerts replace (not tint) the oblast fill: same opacity, own colour per level.
  const alert = alertPaint(p)
  map.addLayer({ id: 'alerts-fill', type: 'fill', source: 'alerts', paint: { 'fill-color': alert.fill, 'fill-opacity': 0.7 } }, firstSymbol)
  map.addLayer({ id: 'alerts-line', type: 'line', source: 'alerts', paint: { 'line-color': alert.line, 'line-opacity': 0.9, 'line-width': 1.4 } }, firstSymbol)

  addTrackLayers(map, p)
}
