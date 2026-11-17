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
export const ATTEMPT_ERROR_PL: Record<string, string> = {
  USER_INACTIVE: 'Konto 4Money jest nieaktywne. Połączenie z bankiem nie jest możliwe.',
  USER_CANCELLED: 'Anulowano łączenie z bankiem.',
  CONSENT_DECLINED: 'Nie udzielono zgody na dostęp do danych bankowych.',
  BANK_AUTHORIZATION_FAILED: 'Autoryzacja w banku nie powiodła się. Spróbuj ponownie.',
  TINK_UNAVAILABLE: 'Dostawca połączenia z bankiem jest chwilowo niedostępny. Spróbuj ponownie później.',
  REQUEST_CANCELLED: 'Przerwano żądanie połączenia z bankiem.',
}
export const attemptErrorText = (code: string | null) => code ? ATTEMPT_ERROR_PL[code] ?? 'Nie udało się połączyć z bankiem. Spróbuj ponownie.' : 'Brak'

const REFRESH_ERROR_PL: Record<string, string> = {
  BANKING_TINK_UNAVAILABLE: 'Nie udało się połączyć z dostawcą danych bankowych. Spróbuj odświeżyć dane później.',
  BANKING_TINK_ACCESS_DENIED: 'Dostawca danych bankowych odmówił dostępu. Sprawdź stan połączenia i zgody bankowej.',
  BANKING_TINK_INVALID_DATA: 'Dostawca zwrócił nieprawidłowe dane bankowe. Spróbuj odświeżyć dane później.',
  BANKING_ATTEMPTS_EXHAUSTED: 'Nie udało się pobrać danych mimo ponawiania prób. Zleć nowe odświeżenie później.',
  BANKING_IDENTITY_CHANGED: 'Dane powiązania Twojego konta z bankiem zmieniły się lub konto 4Money jest nieaktywne. Sprawdź stan konta i połączenia.',
  BANKING_CONFIGURATION_INVALID: 'Integracja bankowa jest nieprawidłowo skonfigurowana. Skontaktuj się z obsługą 4Money.',
  BANKING_REFRESH_INTERRUPTED: 'Odświeżanie zostało przerwane. Sprawdź jego status ponownie za chwilę.',
  BANKING_REFRESH_FAILED: 'Nie udało się odświeżyć danych bankowych. Spróbuj ponownie później.',
}
export const refreshErrorText = (code: string | null) => code ? REFRESH_ERROR_PL[code.toUpperCase()] ?? 'Nie udało się odświeżyć danych bankowych. Spróbuj ponownie później.' : 'Brak'

type ProviderStatusView = { text: string; tone: 'ok' | 'warn' | 'danger' | 'info' | 'neutral' }
const PROVIDER_STATUS_PL: Record<string, ProviderStatusView> = {
  CREATED: { text: 'Utworzono', tone: 'info' },
  AUTHENTICATING: { text: 'Trwa uwierzytelnianie', tone: 'info' },
  UPDATING: { text: 'Trwa aktualizacja', tone: 'info' },
  UPDATED: { text: 'Zaktualizowano', tone: 'ok' },
  AWAITING_MOBILE_BANKID_AUTHENTICATION: { text: 'Oczekiwanie na potwierdzenie w BankID', tone: 'warn' },
  AWAITING_THIRD_PARTY_APP_AUTHENTICATION: { text: 'Oczekiwanie na potwierdzenie w aplikacji banku', tone: 'warn' },
  AWAITING_SUPPLEMENTAL_INFORMATION: { text: 'Wymagane dodatkowe informacje', tone: 'warn' },
  AWAITING_CREDENTIALS: { text: 'Oczekiwanie na dane logowania', tone: 'warn' },
  SESSION_EXPIRED: { text: 'Sesja wygasła', tone: 'warn' },
  EXPIRED: { text: 'Wygasło', tone: 'warn' },
  REVOKED: { text: 'Zgoda cofnięta', tone: 'danger' },
  AUTHENTICATION_ERROR: { text: 'Błąd uwierzytelniania', tone: 'danger' },
  TEMPORARY_ERROR: { text: 'Tymczasowy błąd dostawcy', tone: 'warn' },
  PERMANENT_ERROR: { text: 'Trwały błąd dostawcy', tone: 'danger' },
  DISABLED: { text: 'Wyłączono', tone: 'neutral' },
  DELETED: { text: 'Usunięto', tone: 'neutral' },
}
export const providerStatusView = (status: string): ProviderStatusView => PROVIDER_STATUS_PL[status.toUpperCase()] ?? { text: 'Nieznany status', tone: 'neutral' }
export const label = (map: Record<string, string>, v: string | null | undefined) => (v ? map[v] ?? v : '-')

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
  if (!Number.isFinite(n)) return '-'
  try { return n.toLocaleString('pl-PL', { style: 'currency', currency }) } catch { return `${n.toFixed(2)} ${currency}` }
}
export const money = (n: number) => fmtMoney(n, 'PLN') // Expense module amounts are PLN.
export const dateText = (iso?: string | null) => (iso ? new Date(iso).toLocaleString('pl-PL', { dateStyle: 'medium', timeStyle: 'short' }) : '-')
export const dayText = (d?: string | null) => (d ? new Date(d.length === 10 ? `${d}T12:00:00` : d).toLocaleDateString('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' }) : '-')

export const TX_STATUS_PL: Record<string, string> = { BOOKED: 'Zaksięgowana', PENDING: 'Oczekująca', CANCELLED: 'Anulowana' }
export const transactionStatus = (status: string) => TX_STATUS_PL[status.toUpperCase()] ?? status
