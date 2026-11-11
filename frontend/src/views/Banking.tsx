import { useState } from 'react'
import { ArrowLeft, ChevronRight, Landmark, Loader2, Plus, RefreshCw } from 'lucide-react'
import { useApp } from '../context'
import { banking } from '../api/banking'
import { describeError } from '../api/errors'
import { useRemote } from '../api/hooks'
import {
  ATTEMPT_STATUS_PL, CONNECTION_STATUS_PL, DEFAULT_ACCOUNT_QUERY, DEFAULT_TX_QUERY, SYNC_STATUS_PL, dateText, dayText, fmtMoney, isValidTxQuery, label,
  type AccountQuery, type Page, type Tx, type TxQuery,
} from '../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, PageTitle, Pager, Segmented, Table, Td, cx, inputCls } from '../ui'

const Back = ({ to, text }: { to: Parameters<ReturnType<typeof useApp>['navigate']>[0]; text: string }) => {
  const { navigate } = useApp()
  return <button onClick={() => navigate(to)} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />{text}</button>
}
// Banking shows each amount in its own currency; amounts of different currencies are never summed.
export const TxAmount = ({ t }: { t: Tx }) => <span className={cx('num whitespace-nowrap font-semibold', Number(t.amount) > 0 && 'text-safe')}>{Number(t.amount) > 0 ? '+' : ''}{fmtMoney(t.amount, t.currencyCode)}</span>
const useAccounts = () => useApp().accounts
const useAccountName = () => { const a = useAccounts(); return (id: string) => a.find((x) => x.id === id)?.name ?? id }
const useCurrencies = () => [...new Set(useAccounts().map((a) => a.currencyCode))]
const emptyPage = <T,>(n: number, size: number): Page<T> => ({ items: [], pageNumber: n, pageSize: size, totalCount: 0, totalPages: 1, hasPreviousPage: false, hasNextPage: false })
// Request failure or loading state shown above a list (501 shows the shared dialog instead).
function Status({ loading, error, retry }: { loading: boolean; error: number | null; retry: () => void }) {
  const msg = error !== null ? describeError(error) : null
  if (msg) return <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{msg} <button className="font-semibold underline" onClick={retry}>Spróbuj ponownie</button></p>
  return loading ? <p className="p-4 text-center text-sm text-ink2">Ładowanie…</p> : null
}

const connTone = (s: string) => (s === 'CONNECTED' ? 'ok' : s === 'REAUTH_REQUIRED' ? 'warn' : 'neutral') as 'ok' | 'warn' | 'neutral'
const syncTone = (s: string) => (s === 'SUCCEEDED' ? 'ok' : s === 'FAILED' ? 'danger' : s === 'RUNNING' || s === 'QUEUED' ? 'info' : 'neutral') as 'ok' | 'danger' | 'info' | 'neutral'
const attemptTone = (s: string) => (s === 'COMPLETED' ? 'ok' : s === 'FAILED' ? 'danger' : s === 'PENDING' ? 'info' : 'neutral') as 'ok' | 'danger' | 'info' | 'neutral'

