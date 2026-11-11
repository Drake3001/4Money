// UI-side types, labels, formatters and query models for Banking and Notifications. No data lives here.

// ---------- Banking DTOs
export type Account = {
  id: string; name: string; number: string | null; iban: string | null; financialInstitutionId: string | null; type: string | null
  currencyCode: string; isClosed: boolean; lastSuccessfulSyncAt: string | null; transactionsSyncedThrough: string | null
}
// Amount is decimal text with sign; date is a calendar date (no time of day); status is a free string.
export type Tx = { id: string; bankAccountId: string; amount: string; currencyCode: string; date: string; description: string | null; status: string }

export type ConnectionStatus = string // CONNECTED | REAUTH_REQUIRED | DISCONNECTED (unknown values are shown as received)
export type SyncStatus = string // NOT_STARTED | QUEUED | RUNNING | SUCCEEDED | FAILED
export type Connection = {
  id: string; providerName: string | null; connectionStatus: ConnectionStatus; syncStatus: SyncStatus; createdAt: string; connectedAt: string | null
  consentExpiresAt: string | null; lastSuccessfulSyncAt: string | null; lastSyncFailureCode: string | null
}
export type Consent = { connectionId: string; providerStatus: string; sessionExpiresAt: string | null; sessionExtendable: boolean | null }

export type AttemptStatus = string // PENDING | COMPLETED | CANCELLED | FAILED | EXPIRED
export type Attempt = { id: string; status: AttemptStatus; connectionId: string | null; createdAt: string; expiresAt: string; completedAt: string | null; failureCode: string | null }

export type RefreshAccount = { accountId: string; status: string; addedTransactions: number; updatedTransactions: number; lastSuccessfulSyncAt: string | null; failureCode: string | null }
// Refresh status is a backend string; no fixed enum is assumed.
export type Refresh = {
  refreshId: string; status: string; createdAt: string; startedAt: string | null; completedAt: string | null
  totalAccounts: number; successfulAccounts: number; failedAccounts: number; failureCode: string | null; accounts: RefreshAccount[]
}

// ---------- Labels for known backend values (unknown values are shown as received)
export const CONNECTION_STATUS_PL: Record<string, string> = { CONNECTED: 'Połączono', REAUTH_REQUIRED: 'Wymaga ponownej autoryzacji', DISCONNECTED: 'Rozłączono' }
export const SYNC_STATUS_PL: Record<string, string> = { NOT_STARTED: 'Nie rozpoczęto', QUEUED: 'W kolejce', RUNNING: 'W trakcie', SUCCEEDED: 'Zakończona pomyślnie', FAILED: 'Niepowodzenie' }
export const ATTEMPT_STATUS_PL: Record<string, string> = { PENDING: 'W trakcie', COMPLETED: 'Połączono', CANCELLED: 'Anulowano', FAILED: 'Niepowodzenie', EXPIRED: 'Wygasło' }
export const label = (map: Record<string, string>, v: string | null | undefined) => (v ? map[v] ?? v : '—')

// ---------- List queries (mock of the paginated/filterable endpoints)
export type Page<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }
export type AccountQuery = { pageNumber: number; pageSize: number; name: string; currency: string; type: string; isClosed: '' | 'yes' | 'no'; sortBy: 'name' | 'currencyCode' | 'isClosed'; descending: boolean }
export const DEFAULT_ACCOUNT_QUERY: AccountQuery = { pageNumber: 1, pageSize: 20, name: '', currency: '', type: '', isClosed: '', sortBy: 'name', descending: false }
export type TxQuery = { pageNumber: number; pageSize: number; bankAccountId: string; dateFrom: string; dateTo: string; minAmount: string; maxAmount: string; currency: string; description: string; status: string; sortBy: 'date' | 'amount' | 'status' | 'currency'; descending: boolean }
export const DEFAULT_TX_QUERY: TxQuery = { pageNumber: 1, pageSize: 20, bankAccountId: '', dateFrom: '', dateTo: '', minAmount: '', maxAmount: '', currency: '', description: '', status: '', sortBy: 'date', descending: true }
export const isValidTxQuery = (q: TxQuery) => {
  const n = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')))
  const a = n(q.minAmount), b = n(q.maxAmount)
  return !(Number.isNaN(a) || Number.isNaN(b) || (a !== null && b !== null && a > b) || (q.dateFrom && q.dateTo && q.dateFrom > q.dateTo))
}

// ---------- Notifications DTO
// type is serialized numerically: 0 General, 1 ExpenseAdded, 2 LimitApproaching, 3 LimitExceeded, 4 GroupMemberJoined, 5 SettlementReceived.
export type Notif = { id: string; title: string; text: string; type: number; createdAt: string; groupId: string | null; expenseId: string | null; readAt: string | null; isRead: boolean }
export const NOTIFICATION_TYPE_PL: Record<number, string> = { 0: 'Ogólne', 1: 'Dodano wydatek', 2: 'Zbliżenie do limitu', 3: 'Przekroczono limit', 4: 'Nowy członek grupy', 5: 'Otrzymano rozliczenie' }

// ---------- Formatting
export const fmtMoney = (amount: string | number, currency: string) => {
  const n = Number(amount)
  if (!Number.isFinite(n)) return '—'
  try { return n.toLocaleString('pl-PL', { style: 'currency', currency }) } catch { return `${n.toFixed(2)} ${currency}` }
}
export const money = (n: number) => fmtMoney(n, 'PLN') // Expense module amounts are PLN.
export const dateText = (iso?: string | null) => (iso ? new Date(iso).toLocaleString('pl-PL', { dateStyle: 'medium', timeStyle: 'short' }) : '—')
export const dayText = (d?: string | null) => (d ? new Date(d.length === 10 ? `${d}T12:00:00` : d).toLocaleDateString('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' }) : '—')
