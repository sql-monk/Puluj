import * as maplibregl from 'maplibre-gl'
import type { GeoJSONSource } from 'maplibre-gl'
import type { FeatureCollection, Geometry } from 'geojson'
import { colors, vectorColors } from './geojson'

export const STYLE_LIGHT = 'https://tiles.openfreemap.org/styles/positron'
export const STYLE_DARK = 'https://tiles.openfreemap.org/styles/dark'
export const ATTRIBUTION = 'Дані: alerts.in.ua, ПС ЗСУ, OSM / geoBoundaries, GeoNames'

export function setData(map: maplibregl.Map, id: string, data: FeatureCollection<Geometry, unknown>) {
  const src = map.getSource(id) as GeoJSONSource | undefined
  src?.setData(data as FeatureCollection)
}

/** Fill / outline colours for alert polygons: yellow level = threat (drones), red or unknown level = full alert. */
export function alertPaint(dark: boolean): { fill: maplibregl.ExpressionSpecification; line: maplibregl.ExpressionSpecification } {
  return {
    fill: ['match', ['get', 'level'], 'Yellow', dark ? '#a16207' : '#fde047', dark ? '#7f1d1d' : '#f2b8b5'],
    line: ['match', ['get', 'level'], 'Yellow', dark ? '#facc15' : '#ca8a04', '#b91c1c'],
  }
}

/** Arrow / dot / chevron icons per display mode, drawn on a canvas so no sprite or font glyph is needed. */
export function addIcons(map: maplibregl.Map) {
  for (const [mode, color] of Object.entries(colors)) {
    if (!map.hasImage(`arrow-${mode}`)) map.addImage(`arrow-${mode}`, drawIcon(color, 'arrow'), { pixelRatio: 2 })
    if (!map.hasImage(`dot-${mode}`)) map.addImage(`dot-${mode}`, drawIcon(color, 'dot'), { pixelRatio: 2 })
    if (!map.hasImage(`head-${mode}`)) map.addImage(`head-${mode}`, drawIcon(vectorColors[mode] ?? color, 'head'), { pixelRatio: 2 })
  }
}

/** Marker glyphs, all pointing "up" (north); the layer rotates them by the course. White halo + dark edge keeps them
 * readable on both basemaps and over alert fills. */
function drawIcon(color: string, shape: 'arrow' | 'dot' | 'head'): ImageData {
  const size = 64
  const c = size / 2
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const ctx = canvas.getContext('2d')!
  ctx.lineJoin = 'round'
  ctx.lineCap = 'round'
  const path = () => {
    ctx.beginPath()
    if (shape === 'arrow') {
      // Delta-wing silhouette: long nose, swept wings, notched tail.
      ctx.moveTo(c, 4)
      ctx.lineTo(size - 8, size - 12)
      ctx.lineTo(c, size - 22)
      ctx.lineTo(8, size - 12)
      ctx.closePath()
    } else if (shape === 'head') {
      // Open chevron for the end of the forecast corridor.
      ctx.moveTo(10, size - 14)
      ctx.lineTo(c, 10)
      ctx.lineTo(size - 10, size - 14)
    } else {
      ctx.arc(c, c, 15, 0, Math.PI * 2)
    }
  }
  if (shape === 'head') {
    path()
    ctx.strokeStyle = 'rgba(255,255,255,0.95)'
    ctx.lineWidth = 12
    ctx.stroke()
    path()
    ctx.strokeStyle = color
    ctx.lineWidth = 6
    ctx.stroke()
    return ctx.getImageData(0, 0, size, size)
  }
  path()
  ctx.strokeStyle = 'rgba(255,255,255,0.95)'
  ctx.lineWidth = 9
  ctx.stroke()
  path()
  ctx.strokeStyle = 'rgba(0,0,0,0.8)'
  ctx.lineWidth = 3.5
  ctx.stroke()
  path()
  ctx.fillStyle = color
  ctx.fill()
  return ctx.getImageData(0, 0, size, size)
}

