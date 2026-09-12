// Settings UI client. The admin token (if the server has one) is kept in localStorage and sent as X-Admin-Token.

export interface SettingDto {
  key: string
  value?: string
  isSecret: boolean
  hasValue: boolean
  source: 'db' | 'config' | 'default' | 'none'
}

export interface AdminSourceDto {
  id: number
  code: string
  name: string
  type: string
  enabled: boolean
  trustLevel: number
  priority: number
  url?: string
  channel?: string
  pollingIntervalSeconds?: number
  homeRegion?: string
  /** A token is stored on the source (the value itself never comes back). */
  hasToken: boolean
  rawMessageCount: number
  lastSuccessAt?: string
  lastMessageAt?: string
  consecutiveFailures: number
  lastError?: string
  status: 'disabled' | 'idle' | 'stale' | 'ok'
}

export interface SourcePatch {
  enabled?: boolean
  trustLevel?: number
  name?: string
  priority?: number
  pollingIntervalSeconds?: number
  channel?: string
  url?: string
  homeRegion?: string
  /** API token to store on the source; empty string removes it. */
  token?: string
}

export interface AdminStatusDto {
  alertsConfigured: boolean
  telegramConfigured: boolean
  llmConfigured: boolean
  telegramStatus?: string
  adminTokenSet: boolean
  workerAlive: boolean
  workerLastSeen?: string
}

export interface TestResultDto {
  ok: boolean
  message: string
}

const TOKEN_KEY = 'puluj.adminToken'

export function getAdminToken(): string {
  try {
    return localStorage.getItem(TOKEN_KEY) ?? ''
  } catch {
    return ''
  }
}

export function setAdminToken(token: string) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    /* ignore */
  }
}

export class AdminError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function call<T>(method: string, path: string, body?: unknown, extraHeaders?: Record<string, string>): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', ...extraHeaders }
  const token = getAdminToken()
  if (token) headers['X-Admin-Token'] = token
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  if (!res.ok) {
    let msg = `HTTP ${res.status}`
    try {
      const j = (await res.json()) as { error?: string; title?: string }
      msg = j.error ?? j.title ?? msg
    } catch {
      /* no body */
    }
    throw new AdminError(res.status, msg)
  }
  // Some actions answer with an empty 200/204 body.
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const admin = {
  settings: () => call<SettingDto[]>('GET', '/api/admin/settings'),
  saveSettings: (values: Record<string, string | null>) => call<{ saved: number }>('PUT', '/api/admin/settings', { values }),
  status: () => call<AdminStatusDto>('GET', '/api/admin/status'),
  sources: () => call<AdminSourceDto[]>('GET', '/api/admin/sources'),
  updateSource: (id: number, patch: SourcePatch) => call<AdminSourceDto>('PUT', `/api/admin/sources/${id}`, patch),
  createSource: (req: { name: string; type: string; channel?: string; url?: string; trustLevel?: number; priority?: number; pollingIntervalSeconds?: number }) =>
    call<AdminSourceDto>('POST', '/api/admin/sources', req),
  deleteSource: (id: number) => call<void>('DELETE', `/api/admin/sources/${id}`),
  telegramCode: (code: string) => call<void>('POST', '/api/admin/telegram/code', { code }),
  testAlerts: (token?: string) => call<TestResultDto>('POST', '/api/admin/test/alerts', {}, token ? { 'X-Test-Token': token } : undefined),
}
