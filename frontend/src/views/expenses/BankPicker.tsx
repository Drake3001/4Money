import { useEffect, useState } from 'react'
import { useRemote } from '../../api/hooks'
import { useApp } from '../../context'
import type { Tx } from '../../mocks/bank'
import { useExpenses, type BankFilter } from '../../expensesContext'
import { dayText, money } from '../../mocks/bank'
import { Badge, Btn, Field, Modal, inputCls } from '../../ui'
import { ExpErrorBox } from './ExpenseUi'

export default function BankPicker({ onPick, onClose }: { onPick: (tx: Tx) => void; onClose: () => void }) {
  const ex = useExpenses()
  const { accounts } = useApp()
  const [f, setF] = useState<BankFilter>({ accountId: '', dateFrom: '', dateTo: '', minAmount: '', maxAmount: '', description: '' })
  const set = (p: Partial<BankFilter>) => setF((x) => ({ ...x, ...p }))
  // Text and amount filters are applied after a short pause so that typing does not fire a request per keystroke.
  const [applied, setApplied] = useState(f)
  useEffect(() => { const t = setTimeout(() => setApplied(f), 350); return () => clearTimeout(t) }, [f])
  const q = useRemote(() => ex.availableBankTransactions(applied), [applied])
  const res = q.data
  const list = res?.ok ? res.data : []
  return (
    <Modal title="Wybierz transakcję bankową" onClose={onClose}>
      <p className="mb-3 text-sm text-ink2">Pokazujemy Twoje wychodzące, zaksięgowane transakcje w PLN, które nie są jeszcze przypisane do innego wydatku.</p>
      <div className="mb-4 grid gap-3 sm:grid-cols-2">
        <Field label="Konto"><select value={f.accountId} onChange={(e) => set({ accountId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select></Field>
        <Field label="Opis"><input value={f.description} onChange={(e) => set({ description: e.target.value })} className={inputCls} placeholder="np. Orlen" /></Field>
        <Field label="Data od"><input type="date" value={f.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
        <Field label="Data do"><input type="date" value={f.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
        <Field label="Kwota min. (zł)"><input inputMode="decimal" value={f.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
        <Field label="Kwota maks. (zł)"><input inputMode="decimal" value={f.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
      </div>
      <div className="max-h-72 space-y-2 overflow-y-auto">
        {q.loading && !res && <p className="py-6 text-center text-sm text-ink2">Wczytywanie transakcji…</p>}
        {res && !res.ok && <ExpErrorBox code={res.code} />}
        {q.error !== null && <ExpErrorBox code={q.error} />}
        {res?.ok && list.length === 0 && <p className="py-6 text-center text-sm text-ink2">Brak dostępnych transakcji dla tych filtrów.</p>}
        {list.map((t) => (
          <button key={t.id} onClick={() => onPick(t)} className="flex w-full items-center justify-between gap-3 rounded-xl border border-line p-3 text-left hover:bg-wash">
            <span className="min-w-0"><span className="block truncate text-sm font-semibold">{t.description ?? 'Bez opisu'}</span>
              <span className="block text-xs text-ink2">{accounts.find((a) => a.id === t.bankAccountId)?.name} · {dayText(t.date)}</span></span>
            <span className="num shrink-0 font-semibold">{money(Math.abs(Number(t.amount)))}</span>
          </button>
        ))}
      </div>
      <div className="mt-4 flex items-center justify-between"><Badge>PLN</Badge><Btn variant="ghost" onClick={onClose}>Anuluj</Btn></div>
    </Modal>
  )
}
