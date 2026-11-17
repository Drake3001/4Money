import { createContext, useContext, useEffect, useRef, useState } from 'react'
import { ArrowDown, ArrowUp, ArrowLeft, Landmark, Loader2, Plus, RefreshCw, RotateCcw } from 'lucide-react'
import { useApp } from '../context'
import { banking } from '../api/banking'
import { describeError } from '../api/errors'
import { useRemote } from '../api/hooks'
import {
  ATTEMPT_STATUS_PL, DEFAULT_TX_QUERY, SYNC_STATUS_PL, TX_STATUS_PL, attemptErrorText, refreshErrorText, providerStatusView, transactionStatus, dateText, dayText, isValidTxQuery, label,
  type Page, type Tx, type TxQuery,
} from '../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, PageTitle, Pager, Segmented, Table, Td, cx, inputCls } from '../ui'
import { BankTransactionDetails, BankTransactionModal, TxAmount, txTone } from './BankTransactionDetails'

const Back = ({ to, text }: { to: Parameters<ReturnType<typeof useApp>['navigate']>[0]; text: string }) => {
  const { navigate } = useApp()
  return <button onClick={() => navigate(to)} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />{text}</button>
}
const useAccounts = () => useApp().accounts
const useAccountName = () => { const a = useAccounts(); return (id: string) => a.find((x) => x.id === id)?.name ?? id }
const emptyPage = <T,>(n: number, size: number): Page<T> => ({ items: [], pageNumber: n, pageSize: size, totalCount: 0, totalPages: 1, hasPreviousPage: false, hasNextPage: false })
const BankingErrorMessages = createContext<readonly string[]>([])
// Request failure or loading state shown above a list (501 shows the shared dialog instead).
function Status({ loading, error, retry }: { loading: boolean; error: number | null; retry: () => void }) {
  const sharedMessages = useContext(BankingErrorMessages)
  const msg = error !== null ? describeError(error) : null
  if (msg && sharedMessages.includes(msg)) return null
  if (msg) return <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{msg} <button className="font-semibold underline" onClick={retry}>Spróbuj ponownie</button></p>
  return loading ? <p className="p-4 text-center text-sm text-ink2">Ładowanie…</p> : null
}

const syncTone = (s: string) => (s === 'SUCCEEDED' ? 'ok' : s === 'PARTIALLY_SUCCEEDED' ? 'warn' : s === 'FAILED' ? 'danger' : s === 'RUNNING' || s === 'QUEUED' ? 'info' : 'neutral') as 'ok' | 'warn' | 'danger' | 'info' | 'neutral'
const refreshStatus = (s: string) => label({ ...SYNC_STATUS_PL, SUCCEEDED: 'Powodzenie', PARTIALLY_SUCCEEDED: 'Częściowe powodzenie' }, s)
const attemptTone = (s: string) => (s === 'COMPLETED' ? 'ok' : s === 'FAILED' ? 'danger' : s === 'PENDING' ? 'info' : 'neutral') as 'ok' | 'danger' | 'info' | 'neutral'

