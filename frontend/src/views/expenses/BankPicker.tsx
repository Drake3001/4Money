import { useEffect, useRef, useState } from 'react'
import { ArrowDown, ArrowUp, Check, Loader2, RotateCcw } from 'lucide-react'
import { useRemote } from '../../api/hooks'
import { useApp } from '../../context'
import { useExpenses, type BankPageFilter } from '../../expensesContext'
import { dayText, type Tx } from '../../mocks/bank'
import { isNum, num } from '../../mocks/expenses'
import { Btn, Card, Field, Modal, Pager, Table, Td, cx, inputCls } from '../../ui'
import { TxAmount } from '../BankTransactionDetails'
import { ExpErrorBox } from './ExpenseUi'

const DEFAULT_FILTER: BankPageFilter = { accountId: '', dateFrom: '', dateTo: '', minAmount: '', maxAmount: '', description: '', pageNumber: 1, pageSize: 20, sortBy: 'date', descending: true }

export default function BankPicker({ onPick, onClose }: { onPick: (tx: Tx) => void; onClose: () => void }) {
  return <Modal title="Wybierz transakcję bankową" onClose={onClose} className="flex min-h-0 flex-col !max-w-5xl !overflow-hidden !p-4 sm:!p-6">
    <BankTransactionSelection onPick={onPick} onCancel={onClose} dialog />
  </Modal>
}

