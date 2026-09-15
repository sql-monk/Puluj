// Source analytics page client: results of the Puluj.Analytics service (who copies whom, forwards, activity, track
// firsts) and the state of the service itself. Same origin as the admin panel, same X-Admin-Token.
import { adminCall } from './admin'

export interface RunDto {
  id: number
  instance: string
  startedAt: string
  finishedAt?: string
  updatedAt: string
  status: 'running' | 'ok' | 'failed'
  watermarkFrom: number
  watermarkTo: number
  messagesScanned: number
  messagesFingerprinted: number
  pairsFound: number
  error?: string
}

export interface AnalyticsStatusDto {
  /** The service has created its schema (it has run at least once). */
  initialized: boolean
  watermark: number
  latestRawMessageId: number
  /** Raw messages the index has not reached yet. */
  backlog: number
  heartbeatAt?: string
  lastRun?: RunDto
  runs: RunDto[]
  messagesIndexed: number
  messagesFingerprinted: number
  pairsTotal: number
  schemaBytes: number
  migrations: string[]
  /** What the analytics process writes about itself (`Runtime:Worker:analytics:Status`); absent until it does. */
  instance?: AnalyticsInstanceDto | null
}

export interface AnalyticsInstanceDto {
  host?: string | null
  version?: string | null
  builtAt?: string | null
  startedAt?: string | null
  at?: string | null
  pid?: number | null
  workingSetBytes?: number | null
  cpuPercent?: number | null
  threads?: number | null
}

export interface SourceDayDto {
  day: string
  posts: number
  copies: number
  copiedBy: number
}

export interface SourceAnalyticsDto {
  id: number
  code: string
  name: string
  enabled: boolean
  /** Logical posts (edits of one post count once). */
  posts: number
  edits: number
  /** Posts that repeat an earlier post of another source. */
  copies: number
  /** Times another source repeated one of this source's posts. */
  copiedBy: number
  uniqueShare?: number | null
  avgCopyDelaySeconds?: number | null
  medianCopyDelaySeconds?: number | null
  avgLeadSeconds?: number | null
  verbatimShare?: number | null
  forwardsInternal: number
  forwardsExternal: number
  /** Posts per hour of day (Kyiv), 0..23. */
  perHour: number[]
  perDay: SourceDayDto[]
}

export interface CopyPairDto {
  copierId: number
  originalId: number
  /** Copies whose earliest original is this source. */
  count: number
  /** Including the intermediate originals (whom the copier actually reads). */
  countAll: number
  verbatim: number
  forwards: number
  avgDelaySeconds: number
  medianDelaySeconds: number
  minDelaySeconds: number
  avgJaccard: number
}

export interface ExternalForwardDto {
  sourceId: number
  channelRef: string
  count: number
}

export interface TrackFirstDto {
  sourceId: number
  categoryCode: string
  firsts: number
  participations: number
  avgLagSeconds?: number | null
}

export interface AnalyticsReportDto {
  days: string[]
  sources: SourceAnalyticsDto[]
  pairs: CopyPairDto[]
  externalForwards: ExternalForwardDto[]
  firsts: TrackFirstDto[]
}

export interface RecentCopyDto {
  copierId: number
  copyRawMessageId: number
  copyPublishedAt: string
  copyText?: string
  copyUrl?: string
  originalId: number
  originalRawMessageId: number
  originalPublishedAt: string
  originalText?: string
  originalUrl?: string
  delaySeconds: number
  jaccard: number
  containment: number
  kind: 'near' | 'verbatim' | 'forward'
  isPrimary: boolean
}

export type CopyKind = RecentCopyDto['kind']

/** One bar of the copy-delay histogram: [fromSeconds, toSeconds), the last one open-ended. */
export interface DelayBucketDto {
  fromSeconds: number
  toSeconds?: number | null
  count: number
}

/** One copier → original pair over the period: aggregates, delay distribution, the 20 latest examples. */
export interface PairDetailsDto {
  copierId: number
  originalId: number
  days: number
  count: number
  countAll: number
  verbatim: number
  forwards: number
  avgDelaySeconds?: number | null
  medianDelaySeconds?: number | null
  minDelaySeconds?: number | null
  avgJaccard?: number | null
  delays: DelayBucketDto[]
  recent: RecentCopyDto[]
}

export interface RecentFilter {
  limit?: number
  sourceId?: number
  kind?: CopyKind
  primaryOnly?: boolean
}

function query(params: Record<string, string | number | boolean | undefined>): string {
  const q = Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== false && v !== '')
    .map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`)
    .join('&')
  return q ? `?${q}` : ''
}

export const analytics = {
  status: () => adminCall<AnalyticsStatusDto>('GET', '/api/admin/analytics/status'),
  report: (days = 14) => adminCall<AnalyticsReportDto>('GET', `/api/admin/analytics/report?days=${days}`),
  recent: (f: RecentFilter = {}) => adminCall<RecentCopyDto[]>('GET', `/api/admin/analytics/recent${query({ limit: f.limit ?? 30, sourceId: f.sourceId, kind: f.kind, primaryOnly: f.primaryOnly })}`),
  pair: (copierId: number, originalId: number, days = 14) => adminCall<PairDetailsDto>('GET', `/api/admin/analytics/pairs/${copierId}/${originalId}?days=${days}`),
  reset: () => adminCall<{ ok: boolean }>('POST', '/api/admin/analytics/reset', {}),
}
