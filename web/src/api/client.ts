import type { AlertDto, TargetDto, PlaceDto, RegionDto, SnapshotDto, SourceDto, TimelineBucketDto, TrackDetailsDto } from './types'

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path, { headers: { Accept: 'application/json' } })
  if (!res.ok) {
    throw new Error(`${path}: HTTP ${res.status}`)
  }
  return (await res.json()) as T
}

export const api = {
  snapshot: (at?: Date, activeOnly = true) =>
    get<SnapshotDto>(`/api/snapshot?activeOnly=${activeOnly}${at ? `&at=${encodeURIComponent(at.toISOString())}` : ''}`),
  track: (id: number) => get<TrackDetailsDto>(`/api/tracks/${id}`),
  targets: (sinceHours = 6, limit = 300) =>
    get<TargetDto[]>(`/api/targets?since=${encodeURIComponent(new Date(Date.now() - sinceHours * 3600_000).toISOString())}&limit=${limit}`),
  /** Every target inside a replay window, newest first. */
  targetsBetween: (from: Date, to: Date, limit = 5000) =>
    get<TargetDto[]>(`/api/targets?since=${encodeURIComponent(from.toISOString())}&until=${encodeURIComponent(to.toISOString())}&limit=${limit}`),
  regions: () => get<RegionDto[]>('/api/places/regions'),
  sources: () => get<SourceDto[]>('/api/sources'),
  /** Alerts of one place over the last `hours`, ended ones included, newest first. */
  alertsHistory: (placeId: number, hours = 24) => get<AlertDto[]>(`/api/alerts/history?placeId=${placeId}&hours=${hours}`),
  searchPlaces: (q: string) => get<PlaceDto[]>(`/api/places/search?q=${encodeURIComponent(q)}&limit=8`),
  timeline: (from: Date, to: Date, bucketMinutes: number) =>
    get<TimelineBucketDto[]>(
      `/api/timeline?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}&bucketMinutes=${bucketMinutes}`,
    ),
}