export const TRACK_SOURCES = ['track-areas', 'track-paths', 'track-forecasts', 'track-points', 'home'] as const

export function addTrackSources(map: maplibregl.Map, cluster = true) {
  const empty: FeatureCollection = { type: 'FeatureCollection', features: [] }
  map.addSource('selected-region', { type: 'geojson', data: empty })
  map.addSource('track-areas', { type: 'geojson', data: empty })
  map.addSource('track-paths', { type: 'geojson', data: empty })
  map.addSource('track-forecasts', { type: 'geojson', data: empty })
  map.addSource('track-points', cluster ? { type: 'geojson', data: empty, cluster: true, clusterRadius: 28, clusterMaxZoom: 7 } : { type: 'geojson', data: empty })
  map.addSource('home', { type: 'geojson', data: empty })
}

/** Track layers shared by every map: last-known area, observed path, forecast corridor + chevron, markers, labels, home. */
export function addTrackLayers(map: maplibregl.Map, dark: boolean, opts: { labelMinZoom?: number; iconScale?: number } = {}) {
  const iconScale = opts.iconScale ?? 1
  // Clicked region: bold outline above the fills, below the markers.
  map.addLayer({
    id: 'selected-region-line',
    type: 'line',
    source: 'selected-region',
    layout: { 'line-join': 'round' },
    paint: { 'line-color': dark ? '#fbbf24' : '#d97706', 'line-width': 3.5, 'line-opacity': 1 },
  })

  // Last known area for region-level reports (never a dot): outline only, so it is never mistaken for an alert.
  map.addLayer({
    id: 'track-areas',
    type: 'fill',
    source: 'track-areas',
    paint: { 'fill-color': ['get', 'color'], 'fill-opacity': ['*', 0.06, ['get', 'opacity']] },
  })
  map.addLayer({
    id: 'track-areas-line',
    type: 'line',
    source: 'track-areas',
    layout: { 'line-join': 'round' },
    paint: { 'line-color': ['get', 'color'], 'line-opacity': ['*', 0.95, ['get', 'opacity']], 'line-width': 2.5, 'line-dasharray': [2, 1.5] },
  })

  // Observed path: solid. Forecast: dashed, so fact and prediction never look alike (spec §13, §15).
  // Vectors use the saturated palette and never fade below 0.85: they must read at a glance over any fill.
  const vectorHalo = dark ? 'rgba(255,255,255,0.85)' : 'rgba(15,23,42,0.75)'
  map.addLayer({
    id: 'track-paths-halo',
    type: 'line',
    source: 'track-paths',
    layout: { 'line-cap': 'round', 'line-join': 'round' },
    paint: { 'line-color': vectorHalo, 'line-opacity': ['max', 0.85, ['get', 'opacity']], 'line-width': 8 },
  })
  map.addLayer({
    id: 'track-paths',
    type: 'line',
    source: 'track-paths',
    layout: { 'line-cap': 'round', 'line-join': 'round' },
    paint: { 'line-color': ['get', 'vector'], 'line-opacity': ['max', 0.85, ['get', 'opacity']], 'line-width': 4 },
  })
  map.addLayer({
    id: 'track-forecasts-halo',
    type: 'line',
    source: 'track-forecasts',
    filter: ['==', ['geometry-type'], 'LineString'],
    layout: { 'line-cap': 'round' },
    paint: { 'line-color': vectorHalo, 'line-opacity': ['max', 0.85, ['get', 'opacity']], 'line-width': 8 },
  })
  map.addLayer({
    id: 'track-forecasts',
    type: 'line',
    source: 'track-forecasts',
    filter: ['==', ['geometry-type'], 'LineString'],
    paint: { 'line-color': ['get', 'vector'], 'line-opacity': ['max', 0.85, ['get', 'opacity']], 'line-width': 4, 'line-dasharray': [2, 1.2] },
  })
  map.addLayer({
    id: 'track-forecast-heads',
    type: 'symbol',
    source: 'track-forecasts',
    filter: ['==', ['geometry-type'], 'Point'],
    layout: {
      'icon-image': ['concat', 'head-', ['get', 'mode']],
      'icon-size': 0.55 * iconScale,
      'icon-rotate': ['get', 'rotation'],
      'icon-rotation-alignment': 'map',
      'icon-allow-overlap': true,
      'icon-ignore-placement': true,
    },
    paint: { 'icon-opacity': ['max', 0.85, ['get', 'opacity']] },
  })

  map.addLayer({
    id: 'track-clusters',
    type: 'circle',
    source: 'track-points',
    filter: ['has', 'point_count'],
    paint: { 'circle-color': '#f59e0b', 'circle-opacity': 0.85, 'circle-radius': ['step', ['get', 'point_count'], 14, 5, 18, 20, 24], 'circle-stroke-width': 2, 'circle-stroke-color': '#fff' },
  })
  map.addLayer({
    id: 'track-cluster-count',
    type: 'symbol',
    source: 'track-points',
    filter: ['has', 'point_count'],
    layout: { 'text-field': ['get', 'point_count_abbreviated'], 'text-size': 12 },
    paint: { 'text-color': '#111' },
  })

  // Position marker with direction arrow (rotated) or a plain circle when direction is unknown.
  map.addLayer({
    id: 'track-points-halo',
    type: 'circle',
    source: 'track-points',
    filter: ['!', ['has', 'point_count']],
    paint: {
      'circle-color': ['get', 'color'],
      'circle-opacity': ['*', 0.25, ['get', 'opacity']],
      'circle-radius': ['case', ['==', ['get', 'status'], 'Active'], 18 * iconScale, 10 * iconScale],
    },
  })
  map.addLayer({
    id: 'track-points',
    type: 'symbol',
    source: 'track-points',
    filter: ['!', ['has', 'point_count']],
    layout: {
      'icon-image': ['concat', ['case', ['get', 'hasDirection'], 'arrow-', 'dot-'], ['get', 'mode']],
      'icon-size': 0.75 * iconScale,
      'icon-rotate': ['get', 'rotation'],
      'icon-rotation-alignment': 'map',
      'icon-allow-overlap': true,
      'icon-ignore-placement': true,
    },
    paint: { 'icon-opacity': ['get', 'opacity'] },
  })
  map.addLayer({
    id: 'track-labels',
    type: 'symbol',
    source: 'track-points',
    filter: ['!', ['has', 'point_count']],
    minzoom: opts.labelMinZoom ?? 6,
    layout: { 'text-field': ['get', 'label'], 'text-size': 11, 'text-offset': [0, 1.6], 'text-anchor': 'top', 'text-optional': true },
    paint: { 'text-color': '#fff', 'text-halo-color': '#000', 'text-halo-width': 1.2, 'text-opacity': ['get', 'opacity'] },
  })

  map.addLayer({
    id: 'home',
    type: 'circle',
    source: 'home',
    paint: { 'circle-color': '#22c55e', 'circle-radius': 7, 'circle-stroke-color': '#fff', 'circle-stroke-width': 2.5 },
  })
}

/** Attaches pointer cursors and returns the id of the track under a click (points and paths first, then areas). */
export function trackAt(map: maplibregl.Map, point: maplibregl.Point): number | null {
  const hit = map.queryRenderedFeatures(point, { layers: ['track-points', 'track-paths'] })[0]?.properties?.id
  if (hit !== undefined) return Number(hit)
  const area = map.queryRenderedFeatures(point, { layers: ['track-areas'] })[0]?.properties?.id
  return area === undefined ? null : Number(area)
}

export function pointerCursor(map: maplibregl.Map, layers: string[]) {
  for (const layer of layers) {
    map.on('mouseenter', layer, () => (map.getCanvas().style.cursor = 'pointer'))
    map.on('mouseleave', layer, () => (map.getCanvas().style.cursor = ''))
  }
}
