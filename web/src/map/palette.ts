import type { DisplayMode } from '../api/types'
import type { Theme } from '../store/useStore'

/**
 * Everything on the map that carries colour, per theme: class markers, movement vectors, alert fills, land and
 * borders. Each theme keeps the same reading (yellow = target level, red = alert, one colour per class) in its own
 * key, so a sepia map is not a slate map with sepia panels.
 */
export interface MapPalette {
  /** Marker colour per display mode. */
  marker: Record<DisplayMode, string>
  /** Vector (tail, forecast, chevron) colour per display mode: brighter than the marker. */
  vector: Record<DisplayMode, string>
  /** Halo around marker glyphs, so they read over any fill. */
  glyphHalo: string
  glyphEdge: string
  alertRedFill: string
  alertRedLine: string
  alertYellowFill: string
  alertYellowLine: string
  /** Oblast land fill inside Ukraine, and the alternating district fill on the Kyiv page. */
  land: string
  landAlt: string
  oblastLine: string
  border: string
  borderHalo: string
  /** Outline of the clicked region. */
  selectedRegion: string
  cluster: string
  clusterText: string
  /** Label text / halo. */
  label: string
  labelHalo: string
  home: string
  hazardNear: string
  hazardTowards: string
  selection: string
}

const light: MapPalette = {
  marker: { uav: '#f59e0b', cruise: '#ef4444', ballistic: '#a855f7', aircraft: '#3b82f6' },
  vector: { uav: '#e0a800', cruise: '#e11d1d', ballistic: '#9333ea', aircraft: '#1d6fe0' },
  glyphHalo: 'rgba(255,255,255,0.95)',
  glyphEdge: 'rgba(0,0,0,0.8)',
  alertRedFill: '#f2b8b5',
  alertRedLine: '#b91c1c',
  alertYellowFill: '#fde047',
  alertYellowLine: '#ca8a04',
  land: '#cfe3f7',
  landAlt: '#dbeafe',
  oblastLine: '#5b8fc7',
  border: '#1e3a8a',
  borderHalo: '#ffffff',
  selectedRegion: '#d97706',
  cluster: '#f59e0b',
  clusterText: '#111111',
  label: '#ffffff',
  labelHalo: '#000000',
  home: '#22c55e',
  hazardNear: '#ef4444',
  hazardTowards: '#f97316',
  selection: '#0f172a',
}

const sepia: MapPalette = {
  marker: { uav: '#c27c1a', cruise: '#b3261e', ballistic: '#7a4f9e', aircraft: '#2f6690' },
  vector: { uav: '#b26a00', cruise: '#a11a12', ballistic: '#6a3f95', aircraft: '#22557d' },
  glyphHalo: 'rgba(255,250,240,0.95)',
  glyphEdge: 'rgba(60,40,20,0.85)',
  alertRedFill: '#e9b3a0',
  alertRedLine: '#9a3412',
  alertYellowFill: '#efd985',
  alertYellowLine: '#a16207',
  land: '#efe4cc',
  landAlt: '#f4ecd9',
  oblastLine: '#a58a5c',
  border: '#5c3d1a',
  borderHalo: '#fff8ea',
  selectedRegion: '#9a5b12',
  cluster: '#c27c1a',
  clusterText: '#2b1d0e',
  label: '#fffaf0',
  labelHalo: '#2b1d0e',
  home: '#4d7c0f',
  hazardNear: '#b3261e',
  hazardTowards: '#c2410c',
  selection: '#33281a',
}

const graphite: MapPalette = {
  marker: { uav: '#e69a12', cruise: '#dc3c3c', ballistic: '#9b6ff0', aircraft: '#3d8ef0' },
  vector: { uav: '#d18b00', cruise: '#c62828', ballistic: '#8657d9', aircraft: '#2b77d6' },
  glyphHalo: 'rgba(255,255,255,0.95)',
  glyphEdge: 'rgba(30,35,45,0.85)',
  alertRedFill: '#dea19b',
  alertRedLine: '#a52a2a',
  alertYellowFill: '#e9d66b',
  alertYellowLine: '#a16207',
  land: '#d5dbe4',
  landAlt: '#e0e5ec',
  oblastLine: '#7d8796',
  border: '#2f3947',
  borderHalo: '#f5f6f8',
  selectedRegion: '#3b82f6',
  cluster: '#e69a12',
  clusterText: '#111111',
  label: '#ffffff',
  labelHalo: '#1f242c',
  home: '#16a34a',
  hazardNear: '#dc3c3c',
  hazardTowards: '#ea7a1a',
  selection: '#f5f6f8',
}