export function BankTransactionSelection({ onPick, onCancel, confirmLabel = 'Dołącz transakcję', showIntro = true, dialog = false }: {
  onPick: (tx: Tx) => void; onCancel: () => void; confirmLabel?: string; showIntro?: boolean; dialog?: boolean
}) {
  const ex = useExpenses()
  const { accounts } = useApp()
  const [f, setF] = useState(DEFAULT_FILTER)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const set = (patch: Partial<BankPageFilter>) => { setSelectedId(null); setF((current) => ({ ...current, pageNumber: 1, ...patch })) }
  const [applied, setApplied] = useState(f)
  useEffect(() => { const timer = setTimeout(() => setApplied(f), 350); return () => clearTimeout(timer) }, [f])
  useEffect(() => { listRef.current?.scrollTo({ top: 0 }) }, [applied])
  const valid = (!applied.minAmount.trim() || (isNum(applied.minAmount) && num(applied.minAmount) >= 0))
    && (!applied.maxAmount.trim() || (isNum(applied.maxAmount) && num(applied.maxAmount) >= 0))
    && !(applied.minAmount.trim() && applied.maxAmount.trim() && num(applied.minAmount) > num(applied.maxAmount))
    && !(applied.dateFrom && applied.dateTo && applied.dateFrom > applied.dateTo)
  const q = useRemote(() => ex.availableBankTransactionsPage(applied), [applied], valid)
  const res = q.data
  const page = res?.ok ? res.data : { items: [], pageNumber: applied.pageNumber, pageSize: 20, totalCount: 0, totalPages: 1, hasPreviousPage: false, hasNextPage: false }
  const pending = q.loading || f !== applied
  const selected = page.items.find((tx) => tx.id === selectedId)
  const error = q.error ?? (res && !res.ok ? res.code : null)
  const reset = () => { setSelectedId(null); setF({ ...DEFAULT_FILTER }) }
  return (
      <div className={dialog ? 'flex min-h-0 flex-col gap-4' : 'space-y-4'}>
        {showIntro && <p className="shrink-0 text-sm text-ink2">Pokazujemy Twoje wychodzące, zaksięgowane transakcje, które nie zostały jeszcze przypisane do innego wydatku.</p>}
        <Card className="shrink-0 space-y-4">
        <div className="flex shrink-0 flex-wrap items-end gap-2">
          <Field label="SORTOWANIE"><select value={f.sortBy} onChange={(e) => set({ sortBy: e.target.value as BankPageFilter['sortBy'] })} className={cx(inputCls, 'h-11 !w-28')}><option value="date">Data</option><option value="amount">Kwota</option></select></Field>
          <button type="button" onClick={() => set({ descending: !f.descending })} title={f.descending ? 'Malejąco' : 'Rosnąco'} aria-label={f.descending ? 'Sortowanie malejąco. Zmień na rosnąco' : 'Sortowanie rosnąco. Zmień na malejąco'} className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-white hover:bg-wash">
            {f.descending ? <ArrowDown size={18} /> : <ArrowUp size={18} />}
          </button>
          <div><span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">FILTROWANIE</span>
            <button type="button" aria-expanded={filtersOpen} onClick={() => setFiltersOpen((open) => !open)} className="h-11 rounded-xl border border-line px-3 text-sm font-semibold hover:bg-wash">{filtersOpen ? 'Mniej filtrów' : 'Więcej filtrów'}</button>
          </div>
          <Btn variant="soft" className="h-11" onClick={reset}><RotateCcw size={16} />Wyczyść filtry</Btn>
        </div>
        {filtersOpen && <div className="grid shrink-0 grid-cols-2 gap-2 border-t border-line pt-3 sm:grid-cols-3">
          <Field label="Konto"><select value={f.accountId} onChange={(e) => set({ accountId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{accounts.map((account) => <option key={account.id} value={account.id}>{account.name}</option>)}</select></Field>
          <Field label="Data od"><input type="date" value={f.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
          <Field label="Data do"><input type="date" value={f.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
          <Field label="Kwota min."><input inputMode="decimal" value={f.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
          <Field label="Kwota maks."><input inputMode="decimal" value={f.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
        </div>}
        </Card>
        {!valid && <p role="alert" className="shrink-0 rounded-xl bg-danger-soft p-3 text-sm text-danger">Dane filtrowania są nieprawidłowe. Sprawdź je i spróbuj ponownie.</p>}
        {valid && error !== null && (error === 0 ? <p role="alert" className="text-sm text-danger">Brak połączenia z serwerem. Spróbuj ponownie.</p> : <ExpErrorBox code={error} />)}
        <Card className="flex min-h-16 flex-col overflow-hidden !p-2">
          <div ref={listRef} role="region" aria-label="Dostępne transakcje bankowe" tabIndex={0} className="soft-scroll h-[232px] min-h-16 overflow-y-auto overscroll-contain [&_tbody_tr]:h-16 [&_thead_tr]:h-10 [&_td]:py-0 [&_th]:py-0">
            {pending ? <p role="status" className="flex items-center justify-center gap-2 p-8 text-sm text-ink2"><Loader2 size={16} className="animate-spin" />Wczytywanie transakcji…</p> : valid && error === null && page.items.length === 0 ? <p className="p-8 text-center text-sm text-ink2">Brak dostępnych transakcji dla tych filtrów.</p> : valid && error === null && (
              <div role="radiogroup" aria-label="Wybór transakcji">
                <Table head={['Data', 'Opis', 'Konto', 'Kwota']}>
                  {page.items.map((tx) => <tr key={tx.id} onClick={() => setSelectedId(tx.id)} className={cx('cursor-pointer', selectedId === tx.id ? 'bg-mint-soft/50' : 'hover:bg-wash')}>
                    <Td className="whitespace-nowrap text-ink2">{dayText(tx.date)}</Td>
                    <Td><button type="button" role="radio" aria-checked={selectedId === tx.id} onClick={() => setSelectedId(tx.id)} className="flex items-center gap-2 text-left font-medium"><span className={cx('flex h-5 w-5 shrink-0 items-center justify-center rounded-full border', selectedId === tx.id ? 'border-safe bg-safe text-white' : 'border-line')}>{selectedId === tx.id && <Check size={13} />}</span><span className="line-clamp-2">{tx.description ?? 'Bez opisu'}</span></button></Td>
                    <Td className="text-ink2"><span className="line-clamp-2">{accounts.find((account) => account.id === tx.bankAccountId)?.name ?? tx.bankAccountId}</span></Td>
                    <Td><TxAmount t={tx} /></Td>
                  </tr>)}
                </Table>
              </div>
            )}
          </div>
          <div className="shrink-0"><Pager page={{ ...page, hasPreviousPage: valid && !pending && page.hasPreviousPage, hasNextPage: valid && !pending && page.hasNextPage }} onPage={(pageNumber) => set({ pageNumber })} itemSummary={`Transakcje na stronie: ${valid && !pending ? page.items.length : 0}`} /></div>
        </Card>
        <div className="flex shrink-0 flex-wrap justify-end gap-2">
          <Btn variant="ghost" onClick={onCancel}>Anuluj</Btn>
          <Btn onClick={() => { if (selected && !pending && valid && error === null) onPick(selected) }} disabled={!selected || pending || !valid || error !== null}>{confirmLabel}</Btn>
        </div>
      </div>
  )
}
