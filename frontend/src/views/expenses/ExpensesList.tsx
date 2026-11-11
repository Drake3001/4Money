import { useEffect, useState } from 'react'
import { useRemote } from '../../api/hooks'
import { ChevronLeft, ChevronRight, Landmark, Plus, ReceiptText, RotateCcw } from 'lucide-react'
import { useApp } from '../../context'
import { DEFAULT_QUERY, useExpenses, type ExpenseQuery } from '../../expensesContext'
import { CATEGORIES, fmtDateTime, userLabel } from '../../mocks/expenses'
import { useGroups } from '../../groupsContext'
import { isNum, num } from '../../mocks/expenses'
import { money } from '../../mocks/bank'
import { Btn, Card, Field, PageTitle, Table, Td, cx, inputCls } from '../../ui'
import { CatBadge, ExpErrorBox, useNames } from './ExpenseUi'

const tri = (v: boolean | null) => (v === null ? '' : v ? 'yes' : 'no')
const fromTri = (s: string) => (s === '' ? null : s === 'yes')

export default function ExpensesList() {
  const { navigate } = useApp()
  const ex = useExpenses()
  const groups = useGroups()
  const names = useNames()
  const [q, setQ] = useState<ExpenseQuery>(DEFAULT_QUERY)
  const [open, setOpen] = useState(false)
  const set = (p: Partial<ExpenseQuery>) => setQ((x) => ({ ...x, pageNumber: 1, ...p }))
  // Filters are sent after a short pause, so typing does not fire a request per keystroke.
  const [applied, setApplied] = useState(q)
  useEffect(() => { const t = setTimeout(() => setApplied(q), 350); return () => clearTimeout(t) }, [q])
  const bad = (applied.minAmount.trim() !== '' && !isNum(applied.minAmount)) || (applied.maxAmount.trim() !== '' && !isNum(applied.maxAmount))
    || (applied.minAmount.trim() !== '' && applied.maxAmount.trim() !== '' && num(applied.minAmount) > num(applied.maxAmount)) || (!!applied.dateFrom && !!applied.dateTo && applied.dateFrom > applied.dateTo)
  const listQ = useRemote(() => ex.list(applied), [applied, groups.rev], !bad)
  const res = bad ? ({ ok: false, code: 400 } as const) : listQ.data
  const groupsQ = useRemote(() => groups.fetchList('active'), [groups.rev])
  const activeGroups = groupsQ.data ?? []
  // Participant and payer choices come from the selected group's active members.
  const membersQ = useRemote(() => groups.fetchActiveMembers(q.groupId), [q.groupId, groups.rev], !!q.groupId)
  const members = q.groupId ? membersQ.data ?? [] : []
  const reset = () => setQ(DEFAULT_QUERY)

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <PageTitle title="Wydatki" sub="Historia wydatków z Twoich grup. Wszystkie kwoty w PLN." aside={<Btn onClick={() => navigate({ name: 'expense-new' })}><Plus size={16} />Dodaj wydatek</Btn>} />

      <Card className="space-y-4">
        <div className="flex flex-wrap items-end gap-3">
          <div className="min-w-[220px] flex-1">
            <Field label="Szukaj" hint="Wyszukiwanie po nazwie wydatku lub sprzedawcy.">
              <input value={q.search} onChange={(e) => set({ search: e.target.value })} placeholder="Nazwa lub sprzedawca" className={inputCls} />
            </Field>
          </div>
          <Field label="Sortuj wg">
            <select value={q.sortBy} onChange={(e) => set({ sortBy: e.target.value as ExpenseQuery['sortBy'] })} className={inputCls}>
              <option value="transactionTime">Czas transakcji</option><option value="amount">Kwota</option><option value="name">Nazwa</option>
            </select>
          </Field>
          <Field label="Kolejność">
            <select value={q.descending ? 'desc' : 'asc'} onChange={(e) => set({ descending: e.target.value === 'desc' })} className={inputCls}>
              <option value="desc">Malejąco</option><option value="asc">Rosnąco</option>
            </select>
          </Field>
          <Btn variant="ghost" onClick={() => setOpen(!open)}>{open ? 'Mniej filtrów' : 'Więcej filtrów'}</Btn>
          <Btn variant="soft" onClick={reset}><RotateCcw size={15} />Wyczyść</Btn>
        </div>

        {open && (
          <div className="grid gap-3 border-t border-line pt-4 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Grupa"><select value={q.groupId} onChange={(e) => set({ groupId: e.target.value, participantId: '', payerId: '' })} className={inputCls}><option value="">Wszystkie</option>{activeGroups.map(({ group }) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></Field>
            <Field label="Uczestnik" hint={q.groupId ? undefined : 'Najpierw wybierz grupę.'}><select value={q.participantId} disabled={!q.groupId} onChange={(e) => set({ participantId: e.target.value })} className={inputCls}><option value="">Wszyscy</option>{members.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}</select></Field>
            <Field label="Płatnik" hint={q.groupId ? undefined : 'Najpierw wybierz grupę.'}><select value={q.payerId} disabled={!q.groupId} onChange={(e) => set({ payerId: e.target.value })} className={inputCls}><option value="">Wszyscy</option>{members.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}</select></Field>
            <Field label="Kategoria"><select value={q.categoryId} onChange={(e) => set({ categoryId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{CATEGORIES.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>
            <Field label="Data od"><input type="date" value={q.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
            <Field label="Data do"><input type="date" value={q.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
            <Field label="Kwota min. (zł)"><input inputMode="decimal" value={q.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
            <Field label="Kwota maks. (zł)"><input inputMode="decimal" value={q.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
            <Field label="Paragon"><select value={tri(q.hasReceipt)} onChange={(e) => set({ hasReceipt: fromTri(e.target.value) })} className={inputCls}><option value="">Dowolnie</option><option value="yes">Z paragonem</option><option value="no">Bez paragonu</option></select></Field>
            <Field label="Transakcja bankowa"><select value={tri(q.hasBankTransaction)} onChange={(e) => set({ hasBankTransaction: fromTri(e.target.value) })} className={inputCls}><option value="">Dowolnie</option><option value="yes">Z transakcją</option><option value="no">Bez transakcji</option></select></Field>
          </div>
        )}
      </Card>

      {!res ? (listQ.error !== null ? <ExpErrorBox code={listQ.error} /> : <Card className="py-12 text-center text-ink2">Wczytywanie wydatków…</Card>) : !res.ok ? (res.code === 501 ? <Card className="py-12 text-center text-ink2">Lista wydatków jest chwilowo niedostępna.</Card> : <ExpErrorBox code={res.code} />) : (
        <Card className="!p-2">
          {res.data.items.length === 0 ? <p className="p-10 text-center text-ink2">Brak wydatków dla wybranych filtrów.</p> : (
            <Table head={['Wydatek', 'Grupa i płatnik', 'Kwota', 'Mój udział', 'Czas transakcji']}>
              {res.data.items.map((e) => (
                <tr key={e.id} onClick={() => navigate({ name: 'expense', id: e.id })} className="cursor-pointer hover:bg-wash">
                  <Td>
                    <p className="flex items-center gap-2 font-semibold">{e.name}
                      {e.hasReceipt && <span title="Z paragonem" aria-label="Z paragonem"><ReceiptText size={14} className="text-ink2" /></span>}
                      {e.hasBankTransaction && <span title="Z transakcją bankową" aria-label="Z transakcją bankową"><Landmark size={14} className="text-ink2" /></span>}
                    </p>
                    <p className="text-xs text-ink2">{e.merchant ?? 'Brak sprzedawcy'}</p>
                    <div className="mt-1.5 flex flex-wrap gap-1">{e.categoryIds.map((c) => <CatBadge key={c} id={c} compact />)}</div>
                  </Td>
                  <Td><p>{e.groupName}</p><p className="text-xs text-ink2">Płaci: {names.user(e.payerId)}</p></Td>
                  <Td className="num whitespace-nowrap font-semibold">{money(e.totalAmount)}</Td>
                  <Td className="num whitespace-nowrap">{e.myShareRate > 0 ? <><p className="font-semibold">{money(e.myShareAmount)}</p><p className="text-xs text-ink2">{String(e.myShareRate).replace('.', ',')}%</p></> : <span className="text-ink2">—</span>}</Td>
                  <Td className="whitespace-nowrap text-ink2">{fmtDateTime(e.transactionTime)}</Td>
                </tr>
              ))}
            </Table>
          )}
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-3 py-3 text-sm">
            <span className="text-ink2">Strona {res.data.pageNumber} z {res.data.totalPages} · wyników: {res.data.totalCount} · {res.data.pageSize} na stronę</span>
            <div className="flex gap-2">
              <Btn variant="ghost" disabled={!res.data.hasPreviousPage} onClick={() => setQ({ ...q, pageNumber: q.pageNumber - 1 })} className={cx('!min-h-9 !px-3')}><ChevronLeft size={16} />Poprzednia</Btn>
              <Btn variant="ghost" disabled={!res.data.hasNextPage} onClick={() => setQ({ ...q, pageNumber: q.pageNumber + 1 })} className="!min-h-9 !px-3">Następna<ChevronRight size={16} /></Btn>
            </div>
          </div>
        </Card>
      )}
    </div>
  )
}
