import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { AppCtx, type Ctx, type NotifFilter, type Route } from './context'
import { setUnavailableHandler, run } from './api/client'
import { banking } from './api/banking'
import { notifications } from './api/notifications'
import { useUser } from './userContext'
import { UnavailableDialog } from './ui'
import { initialRoute, clearEmailLink } from './emailLinks'
import type { Attempt, Connection, Consent, Refresh, Account } from './mocks/bank'

const ATTEMPT_KEY = 'fourmoney.lastAttempt'
const REFRESH_KEY = 'fourmoney.lastRefresh'
const TERMINAL_REFRESH = ['SUCCEEDED', 'PARTIALLY_SUCCEEDED', 'FAILED']

// Navigation, notifications and Banking over the real API. All data comes from HTTP responses; nothing is simulated.
export function Provider({ children }: { children: ReactNode }) {
  const user = useUser()
  const signedIn = user.isSignedIn
  const [route, setRoute] = useState<Route>(() => initialRoute(window.location))
  const [unread, setUnread] = useState(0)
  const [notifVersion, setNotifVersion] = useState(0)
  const [connections, setConnections] = useState<Connection[]>([])
  const [consents, setConsents] = useState<Consent[]>([])
  const [accounts, setAccounts] = useState<Account[]>([])
  const [bankingError, setBankingError] = useState<number | null>(null)
  const [bankingLoaded, setBankingLoaded] = useState(false)
  const [attempt, setAttempt] = useState<Attempt | null>(null)
  const [lastRefresh, setLastRefresh] = useState<Refresh | null>(null)
  const [toast, setToast] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)

  const current = useRef<Route>(route)
  const previous = useRef<Route | null>(null)
  const navigate = useCallback((r: Route) => {
    if (new URLSearchParams(window.location.search).has('action')) clearEmailLink()
    previous.current = current.current; current.current = r; setRoute(r)
  }, [])
  const goBack = useCallback(() => navigate(previous.current && previous.current.name !== 'expense-new' ? previous.current : { name: 'expenses' }), [navigate])
  const showToast = useCallback((m: string) => { setToast(m); setTimeout(() => setToast(null), 3200) }, [])
  const showUnavailable = useCallback(() => setUnavailable(true), [])
  // HTTP 501 detected by the api client opens the shared dialog.
  useEffect(() => { setUnavailableHandler(showUnavailable) }, [showUnavailable])

  // ---- Notifications: the unread count is the totalCount of GET /api/notifications?isRead=false
  const refreshUnread = useCallback(async () => {
    const r = await run(() => notifications.list(false, 1, 1))
    if (r.ok) setUnread(r.data.totalCount)
  }, [])
  const fetchNotifications = useCallback((filter: NotificationFilterArg, pageNumber: number, pageSize = 20) =>
    notifications.list(filter === 'all' ? undefined : filter === 'read', pageNumber, pageSize), [])
  const markRead = useCallback(async (id: string) => {
    const r = await run(() => notifications.markRead(id))
    if (r.ok) { setNotifVersion((v) => v + 1); void refreshUnread() }
    return r
  }, [refreshUnread])
  const markAllRead = useCallback(async () => {
    const r = await run(() => notifications.markAllRead())
    if (r.ok) { setNotifVersion((v) => v + 1); void refreshUnread() }
    return r
  }, [refreshUnread])
  useEffect(() => { if (signedIn) void refreshUnread() }, [signedIn, route.name, refreshUnread])

  // ---- Banking
  const reloadBanking = useCallback(async () => {
    const r = await run(async () => Promise.all([banking.connections(), banking.consents(), banking.allAccounts()]))
    if (r.ok) { setConnections(r.data[0]); setConsents(r.data[1] as Consent[]); setAccounts(r.data[2] as Account[]); setBankingError(null) } else setBankingError(r.code)
    setBankingLoaded(true)
  }, [])
  useEffect(() => {
    if (!signedIn) { setConnections([]); setConsents([]); setAccounts([]); setAttempt(null); setLastRefresh(null); setBankingLoaded(false); return }
    void reloadBanking()
    // Resume the last attempt / refresh started from this browser (no list endpoints exist for them).
    const a = localStorage.getItem(ATTEMPT_KEY); if (a) void run(() => banking.attempt(a)).then((r) => { if (r.ok) setAttempt(r.data as Attempt) })
    const f = localStorage.getItem(REFRESH_KEY); if (f) void run(() => banking.refresh(f)).then((r) => { if (r.ok) setLastRefresh(r.data) })
  }, [signedIn, reloadBanking])

  // POST /api/banking/connection-attempts has no body; the user authorises the bank at the returned Tink URL.
  const startAttempt = useCallback(async () => {
    const tab = window.open('', '_blank') // opened synchronously so the popup is not blocked; pointed at the real URL below
    const r = await run(() => banking.startConnection())
    if (!r.ok) { tab?.close(); return r }
    localStorage.setItem(ATTEMPT_KEY, r.data.attemptId)
    setAttempt({ id: r.data.attemptId, status: 'PENDING', connectionId: null, createdAt: new Date().toISOString(), expiresAt: r.data.expiresAt, completedAt: null, failureCode: null })
    if (tab) tab.location.href = r.data.authorizationUrl; else window.location.assign(r.data.authorizationUrl)
    return { ok: true as const, data: r.data.attemptId }
  }, [])

  const pollAttempt = useCallback(async (id: string) => {
    const r = await run(() => banking.attempt(id))
    if (!r.ok) return null
    setAttempt(r.data as Attempt)
    if (r.data.status === 'COMPLETED') void reloadBanking()
    return r.data
  }, [reloadBanking])
  useEffect(() => {
    if (!attempt || attempt.status !== 'PENDING') return
    const t = setInterval(() => { void pollAttempt(attempt.id) }, 3000)
    return () => clearInterval(t)
  }, [attempt?.id, attempt?.status, pollAttempt]) // eslint-disable-line react-hooks/exhaustive-deps

  const startRefresh = useCallback(async () => {
    const r = await run(() => banking.startRefresh())
    if (!r.ok) return r
    localStorage.setItem(REFRESH_KEY, r.data.refreshId)
    setLastRefresh({ refreshId: r.data.refreshId, status: r.data.status, createdAt: r.data.createdAt, startedAt: null, completedAt: null, totalAccounts: 0, successfulAccounts: 0, failedAccounts: 0, failureCode: null, accounts: [] })
    return { ok: true as const, data: r.data.refreshId }
  }, [])
  const refreshStatus = lastRefresh?.status
  const refreshId = lastRefresh?.refreshId
  useEffect(() => {
    if (!refreshId || !refreshStatus || TERMINAL_REFRESH.includes(refreshStatus)) return
    const t = setInterval(async () => {
      const r = await run(() => banking.refresh(refreshId))
      if (!r.ok) return
      setLastRefresh(r.data)
      if (TERMINAL_REFRESH.includes(r.data.status)) { void reloadBanking(); showToast(r.data.status === 'SUCCEEDED' ? 'Odświeżanie danych zakończone' : 'Odświeżanie zakończone z błędami lub niepowodzeniem') }
    }, 2500)
    return () => clearInterval(t)
  }, [refreshId, refreshStatus, reloadBanking, showToast])

  const value = useMemo<Ctx>(() => ({
    route, navigate, goBack, fetchNotifications, notifVersion, unread, markRead, markAllRead,
    connections, consents, accounts, bankingError, bankingLoaded, reloadBanking, attempt, startAttempt, pollAttempt, lastRefresh, startRefresh, showUnavailable, toast, showToast,
  }), [route, navigate, goBack, fetchNotifications, notifVersion, unread, markRead, markAllRead, connections, consents, accounts, bankingError, bankingLoaded, reloadBanking, attempt, startAttempt, pollAttempt, lastRefresh, startRefresh, showUnavailable, toast, showToast])

  return <AppCtx.Provider value={value}>{children}{unavailable && <UnavailableDialog onClose={() => setUnavailable(false)} />}</AppCtx.Provider>
}
type NotificationFilterArg = NotifFilter
