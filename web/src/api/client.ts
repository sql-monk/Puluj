import type { ObservationDto, PlaceDto, RegionDto, SnapshotDto, TimelineBucketDto, TrackDetailsDto } from './types'

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
  observations: (sinceHours = 6, limit = 300) =>
    get<ObservationDto[]>(`/api/observations?since=${encodeURIComponent(new Date(Date.now() - sinceHours * 3600_000).toISOString())}&limit=${limit}`),
  /** Every observation inside a replay window, newest first. */
  observationsBetween: (from: Date, to: Date, limit = 5000) =>
    get<ObservationDto[]>(`/api/observations?since=${encodeURIComponent(from.toISOString())}&until=${encodeURIComponent(to.toISOString())}&limit=${limit}`),
  regions: () => get<RegionDto[]>('/api/places/regions'),
  searchPlaces: (q: string) => get<PlaceDto[]>(`/api/places/search?q=${encodeURIComponent(q)}&limit=8`),
  timeline: (from: Date, to: Date, bucketMinutes: number) =>
    get<TimelineBucketDto[]>(
      `/api/timeline?from=${encodeURIComponent(from.toISOString())}&to=${encodeURIComponent(to.toISOString())}&bucketMinutes=${bucketMinutes}`,
    ),
}