/* ---------------- Accounts ---------------- */
function AccountsTab() {
  const { navigate, lastRefresh } = useApp()
  const [name, setName] = useState('')
  const [filter, setFilter] = useState<'open' | 'closed' | 'all'>('open')
  const [descending, setDescending] = useState(false)
  const rq = useRemote(() => banking.allAccounts(), [lastRefresh?.completedAt])
  const search = name.trim().toLocaleLowerCase('pl')
  const accounts = (rq.data ?? [])
    .filter((a) => (filter === 'all' || a.isClosed === (filter === 'closed')) && a.name.toLocaleLowerCase('pl').includes(search))
    .sort((a, b) => (descending ? -1 : 1) * a.name.localeCompare(b.name, 'pl', { sensitivity: 'base', numeric: true }))
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <label className="flex max-w-full items-center gap-2 text-sm text-ink2">
          <span className="text-xs font-semibold uppercase tracking-wide">FILTROWANIE</span>
          <select value={filter} onChange={(e) => setFilter(e.target.value as typeof filter)} className={cx(inputCls, '!w-auto')}><option value="open">Otwarte</option><option value="closed">Zamknięte</option><option value="all">Wszystkie</option></select>
        </label>
        <div className="flex max-w-full items-center gap-2">
          <span className="text-xs font-semibold uppercase tracking-wide text-ink2">Sortowanie: nazwa</span>
          <button type="button" onClick={() => setDescending((value) => !value)}
            title={descending ? 'Malejąco' : 'Rosnąco'} aria-label={descending ? 'Sortowanie malejąco. Zmień na rosnąco' : 'Sortowanie rosnąco. Zmień na malejąco'}
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-ink transition hover:bg-wash">
            {descending ? <ArrowDown size={18} /> : <ArrowUp size={18} />}
          </button>
        </div>
      </div>
      <Field label="Nazwa konta"><input type="search" placeholder="Szukaj konta po nazwie…" value={name} onChange={(e) => setName(e.target.value)} className={inputCls} /></Field>
      <Status loading={rq.loading} error={rq.error} retry={rq.reload} />
      {!rq.loading && rq.error === null && (accounts.length === 0 ? (
        <Card className="py-12 text-center text-ink2">Brak kont dla wybranych filtrów.</Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {accounts.map((a) => (
            <button key={a.id} onClick={() => navigate({ name: 'account', id: a.id })} className="rounded-2xl border border-line bg-white p-4 text-left transition hover:border-ink/40">
              <div className="mb-3 flex items-center gap-3">
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-shared-soft text-shared-deep"><Landmark size={19} /></span>
                <div className="min-w-0 flex-1"><h3 className="truncate font-display text-lg font-bold">{a.name}</h3></div>
                <div className="flex flex-col items-end gap-1"><Badge>{a.currencyCode}</Badge>{a.isClosed && <Badge tone="warn">Zamknięte</Badge>}</div>
              </div>
              <p className="num truncate text-sm text-ink2">{a.number ?? a.iban ?? '-'}</p>
            </button>
          ))}
        </div>
      ))}
    </div>
  )
}

