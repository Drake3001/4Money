import { createContext, useContext } from 'react'
import type { Res } from './api/client'
import type { Account, Attempt, Connection, Consent, Notif, Page, Refresh } from './mocks/bank'

export type RouteName =
  | 'dashboard' | 'expenses' | 'expense' | 'expense-new' | 'expense-edit' | 'groups' | 'group' | 'group-new' | 'group-edit' | 'invite'
  | 'banking' | 'account' | 'transaction' | 'connection' | 'attempt' | 'notifications' | 'profile'
  | 'login' | 'register' | 'confirm-email' | 'resend' | 'forgot' | 'reset' | 'confirm-email-change' | 'confirm-deactivation'
export type Route = { name: RouteName; id?: string; tab?: string }
export const AUTH_ROUTES: RouteName[] = ['login', 'register', 'confirm-email', 'resend', 'forgot', 'reset', 'confirm-email-change', 'confirm-deactivation']

export type NotifFilter = 'all' | 'unread' | 'read'

export type Ctx = {
  route: Route
  navigate: (r: Route) => void
  /** Returns to the screen the user came from (falls back to the dashboard). */
  goBack: () => void
  // Notifications (GET /api/notifications, PUT .../read, PUT .../read-all). The server order is fixed (newest first).
  fetchNotifications: (filter: NotifFilter, pageNumber: number, pageSize?: number) => Promise<Page<Notif>>
  /** Bumps after every successful mark-as-read so open lists reload. */
  notifVersion: number
  /** totalCount of GET /api/notifications?isRead=false (not derived from a loaded page). */
  unread: number
  markRead: (id: string) => Promise<Res>
  markAllRead: () => Promise<Res>
  // Banking
  connections: Connection[]
  consents: Consent[]
  /** All bank accounts (used for names and currency options). */
  accounts: Account[]
  bankingError: number | null
  bankingLoaded: boolean
  reloadBanking: () => Promise<void>
  attempt: Attempt | null
  /** POST /api/banking/connection-attempts; opens the Tink authorizationUrl returned by the backend. Resolves to the attempt id. */
  startAttempt: () => Promise<Res<string>>
  /** GET /api/banking/connection-attempts/{id} */
  pollAttempt: (id: string) => Promise<Attempt | null>
  lastRefresh: Refresh | null
  startRefresh: () => Promise<Res<string>>
  /** Opens the shared "Usługa tymczasowo niedostępna" dialog (the api client does this on HTTP 501). */
  showUnavailable: () => void
  toast: string | null
  showToast: (m: string) => void
}
export const AppCtx = createContext<Ctx>(null as never)
export const useApp = () => useContext(AppCtx)