/* ---------------- Accounts ---------------- */
function AccountsTab() {
  const { navigate } = useApp()
  const [q, setQ] = useState<AccountQuery>(DEFAULT_ACCOUNT_QUERY)
  const set = (p: Partial<AccountQuery>) => setQ((x) => ({ ...x, pageNumber: 1, ...p }))
  const CURRENCIES = useCurrencies()
  const rq = useRemote(() => banking.accounts(q), [JSON.stringify(q)])
  const page = (rq.data ?? emptyPage(q.pageNumber, q.pageSize)) as Page<import('../mocks/bank').Account>
  return (
    <div className="space-y-4">
      <Card className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field label="Nazwa"><input value={q.name} onChange={(e) => set({ name: e.target.value })} className={inputCls} /></Field>
        <Field label="Waluta"><select value={q.currency} onChange={(e) => set({ currency: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{CURRENCIES.map((c) => <option key={c}>{c}</option>)}</select></Field>
        <Field label="Typ"><input value={q.type} onChange={(e) => set({ type: e.target.value })} className={inputCls} /></Field>
        <Field label="Stan konta"><select value={q.isClosed} onChange={(e) => set({ isClosed: e.target.value as AccountQuery['isClosed'] })} className={inputCls}><option value="">Wszystkie</option><option value="no">Otwarte</option><option value="yes">Zamknięte</option></select></Field>
        <Field label="Sortuj wg"><select value={q.sortBy} onChange={(e) => set({ sortBy: e.target.value as AccountQuery['sortBy'] })} className={inputCls}><option value="name">Nazwa</option><option value="currencyCode">Waluta</option><option value="isClosed">Zamknięte</option></select></Field>
        <Field label="Kolejność"><select value={q.descending ? 'desc' : 'asc'} onChange={(e) => set({ descending: e.target.value === 'desc' })} className={inputCls}><option value="asc">Rosnąco</option><option value="desc">Malejąco</option></select></Field>
      </Card>
      <Card className="!p-2">
        <Status loading={rq.loading} error={rq.error} retry={rq.reload} />
        {!rq.loading && rq.error === null && page.items.length === 0 ? <p className="p-8 text-center text-ink2">Brak kont dla wybranych filtrów.</p> : (
          <div className="grid gap-3 p-2 md:grid-cols-2">
            {page.items.map((a) => (
              <button key={a.id} onClick={() => navigate({ name: 'account', id: a.id })} className="rounded-2xl border border-line bg-white p-4 text-left transition hover:border-personal/40">
                <div className="mb-3 flex items-center gap-3">
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-shared-soft text-shared-deep"><Landmark size={19} /></span>
                  <div className="min-w-0 flex-1"><p className="truncate font-semibold">{a.name}</p><p className="text-xs text-ink2">{a.type ?? '—'}</p></div>
                  <div className="flex flex-col items-end gap-1"><Badge>{a.currencyCode}</Badge>{a.isClosed && <Badge tone="warn">Zamknięte</Badge>}</div>
                </div>
                <p className="num truncate text-sm text-ink2">{a.iban ?? a.number ?? '—'}</p>
              </button>
            ))}
          </div>
        )}
        <Pager page={page} onPage={(n) => setQ({ ...q, pageNumber: n })} />
      </Card>
    </div>
  )
}

/* ---------------- Transactions ---------------- */
export function TransactionsPanel({ mode = 'full', accountId }: { mode?: 'full' | 'account' | 'recent'; accountId?: string }) {
  const { navigate } = useApp()
  const [q, setQ] = useState<TxQuery>({ ...DEFAULT_TX_QUERY, bankAccountId: accountId ?? '', pageSize: mode === 'recent' ? 5 : 20 })
  const set = (p: Partial<TxQuery>) => setQ((x) => ({ ...x, pageNumber: 1, ...p }))
  const valid = isValidTxQuery(q)
  const accounts = useAccounts(); const accountName = useAccountName(); const CURRENCIES = useCurrencies()
  const rq = useRemote(() => banking.transactions(q), [JSON.stringify(q)], valid)
  const data = (rq.data ?? emptyPage(q.pageNumber, q.pageSize)) as Page<Tx>
  const page = valid ? data : null
  return (
    <div className="space-y-4">
      {mode === 'full' && (
        <Card className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Field label="Konto"><select value={q.bankAccountId} onChange={(e) => set({ bankAccountId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select></Field>
          <Field label="Data od"><input type="date" value={q.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
          <Field label="Data do"><input type="date" value={q.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
          <Field label="Waluta"><select value={q.currency} onChange={(e) => set({ currency: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{CURRENCIES.map((c) => <option key={c}>{c}</option>)}</select></Field>
          <Field label="Kwota min."><input inputMode="decimal" value={q.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
          <Field label="Kwota maks."><input inputMode="decimal" value={q.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
          <Field label="Opis"><input value={q.description} onChange={(e) => set({ description: e.target.value })} className={inputCls} /></Field>
          <Field label="Status"><input value={q.status} onChange={(e) => set({ status: e.target.value })} className={inputCls} placeholder="np. BOOKED" /></Field>
          <Field label="Sortuj wg"><select value={q.sortBy} onChange={(e) => set({ sortBy: e.target.value as TxQuery['sortBy'] })} className={inputCls}><option value="date">Data</option><option value="amount">Kwota</option><option value="status">Status</option><option value="currency">Waluta</option></select></Field>
          <Field label="Kolejność"><select value={q.descending ? 'desc' : 'asc'} onChange={(e) => set({ descending: e.target.value === 'desc' })} className={inputCls}><option value="desc">Malejąco</option><option value="asc">Rosnąco</option></select></Field>
        </Card>
      )}
      {!page ? <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">Filtry są nieprawidłowe: sprawdź zakres dat i kwot.</p> : (
        <Card className="!p-2">
          <Status loading={rq.loading} error={rq.error} retry={rq.reload} />
          {!rq.loading && rq.error === null && page.items.length === 0 ? <p className="p-8 text-center text-ink2">Brak transakcji.</p> : (
            <Table head={['Data', 'Opis', 'Konto', 'Kwota', 'Status', '']}>
              {page.items.map((t) => (
                <tr key={t.id} onClick={() => navigate({ name: 'transaction', id: t.id })} className="cursor-pointer hover:bg-wash">
                  <Td className="num whitespace-nowrap text-ink2">{dayText(t.date)}</Td>
                  <Td className="font-medium">{t.description ?? '—'}</Td>
                  <Td className="text-ink2">{accountName(t.bankAccountId)}</Td>
                  <Td className="text-right"><TxAmount t={t} /></Td>
                  <Td><Badge>{t.status}</Badge></Td>
                  <Td><ChevronRight size={16} className="text-ink2/60" /></Td>
                </tr>
              ))}
            </Table>
          )}
          {mode !== 'recent' && <Pager page={page} onPage={(n) => setQ({ ...q, pageNumber: n })} />}
        </Card>
      )}
    </div>
  )
}

/* ---------------- Connections / consents / refresh ---------------- */
function ConnectionsTab() {
  const { connections, attempt, navigate, bankingLoaded } = useApp()
  return (
    <div className="space-y-6">
      {attempt && (
        <Card className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            {attempt.status === 'PENDING' && <Loader2 size={18} className="animate-spin text-personal" />}
            <div><p className="text-sm font-semibold">Bieżąca próba połączenia</p><Badge tone={attemptTone(attempt.status)}>{label(ATTEMPT_STATUS_PL, attempt.status)}</Badge></div>
          </div>
          <Btn variant="ghost" onClick={() => navigate({ name: 'attempt', id: attempt.id })}>Zobacz status</Btn>
        </Card>
      )}
      <Card className="!p-2">
        {!bankingLoaded ? <p className="p-8 text-center text-ink2">Ładowanie…</p> : connections.length === 0 ? <p className="p-8 text-center text-ink2">Brak połączeń z bankami.</p> : (
          <Table head={['Dostawca', 'Połączenie', 'Synchronizacja', 'Ostatnia udana synchronizacja', '']}>
            {connections.map((c) => (
              <tr key={c.id} onClick={() => navigate({ name: 'connection', id: c.id })} className="cursor-pointer hover:bg-wash">
                <Td className="font-semibold">{c.providerName ?? '—'}</Td>
                <Td><Badge tone={connTone(c.connectionStatus)}>{label(CONNECTION_STATUS_PL, c.connectionStatus)}</Badge></Td>
                <Td><Badge tone={syncTone(c.syncStatus)}>{label(SYNC_STATUS_PL, c.syncStatus)}</Badge></Td>
                <Td className="text-ink2">{dateText(c.lastSuccessfulSyncAt)}</Td>
                <Td><ChevronRight size={16} className="text-ink2/60" /></Td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </div>
  )
}

function ConsentsTab() {
  const { consents, connections } = useApp()
  // Read-only: the backend has no endpoints to revoke or renew a consent.
  return (
    <Card className="!p-2">
      {consents.length === 0 ? <p className="p-8 text-center text-ink2">Brak zgód.</p> : (
        <Table head={['Połączenie', 'Status u dostawcy', 'Sesja ważna do', 'Można przedłużyć']}>
          {consents.map((c) => (
            <tr key={c.connectionId}>
              <Td className="font-semibold">{connections.find((x) => x.id === c.connectionId)?.providerName ?? c.connectionId}</Td>
              <Td><Badge>{c.providerStatus}</Badge></Td>
              <Td className="text-ink2">{dateText(c.sessionExpiresAt)}</Td>
              <Td>{c.sessionExtendable === null ? '—' : c.sessionExtendable ? 'Tak' : 'Nie'}</Td>
            </tr>
          ))}
        </Table>
      )}
    </Card>
  )
}

export function RefreshDetails() {
  const { lastRefresh: r } = useApp()
  const accountName = useAccountName()
  if (!r) return <p className="text-sm text-ink2">Nie zlecono jeszcze odświeżenia w tej przeglądarce.</p>
  return (
    <div className="space-y-4">
      <KeyValue rows={[
        ['Status', <Badge key="s">{r.status}</Badge>], ['Utworzono', dateText(r.createdAt)], ['Rozpoczęto', dateText(r.startedAt)], ['Zakończono', dateText(r.completedAt)],
        ['Konta (łącznie)', r.totalAccounts], ['Zakończone powodzeniem', r.successfulAccounts], ['Zakończone błędem', r.failedAccounts], ['Kod błędu', r.failureCode ?? '—'],
      ]} />
      {r.accounts.length > 0 && (
        <Table head={['Konto', 'Status', 'Dodane transakcje', 'Zaktualizowane transakcje', 'Ostatnia udana synchronizacja', 'Kod błędu']}>
          {r.accounts.map((a) => (
            <tr key={a.accountId}>
              <Td className="font-medium">{accountName(a.accountId)}</Td><Td><Badge>{a.status}</Badge></Td><Td className="num">{a.addedTransactions}</Td><Td className="num">{a.updatedTransactions}</Td>
              <Td className="text-ink2">{dateText(a.lastSuccessfulSyncAt)}</Td><Td>{a.failureCode ?? '—'}</Td>
            </tr>
          ))}
        </Table>
      )}
    </div>
  )
}

function RefreshTab() {
  const { lastRefresh: r, startRefresh } = useApp()
  const running = !!r && (r.status === 'QUEUED' || r.status === 'RUNNING')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<number | null>(null)
  const start = async () => { setBusy(true); setErr(null); const x = await startRefresh(); setBusy(false); if (!x.ok) setErr(x.code) }
  return (
    <div className="space-y-4">
      <Card className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-md text-sm text-ink2">Pobierz najnowsze konta i transakcje z połączonych banków.</p>
        <Btn onClick={start} disabled={running || busy}>{running ? <Loader2 size={16} className="animate-spin" /> : <RefreshCw size={16} />}Odśwież dane</Btn>
      </Card>
      {err !== null && describeError(err) && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{describeError(err)}</p>}
      <Card><H>Ostatnie odświeżenie</H><RefreshDetails /></Card>
    </div>
  )
}

const TABS = [
  { v: 'accounts', label: 'Konta' }, { v: 'transactions', label: 'Transakcje' }, { v: 'connections', label: 'Połączenia' },
  { v: 'consents', label: 'Zgody' }, { v: 'refresh', label: 'Odświeżanie' },
]

export function BankingPage({ tab = 'accounts' }: { tab?: string }) {
  const { navigate, startAttempt, bankingError, reloadBanking } = useApp()
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<number | null>(null)
  const connect = async () => {
    setBusy(true); setErr(null)
    const r = await startAttempt()
    setBusy(false)
    if (r.ok) navigate({ name: 'attempt', id: r.data }); else setErr(r.code)
  }
  const bankMsg = bankingError !== null ? describeError(bankingError) : null
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <PageTitle title="Konta bankowe" sub="Dane z banków są tylko do odczytu." aside={<Btn onClick={connect} disabled={busy}>{busy ? <Loader2 size={16} className="animate-spin" /> : <Plus size={16} />}Połącz bank</Btn>} />
      {err !== null && describeError(err) && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{describeError(err)}</p>}
      {bankMsg && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{bankMsg} <button className="font-semibold underline" onClick={() => void reloadBanking()}>Spróbuj ponownie</button></p>}
      <Segmented value={tab} onChange={(v) => navigate({ name: 'banking', tab: v })} options={TABS} />
      {tab === 'accounts' && <AccountsTab />}
      {tab === 'transactions' && <TransactionsPanel />}
      {tab === 'connections' && <ConnectionsTab />}
      {tab === 'consents' && <ConsentsTab />}
      {tab === 'refresh' && <RefreshTab />}
    </div>
  )
}

/* ---------------- Attempt, details ---------------- */
export function AttemptPage({ id }: { id: string }) {
  const { attempt, navigate, pollAttempt } = useApp()
  const q = useRemote(() => banking.attempt(id), [id, attempt?.status])
  const a = (attempt && attempt.id === id ? attempt : q.data) as import('../mocks/bank').Attempt | null
  const [checking, setChecking] = useState(false)
  const check = async () => { setChecking(true); await pollAttempt(id); setChecking(false); q.reload() }
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div><Back to={{ name: 'banking', tab: 'connections' }} text="Połączenia" /><PageTitle title="Łączenie z bankiem" /></div>
      {!a ? (q.loading ? <Card className="text-ink2">Ładowanie…</Card> : <Card className="text-ink2">{(q.error !== null && describeError(q.error)) || 'Nie znaleziono tej próby połączenia.'}</Card>) : (
        <Card className="space-y-4">
          <div className="flex items-center gap-3">
            {a.status === 'PENDING' && <Loader2 className="animate-spin text-personal" />}
            <div><p className="font-semibold">Status próby połączenia</p><Badge tone={attemptTone(a.status)}>{label(ATTEMPT_STATUS_PL, a.status)}</Badge></div>
          </div>
          <p className="text-sm text-ink2">
            {a.status === 'PENDING' ? 'Trwa łączenie. Bank i zgodę wybierasz w zewnętrznym kroku dostawcy (Tink). Status sprawdzamy automatycznie. Po autoryzacji u dostawcy wróć do tej karty.'
              : a.status === 'COMPLETED' ? 'Połączenie zostało utworzone. Możesz teraz odświeżyć dane bankowe.'
              : a.status === 'CANCELLED' ? 'Łączenie zostało anulowane. Możesz spróbować ponownie.'
              : a.status === 'EXPIRED' ? 'Czas na dokończenie łączenia minął. Rozpocznij je ponownie.'
              : 'Łączenie nie powiodło się. Spróbuj ponownie.'}
          </p>
          <KeyValue rows={[['Utworzono', dateText(a.createdAt)], ['Ważna do', dateText(a.expiresAt)], ['Zakończono', dateText(a.completedAt)], ['Kod błędu', a.failureCode ?? '—'], ['Połączenie', a.connectionId ?? '—']]} />
          <div className="flex flex-wrap gap-2">
            <Btn variant="ghost" onClick={check} disabled={checking}>Sprawdź status</Btn>
            {a.connectionId && <Btn variant="ghost" onClick={() => navigate({ name: 'connection', id: a.connectionId! })}>Zobacz połączenie</Btn>}
          </div>
        </Card>
      )}
    </div>
  )
}

export function AccountPage({ id }: { id: string }) {
  const q = useRemote(() => banking.account(id), [id])
  const a = q.data as import('../mocks/bank').Account | null
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div><Back to={{ name: 'banking', tab: 'accounts' }} text="Konta" /><PageTitle title={a ? a.name : 'Szczegóły konta'} /></div>
      {!a ? <Card className="text-ink2">{q.loading ? 'Ładowanie…' : (q.error !== null && describeError(q.error)) || 'Nie znaleziono konta.'}</Card> : (
        <>
          <Card><KeyValue rows={[
            ['Nazwa', a.name], ['Numer', a.number ?? '—'], ['IBAN', a.iban ?? '—'], ['Identyfikator instytucji', a.financialInstitutionId ?? '—'], ['Typ', a.type ?? '—'], ['Waluta', a.currencyCode],
            ['Konto zamknięte', a.isClosed ? 'Tak' : 'Nie'], ['Ostatnia udana synchronizacja', dateText(a.lastSuccessfulSyncAt)], ['Transakcje zsynchronizowane do', dayText(a.transactionsSyncedThrough)],
          ]} /></Card>
          <div><H>Transakcje na koncie</H><TransactionsPanel mode="account" accountId={a.id} /></div>
        </>
      )}
    </div>
  )
}

export function TransactionPage({ id }: { id: string }) {
  const accountName = useAccountName()
  const q = useRemote(() => banking.transaction(id), [id])
  const t = q.data as Tx | null
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div><Back to={{ name: 'banking', tab: 'transactions' }} text="Transakcje" /><PageTitle title="Szczegóły transakcji" /></div>
      {!t ? <Card className="text-ink2">{q.loading ? 'Ładowanie…' : (q.error !== null && describeError(q.error)) || 'Nie znaleziono transakcji.'}</Card> : (
        <Card>
          <p className="mb-4 text-3xl"><TxAmount t={t} /></p>
          <KeyValue rows={[['Opis', t.description ?? '—'], ['Data', dayText(t.date)], ['Waluta', t.currencyCode], ['Status', <Badge key="s">{t.status}</Badge>], ['Konto', accountName(t.bankAccountId)]]} />
        </Card>
      )}
    </div>
  )
}

export function ConnectionPage({ id }: { id: string }) {
  const q = useRemote(() => banking.connection(id), [id])
  const c = q.data as import('../mocks/bank').Connection | null
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div><Back to={{ name: 'banking', tab: 'connections' }} text="Połączenia" /><PageTitle title={c ? c.providerName ?? 'Połączenie' : 'Szczegóły połączenia'} /></div>
      {!c ? <Card className="text-ink2">{q.loading ? 'Ładowanie…' : (q.error !== null && describeError(q.error)) || 'Nie znaleziono połączenia.'}</Card> : (
        <Card><KeyValue rows={[
          ['Dostawca', c.providerName ?? '—'], ['Status połączenia', <Badge key="a" tone={connTone(c.connectionStatus)}>{label(CONNECTION_STATUS_PL, c.connectionStatus)}</Badge>],
          ['Status synchronizacji', <Badge key="b" tone={syncTone(c.syncStatus)}>{label(SYNC_STATUS_PL, c.syncStatus)}</Badge>], ['Utworzono', dateText(c.createdAt)], ['Połączono', dateText(c.connectedAt)],
          ['Zgoda ważna do', dateText(c.consentExpiresAt)], ['Ostatnia udana synchronizacja', dateText(c.lastSuccessfulSyncAt)], ['Kod ostatniego błędu synchronizacji', c.lastSyncFailureCode ?? '—'],
        ]} /></Card>
      )}
    </div>
  )
}