/* ---------------- Transactions ---------------- */
export function TransactionsPanel({ mode = 'full', accountId }: { mode?: 'full' | 'account' | 'recent'; accountId?: string }) {
  const { lastRefresh } = useApp()
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const list = useRef<HTMLDivElement>(null)
  const [q, setQ] = useState<TxQuery>({ ...DEFAULT_TX_QUERY, bankAccountId: accountId ?? '', pageSize: mode === 'recent' ? 5 : 20 })
  const set = (p: Partial<TxQuery>) => setQ((x) => ({ ...x, pageNumber: 1, ...p }))
  const valid = isValidTxQuery(q)
  const accounts = useAccounts(); const accountName = useAccountName()
  const queryKey = JSON.stringify(q)
  useEffect(() => { list.current?.scrollTo({ top: 0 }) }, [queryKey])
  const rq = useRemote(() => banking.transactions(q), [queryKey, lastRefresh?.completedAt], valid)
  const data = (rq.data ?? emptyPage(q.pageNumber, q.pageSize)) as Page<Tx>
  const page = valid ? data : null
  return (
    <div className="space-y-4">
      {mode !== 'recent' && (
        <Card className="shrink-0 space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <Field label="SORTOWANIE"><select value={q.sortBy} onChange={(e) => set({ sortBy: e.target.value as TxQuery['sortBy'] })} className={cx(inputCls, 'h-11 !w-28')}><option value="date">Data</option><option value="amount">Kwota</option></select></Field>
            <button type="button" onClick={() => set({ descending: !q.descending })}
              title={q.descending ? 'Malejąco' : 'Rosnąco'} aria-label={q.descending ? 'Sortowanie malejąco. Zmień na rosnąco' : 'Sortowanie rosnąco. Zmień na malejąco'}
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-ink transition hover:bg-wash">
              {q.descending ? <ArrowDown size={18} /> : <ArrowUp size={18} />}
            </button>
            <div><span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">FILTROWANIE</span>
              <button type="button" aria-expanded={filtersOpen} onClick={() => setFiltersOpen((v) => !v)} className="h-11 rounded-xl border border-line bg-white px-4 text-sm font-semibold transition hover:bg-wash">{filtersOpen ? 'Mniej filtrów' : 'Więcej filtrów'}</button>
            </div>
            <Btn variant="soft" className="h-11" onClick={() => setQ({ ...DEFAULT_TX_QUERY, bankAccountId: accountId ?? '' })}><RotateCcw size={16} />Wyczyść filtry</Btn>
          </div>
          {filtersOpen && <div className="grid gap-3 border-t border-line pt-4 sm:grid-cols-2 lg:grid-cols-3">
            {mode === 'full' && <Field label="Konto"><select value={q.bankAccountId} onChange={(e) => set({ bankAccountId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select></Field>}
            <Field label="Data od"><input type="date" value={q.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
            <Field label="Data do"><input type="date" value={q.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
            <Field label="Kwota min."><input inputMode="decimal" value={q.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
            <Field label="Kwota maks."><input inputMode="decimal" value={q.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
            <Field label="Status"><select value={q.status} onChange={(e) => set({ status: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{Object.entries(TX_STATUS_PL).map(([value, text]) => <option key={value} value={value[0] + value.slice(1).toLowerCase()}>{text}</option>)}</select></Field>
          </div>}
        </Card>
      )}
      {!page ? <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">Dane filtrowania są nieprawidłowe. Sprawdź je i spróbuj ponownie.</p> : (
        <Card className="overflow-hidden !p-2">
          <div ref={list} role="region" aria-label="Lista transakcji" tabIndex={0} className={cx('soft-scroll', mode !== 'recent' && 'h-[232px] overflow-y-auto overscroll-contain [&_tbody_tr]:h-16 [&_thead_tr]:h-10 [&_td]:py-0 [&_th]:py-0')}>
          <Status loading={rq.loading} error={rq.error} retry={rq.reload} />
          {!rq.loading && rq.error === null && page.items.length === 0 ? <p className="p-8 text-center text-ink2">Brak transakcji.</p> : !rq.loading && rq.error === null && (
            <Table head={['Data', 'Opis', 'Konto', 'Kwota', 'Status']}>
              {page.items.map((t) => (
                <tr key={t.id} onClick={() => setSelectedId(t.id)} className="cursor-pointer hover:bg-wash">
                  <Td className="num whitespace-nowrap text-ink2">{dayText(t.date)}</Td>
                  <Td className="font-medium"><button type="button" onClick={() => setSelectedId(t.id)} className="line-clamp-2 text-left">{t.description ?? 'Szczegóły transakcji'}</button></Td>
                  <Td className="text-ink2"><span className="line-clamp-2">{accountName(t.bankAccountId)}</span></Td>
                  <Td className="text-right"><TxAmount t={t} /></Td>
                  <Td><Badge tone={txTone(t.status)}>{transactionStatus(t.status)}</Badge></Td>
                </tr>
              ))}
            </Table>
          )}
          </div>
          {mode !== 'recent' && <div className="shrink-0"><Pager page={page} onPage={(n) => setQ({ ...q, pageNumber: n })} itemSummary={`Transakcje na stronie: ${page.items.length}`} /></div>}
        </Card>
      )}
      {selectedId && <BankTransactionModal key={selectedId} id={selectedId} onClose={() => setSelectedId(null)} />}
    </div>
  )
}

/* ---------------- Connections / consents / refresh ---------------- */
function ConnectionsTab() {
  const { connections, consents, attempt, navigate, bankingLoaded } = useApp()
  return (
    <div className="space-y-6">
      {attempt && (
        <Card className="space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              {attempt.status === 'PENDING' && <Loader2 size={18} className="animate-spin text-ink" />}
              <div><p className="text-sm font-semibold">Ostatnia próba połączenia</p><Badge tone={attemptTone(attempt.status)}>{label(ATTEMPT_STATUS_PL, attempt.status)}</Badge></div>
            </div>
            <Btn variant="ghost" onClick={() => navigate({ name: 'attempt', id: attempt.id })}>Zobacz status</Btn>
          </div>
        </Card>
      )}
      <Card className="!p-2">
        {!bankingLoaded ? <p className="p-8 text-center text-ink2">Ładowanie…</p> : consents.length === 0 ? <p className="p-8 text-center text-ink2">{connections.length ? 'Dostawca nie zwrócił informacji o zgodach dla połączeń.' : 'Brak połączeń z bankami.'}</p> : (
          <Table head={['Połączenie', 'Status u dostawcy', 'Data ważności sesji', 'Czy można przedłużyć']}>
            {consents.map((c) => {
              const status = providerStatusView(c.providerStatus)
              return (
                <tr key={c.connectionId}>
                  <Td className="num break-all font-medium">{c.connectionId}</Td>
                  <Td><span title={c.providerStatus}><Badge tone={status.tone}>{status.text}</Badge></span></Td>
                  <Td className="text-ink2">{c.sessionExpiresAt ? dateText(c.sessionExpiresAt) : 'Brak informacji'}</Td>
                  <Td>{c.sessionExtendable === null ? 'Brak informacji' : c.sessionExtendable ? 'Tak' : 'Nie'}</Td>
                </tr>
              )
            })}
          </Table>
        )}
      </Card>
    </div>
  )
}

export function RefreshDetails() {
  const { lastRefresh: r } = useApp()
  const accountName = useAccountName()
  if (!r) return <p className="text-sm text-ink2">Nie zlecono jeszcze odświeżenia.</p>
  return (
    <div className="space-y-4">
      <KeyValue rows={[
        ['Status', <Badge key="s" tone={syncTone(r.status)}>{refreshStatus(r.status)}</Badge>], ['Data odświeżenia', dateText(r.completedAt ?? r.startedAt ?? r.createdAt)],
        ['Konta', r.totalAccounts], ['Zakończone powodzeniem', r.successfulAccounts], ['Zakończone błędem', r.failedAccounts], ['Błąd', refreshErrorText(r.failureCode)],
      ]} />
      {r.accounts.length > 0 && (
        <Table head={['Konto', 'Status', 'Dodane transakcje', 'Zaktualizowane transakcje', 'Ostatnia udana synchronizacja', 'Błąd']}>
          {r.accounts.map((a) => (
            <tr key={a.accountId}>
              <Td className="font-medium">{accountName(a.accountId)}</Td><Td><Badge tone={syncTone(a.status)}>{refreshStatus(a.status)}</Badge></Td><Td className="num">{a.addedTransactions}</Td><Td className="num">{a.updatedTransactions}</Td>
              <Td className="text-ink2">{dateText(a.lastSuccessfulSyncAt)}</Td><Td>{refreshErrorText(a.failureCode)}</Td>
            </tr>
          ))}
        </Table>
      )}
    </div>
  )
}

function RefreshTab() {
  const sharedMessages = useContext(BankingErrorMessages)
  const { lastRefresh: r, startRefresh } = useApp()
  const running = !!r && (r.status === 'QUEUED' || r.status === 'RUNNING')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<number | null>(null)
  const errorMessage = err !== null ? describeError(err) : null
  const start = async () => { setBusy(true); setErr(null); const x = await startRefresh(); setBusy(false); if (!x.ok) setErr(x.code) }
  return (
    <div className="space-y-4">
      <Card className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-md text-sm text-ink2">Pobierz najnowsze konta i transakcje z połączonych banków.</p>
        <Btn onClick={start} disabled={running || busy}>{running ? <Loader2 size={16} className="animate-spin" /> : <RefreshCw size={16} />}Odśwież dane</Btn>
      </Card>
      {errorMessage && !sharedMessages.includes(errorMessage) && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{errorMessage}</p>}
      <Card><H>Ostatnie odświeżenie</H><RefreshDetails /></Card>
    </div>
  )
}

const TABS = [
  { v: 'accounts', label: 'Konta' }, { v: 'transactions', label: 'Transakcje' }, { v: 'connections', label: 'Połączenia' },
  { v: 'refresh', label: 'Odświeżanie' },
]

export function BankingPage({ tab = 'accounts' }: { tab?: string }) {
  const activeTab = tab === 'consents' ? 'connections' : tab
  const { navigate, startAttempt, bankingError } = useApp()
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<number | null>(null)
  const connect = async () => {
    setBusy(true); setErr(null)
    const r = await startAttempt()
    setBusy(false)
    if (r.ok) navigate({ name: 'attempt', id: r.data }); else setErr(r.code)
  }
  const bankMsg = bankingError !== null ? describeError(bankingError) : null
  const connectMsg = err !== null ? describeError(err) : null
  const errorMessages = [...new Set([bankMsg, connectMsg].filter((message): message is string => !!message))]
  return (
    <BankingErrorMessages.Provider value={errorMessages}>
    <div className="mx-auto max-w-4xl space-y-6">
      <PageTitle title="Konta bankowe" sub="Dane z kont bankowych są tylko do odczytu. 4Money nie zarządza Twoim kontem bankowym." aside={<Btn onClick={connect} disabled={busy}>{busy ? <Loader2 size={16} className="animate-spin" /> : <Plus size={16} />}Połącz bank</Btn>} />
      {errorMessages.map((message) => <p key={message} role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{message}</p>)}
      <Segmented value={activeTab} onChange={(v) => navigate({ name: 'banking', tab: v })} options={TABS} />
      {tab === 'accounts' && <AccountsTab />}
      {tab === 'transactions' && <TransactionsPanel />}
      {activeTab === 'connections' && <ConnectionsTab />}
      {tab === 'refresh' && <RefreshTab />}
    </div>
    </BankingErrorMessages.Provider>
  )
}

/* ---------------- Attempt, details ---------------- */
export function AttemptPage({ id }: { id: string }) {
  const { attempt, navigate, pollAttempt, showToast } = useApp()
  const q = useRemote(() => banking.attempt(id), [id, attempt?.status])
  const a = (attempt && attempt.id === id ? attempt : q.data) as import('../mocks/bank').Attempt | null
  const [checking, setChecking] = useState(false)
  const check = async () => {
    if (checking) return
    const previousStatus = a?.status
    setChecking(true)
    try {
      const result = await pollAttempt(id)
      if (!result) {
        showToast('Nie udało się sprawdzić statusu. Dane nie zostały zaktualizowane.')
      } else {
        const statusMessage = result.status === previousStatus ? 'Status bez zmian.' : `Status zmienił się na: ${label(ATTEMPT_STATUS_PL, result.status)}.`
        showToast(`${statusMessage} Pobrano aktualne dane próby połączenia.`)
      }
    }
    catch { showToast('Nie udało się sprawdzić statusu. Dane nie zostały zaktualizowane.') }
    finally { setChecking(false) }
  }
  const description: Record<string, string> = {
    PENDING: 'Trwa łączenie. Bank i zgodę wybierasz w zewnętrznym kroku dostawcy (Tink). Status sprawdzamy automatycznie. Po autoryzacji u dostawcy wróć do tej karty.',
    COMPLETED: 'Połączenie zostało utworzone. Możesz teraz odświeżyć dane bankowe.',
    CANCELLED: 'Łączenie zostało anulowane. Możesz spróbować ponownie.',
    EXPIRED: 'Czas na dokończenie łączenia minął. Rozpocznij je ponownie.',
  }
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div><Back to={{ name: 'banking', tab: 'connections' }} text="Połączenia" /><PageTitle title="Połączenie z bankiem" /></div>
      {!a ? (q.loading ? <Card className="text-ink2">Ładowanie…</Card> : <Card className="text-ink2">{(q.error !== null && describeError(q.error)) || 'Nie znaleziono tej próby połączenia.'}</Card>) : (
        <Card className="space-y-4">
          <div className="flex items-center gap-3">
            {a.status === 'PENDING' && <Loader2 className="animate-spin text-ink" />}
            <div><p className="font-semibold">Status próby połączenia</p><Badge tone={attemptTone(a.status)}>{label(ATTEMPT_STATUS_PL, a.status)}</Badge></div>
          </div>
          {description[a.status] && <p className="text-sm text-ink2">{description[a.status]}</p>}
          <KeyValue rows={[['Utworzono', dateText(a.createdAt)], ['Ważna do', dateText(a.expiresAt)], ['Zakończono', dateText(a.completedAt)], ['Błąd', attemptErrorText(a.failureCode)]]} />
          <div className="flex flex-wrap gap-2">
            <Btn variant="ghost" onClick={check} disabled={checking}>{checking && <Loader2 size={16} className="animate-spin" />}{checking ? 'Sprawdzanie…' : 'Sprawdź status'}</Btn>
            {a.connectionId && <Btn variant="ghost" onClick={() => navigate({ name: 'banking', tab: 'connections' })}>Połączenia</Btn>}
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
      <div><Back to={{ name: 'banking', tab: 'accounts' }} text="Konta bankowe" /><PageTitle title={a ? a.name : 'Szczegóły konta'} /></div>
      {!a ? <Card className="text-ink2">{q.loading ? 'Ładowanie…' : (q.error !== null && describeError(q.error)) || 'Nie znaleziono konta.'}</Card> : (
        <>
          <Card><KeyValue rows={[
            ['Nazwa', a.name], ['Numer konta', a.number ?? a.iban ?? '-'], ['Identyfikator instytucji', a.financialInstitutionId ?? '-'], ['Waluta', a.currencyCode],
            ['Status', <Badge key="status" tone={a.isClosed ? 'warn' : 'ok'}>{a.isClosed ? 'Zamknięte' : 'Otwarte'}</Badge>], ['Ostatnia udana synchronizacja', dateText(a.lastSuccessfulSyncAt)],
          ]} /></Card>
          <div><H>Transakcje na koncie</H><TransactionsPanel mode="account" accountId={a.id} /></div>
        </>
      )}
    </div>
  )
}

export function TransactionPage({ id }: { id: string }) {
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div><Back to={{ name: 'banking', tab: 'transactions' }} text="Transakcje" /><PageTitle title="Szczegóły transakcji" /></div>
      <Card><BankTransactionDetails id={id} /></Card>
    </div>
  )
}