const dark: MapPalette = {
  marker: { uav: '#f59e0b', cruise: '#ef4444', ballistic: '#a855f7', aircraft: '#3b82f6' },
  vector: { uav: '#ffd400', cruise: '#ff2d2d', ballistic: '#e040fb', aircraft: '#00c2ff' },
  glyphHalo: 'rgba(255,255,255,0.95)',
  glyphEdge: 'rgba(0,0,0,0.8)',
  alertRedFill: '#7f1d1d',
  alertRedLine: '#b91c1c',
  alertYellowFill: '#a16207',
  alertYellowLine: '#facc15',
  land: '#1b3149',
  landAlt: '#1e3a5c',
  oblastLine: '#7fb3e6',
  border: '#7dd3fc',
  borderHalo: '#0b1220',
  selectedRegion: '#fbbf24',
  cluster: '#f59e0b',
  clusterText: '#111111',
  label: '#ffffff',
  labelHalo: '#000000',
  home: '#22c55e',
  hazardNear: '#ef4444',
  hazardTowards: '#f97316',
  selection: '#f8fafc',
}

const midnight: MapPalette = {
  marker: { uav: '#fbbf24', cruise: '#fb7185', ballistic: '#c084fc', aircraft: '#38bdf8' },
  vector: { uav: '#ffd54a', cruise: '#ff8fa3', ballistic: '#d8a3ff', aircraft: '#67d2ff' },
  glyphHalo: 'rgba(238,243,255,0.95)',
  glyphEdge: 'rgba(4,10,28,0.9)',
  alertRedFill: '#6b1d2e',
  alertRedLine: '#f43f5e',
  alertYellowFill: '#6f5416',
  alertYellowLine: '#facc15',
  land: '#12213f',
  landAlt: '#172a4d',
  oblastLine: '#5b7fc2',
  border: '#8fb6ff',
  borderHalo: '#040a1c',
  selectedRegion: '#fbbf24',
  cluster: '#fbbf24',
  clusterText: '#0a1530',
  label: '#eef3ff',
  labelHalo: '#040a1c',
  home: '#34d399',
  hazardNear: '#fb7185',
  hazardTowards: '#fb923c',
  selection: '#eef3ff',
}

const olive: MapPalette = {
  marker: { uav: '#e8c547', cruise: '#e0655c', ballistic: '#b48fe0', aircraft: '#7ec8e3' },
  vector: { uav: '#f5d95a', cruise: '#ff7b70', ballistic: '#caa6f2', aircraft: '#9adcf2' },
  glyphHalo: 'rgba(242,245,234,0.95)',
  glyphEdge: 'rgba(13,18,9,0.9)',
  alertRedFill: '#6e2a22',
  alertRedLine: '#e06b5f',
  alertYellowFill: '#6b6118',
  alertYellowLine: '#d9c94a',
  land: '#1e2a1a',
  landAlt: '#243120',
  oblastLine: '#6f8560',
  border: '#b7cf8f',
  borderHalo: '#0d120a',
  selectedRegion: '#e8c547',
  cluster: '#e8c547',
  clusterText: '#181f12',
  label: '#f2f5ea',
  labelHalo: '#0d120a',
  home: '#9ccc3a',
  hazardNear: '#e0655c',
  hazardTowards: '#e8963a',
  selection: '#f2f5ea',
}

const palettes: Record<Theme, MapPalette> = { light, sepia, graphite, dark, midnight, olive }

export function getPalette(theme: Theme): MapPalette {
  return palettes[theme] ?? light
}

export const DISPLAY_MODES: DisplayMode[] = ['uav', 'cruise', 'ballistic', 'aircraft']
