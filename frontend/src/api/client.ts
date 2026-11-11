// Central HTTP client for the 4Money backend. Every module goes through `request`.
// - Bearer token + single-flight refresh-token rotation on 401
// - HTTP 501 is detected here (and only here) and opens the shared "Usługa tymczasowo niedostępna" dialog
// - Network failures are ApiError with status 0 (never confused with an HTTP response)

export const API_BASE_URL = String(import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '')

export type Session = { accessToken: string; accessTokenExpiresAt: string; refreshToken: string; refreshTokenExpiresAt: string }
const KEY = 'fourmoney.session'

export const sessionStore = {
  get(): Session | null { try { return JSON.parse(localStorage.getItem(KEY) ?? 'null') } catch { return null } },
  set(s: Session) { localStorage.setItem(KEY, JSON.stringify(s)) },
  clear() { localStorage.removeItem(KEY) },
}

export class ApiError extends Error {
  /** HTTP status, or 0 for a network failure (no response received). */
  status: number
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
  constructor(status: number, p: { title?: string; detail?: string; code?: string; errors?: Record<string, string[]> } = {}) {
    super(p.detail ?? p.title ?? `HTTP ${status}`)
    this.status = status; this.title = p.title; this.detail = p.detail; this.code = p.code; this.errors = p.errors
  }
}

type Listener = () => void
let onUnavailable: Listener = () => {}
let onSessionLost: Listener = () => {}
/** Registered once by the app shell: opens the reusable unavailable-service dialog. */
export const setUnavailableHandler = (fn: Listener) => { onUnavailable = fn }
/** Registered once by the user store: called when the refresh token is rejected or missing. */
export const setSessionLostHandler = (fn: Listener) => { onSessionLost = fn }

type Query = Record<string, string | number | boolean | null | undefined>
export type RequestOptions = { query?: Query; body?: unknown; form?: FormData; auth?: boolean; signal?: AbortSignal }

const qs = (q?: Query) => {
  if (!q) return ''
  const p = new URLSearchParams()
  Object.entries(q).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') p.set(k, String(v)) })
  const s = p.toString()
  return s ? `?${s}` : ''
}

let refreshing: Promise<boolean> | null = null
async function refreshSession(): Promise<boolean> {
  const s = sessionStore.get()
  if (!s?.refreshToken) return false
  refreshing ??= (async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/api/users/refresh`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: s.refreshToken }) })
      if (!res.ok) return false
      sessionStore.set(await res.json())
      return true
    } catch { return false }
  })().finally(() => { refreshing = null })
  return refreshing
}

async function toError(res: Response): Promise<ApiError> {
  let body: { title?: string; detail?: string; code?: string; errors?: Record<string, string[]> } = {}
  try { body = await res.json() } catch { /* empty or non-JSON body (e.g. bare 403 from Grouping) */ }
  return new ApiError(res.status, { title: body.title, detail: body.detail, code: body.code, errors: body.errors })
}

export async function request<T = void>(method: string, path: string, opt: RequestOptions = {}, retried = false): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  let body: BodyInit | undefined
  if (opt.form) body = opt.form // the browser sets the multipart boundary
  else if (opt.body !== undefined) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(opt.body) }
  const auth = opt.auth !== false
  const session = auth ? sessionStore.get() : null
  if (session) headers.Authorization = `Bearer ${session.accessToken}`

  let res: Response
  try {
    res = await fetch(`${API_BASE_URL}${path}${qs(opt.query)}`, { method, headers, body, signal: opt.signal })
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e
    throw new ApiError(0, { title: 'network' })
  }

  if (res.status === 401 && auth && !retried) {
    if (await refreshSession()) return request<T>(method, path, opt, true)
    sessionStore.clear(); onSessionLost()
    throw await toError(res)
  }
  if (res.status === 501) { onUnavailable(); throw new ApiError(501) }
  if (!res.ok) throw await toError(res)
  if (res.status === 204 || res.status === 202 && res.headers.get('content-length') === '0') return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const api = {
  get: <T>(path: string, query?: Query, signal?: AbortSignal) => request<T>('GET', path, { query, signal }),
  post: <T = void>(path: string, body?: unknown, o: Omit<RequestOptions, 'body'> = {}) => request<T>('POST', path, { ...o, body }),
  put: <T = void>(path: string, body?: unknown, o: Omit<RequestOptions, 'body'> = {}) => request<T>('PUT', path, { ...o, body }),
  del: <T = void>(path: string, query?: Query) => request<T>('DELETE', path, { query }),
  upload: <T>(method: 'POST' | 'PUT', path: string, form: FormData) => request<T>(method, path, { form }),
}

/** Result shape used by the existing UI: `code` is the HTTP status (0 = network failure). 501 has already opened the dialog. */
export type Res<T = void> = { ok: true; data: T } | { ok: false; code: number; message?: string; errors?: Record<string, string[]>; apiCode?: string; reason?: string }
/** Most recent failed request: lets code-based error boxes append the backend's ProblemDetails text. */
export let lastProblem: { code: number; message?: string } | null = null
const validationText = (e: ApiError) => { const m = e.errors && Object.values(e.errors).flat()[0]; return m ?? undefined }
export async function run<T>(fn: () => Promise<T>): Promise<Res<T>> {
  try { return { ok: true, data: await fn() } } catch (e) {
    if (e instanceof ApiError) { lastProblem = { code: e.status, message: validationText(e) ?? e.detail ?? e.title }; return { ok: false, code: e.status, message: e.detail ?? e.title, errors: e.errors, apiCode: e.code } }
    throw e
  }
}

/** Reads every page of a paginated list (pageSize 100). */
export type Page<T> = { items: T[]; totalCount: number; pageNumber: number; pageSize: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }
export async function allPages<T>(path: string, query: Query = {}): Promise<T[]> {
  const out: T[] = []
  for (let n = 1; n < 50; n++) {
    const p = await api.get<Page<T>>(path, { ...query, pageNumber: n, pageSize: 100 })
    out.push(...p.items)
    if (!p.hasNextPage) break
  }
  return out
}
