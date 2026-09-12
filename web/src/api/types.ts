// Mirrors src/Puluj.Contracts/Dtos.cs (camelCase, nulls omitted by the API).
import type { Geometry, LineString, Point } from 'geojson'

export type Confidence = 'Unknown' | 'Low' | 'Medium' | 'High' | 'Confirmed'
export type LocationKind = 'Unknown' | 'DirectionOnly' | 'Region' | 'District' | 'City' | 'Area' | 'Point'
export type DirectionKind = 'Unknown' | 'Compass' | 'TowardsPlace'
export type TrackStatus = 'Active' | 'Closed' | 'Cancelled'
export type DisplayMode = 'uav' | 'cruise' | 'ballistic' | 'aircraft'

export interface SpeedProfile {
  minKmh?: number
  maxKmh?: number
  etaEnabled: boolean
}

export interface ThreatDto {
  categoryCode: string
  categoryName: string
  classCode?: string
  className?: string
  familyCode?: string
  familyName?: string
  modelCode?: string
  modelName?: string
  displayMode: DisplayMode
  fadeMinutes: number
  speedProfile: SpeedProfile
  label: string
}

export interface LocationDto {
  kind: LocationKind
  placeId?: number
  placeName?: string
  regionId?: number
  regionName?: string
  point?: Point
  accuracyKm?: number
}

export interface DirectionDto {
  degrees: number
  kind: DirectionKind
  confidence: Confidence
}

/** One earlier reported position of a track: the crumbs drawn behind the marker. */
export interface FixDto {
  at: string
  placeName?: string
  kind: LocationKind
  point: Point
  accuracyKm?: number
  /** The report only named a destination: the object was on its way to this place. */
  approach: boolean
}

export interface TrackDto {
  id: number
  status: TrackStatus
  closedReason?: string
  threat: ThreatDto
  modelConfidence: Confidence
  trackConfidence: Confidence
  firstSeenAt: string
  lastSeenAt: string
  updatedAt: string
  lastLocation?: LocationDto
  trackGeometry?: LineString
  direction?: DirectionDto
  objectCount?: number
  observationCount: number
  distinctSourceCount: number
  /** Ids of the sources whose observations make up the track (feeds the per-source filter and the badge). */
  sourceIds: number[]
  /** The last few distinct reported positions, oldest first, the current one last. */
  fixes: FixDto[]
  /** Raw messages behind the newest observations: tracks sharing one are neighbours by message. */
  messageIds: number[]
}

export type AlertLevel = 'Unknown' | 'Yellow' | 'Red'

export interface AlertDto {
  id: number
  placeId: number
  placeName: string
  alertType: string
  /** Yellow / Red where the administration publishes levels; Unknown for plain on/off alerts (drawn as red). */
  level: AlertLevel
  startedAt: string
  endedAt?: string
  /** Point + radius for places without a polygon (raion towns). */
  location?: LocationDto
}

export interface SnapshotDto {
  at: string
  historical: boolean
  tracks: TrackDto[]
  alerts: AlertDto[]
}

export interface SourceDto {
  id: number
  code: string
  name: string
  type: string
  trustLevel: number
  url?: string
}

export interface RawMessageDto {
  id: number
  sourceMessageId: string
  publishedAt: string
  receivedAt: string
  text?: string
  url?: string
}

export interface ObservationDto {
  id: number
  observedAt: string
  eventType: string
  threat?: ThreatDto
  modelConfidence: Confidence
  classificationConfidence: Confidence
  observationConfidence: Confidence
  location?: LocationDto
  origin?: LocationDto
  destination?: LocationDto
  direction?: DirectionDto
  objectCount?: number
  objectCountIsApproximate: boolean
  identificationMethod: string
  identificationSource?: string
  segmentText?: string
  duplicateOfObservationId?: number
  associationConfidence?: number
  source: SourceDto
  rawMessage: RawMessageDto
  /** Track the observation was attached to (feed highlighting), if any. */
  trackId?: number
}

export interface TrackDetailsDto {
  track: TrackDto
  observations: ObservationDto[]
}

export interface PlaceDto {
  id: number
  name: string
  level: string
  parentId?: number
  parentName?: string
  lon: number
  lat: number
  radiusKm: number
  population: number
}

export interface RegionDto {
  id: number
  name: string
  level: string
  countryCode: string
  /** Set for city districts (Kyiv): the city-region they belong to. */
  parentId?: number
  geometry: Geometry
}

export interface TimelineBucketDto {
  from: string
  observations: number
  tracksOpened: number
  alerts: number
}
