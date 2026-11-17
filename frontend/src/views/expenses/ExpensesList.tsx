import { useEffect, useId, useState } from 'react'
import { useRemote } from '../../api/hooks'
import { ArrowDown, ArrowUp, ChevronDown, ChevronLeft, ChevronRight, Landmark, Plus, ReceiptText, RotateCcw } from 'lucide-react'
import { useApp } from '../../context'
import { useUser } from '../../userContext'
import { DEFAULT_QUERY, useExpenses, type ExpenseQuery } from '../../expensesContext'
import { userLabel } from '../../mocks/expenses'
import { useGroups } from '../../groupsContext'
import { isNum, num } from '../../mocks/expenses'
import { money } from '../../mocks/bank'
import { Btn, Card, Field, PageTitle, Table, Td, cx, inputCls } from '../../ui'
import { CatBadge, CategoryLoadError, ExpErrorBox, useNames } from './ExpenseUi'

const tri = (v: boolean | null) => (v === null ? '' : v ? 'yes' : 'no')
const fromTri = (s: string) => (s === '' ? null : s === 'yes')

function MemberFilter({ label, enabled, value, members, onChange }: { label: string; enabled: boolean; value: string; members: string[]; onChange: (value: string) => void }) {
  const [hint, setHint] = useState(false)
  const hintId = useId()
  return (
    <div className="relative">
      <Field label={label}>
        {enabled ? (
          <select value={value} onChange={(e) => onChange(e.target.value)} className={inputCls}>
            <option value="">Wszyscy</option>{members.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}
          </select>
        ) : (
          <button type="button" aria-label={label} aria-describedby={hint ? hintId : undefined}
            onClick={() => setHint(true)} onBlur={() => setHint(false)} onKeyDown={(e) => { if (e.key === 'Escape') setHint(false) }}
            className={cx(inputCls, 'flex items-center justify-between text-left')}>
            Wszyscy<ChevronDown size={16} />
          </button>
        )}
      </Field>
      {!enabled && hint && <p id={hintId} role="status" className="absolute left-0 top-full z-20 mt-1 rounded-lg border border-line bg-white p-2 text-xs text-ink2 shadow-card">Najpierw wybierz grupę.</p>}
    </div>
  )
}

function FilterError({ code }: { code: number | null }) {
  return <ExpErrorBox code={code} message={code === 400 ? 'Dane filtrowania są nieprawidłowe. Sprawdź je i spróbuj ponownie.' : undefined} />
}

export default function ExpensesList() {
  const { navigate } = useApp()
  const { profile } = useUser()
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
      <PageTitle title="Wydatki" sub="Historia wydatków z Twoich grup." aside={<Btn onClick={() => navigate({ name: 'expense-new' })}><Plus size={16} />Dodaj wydatek</Btn>} />

      <Card className="space-y-4">
        <div>
          <div className="flex flex-wrap items-end gap-3">
            <div className="min-w-[220px] flex-1">
              <Field label="Szukaj">
                <input value={q.search} onChange={(e) => set({ search: e.target.value })} placeholder="Nazwa lub sprzedawca" aria-describedby="expense-search-hint" className={cx(inputCls, 'h-11')} />
              </Field>
            </div>
            <Field label="SORTOWANIE">
              <select value={q.sortBy} onChange={(e) => set({ sortBy: e.target.value as ExpenseQuery['sortBy'] })} className={cx(inputCls, 'h-11 !w-28')}>
                <option value="transactionTime">Data</option><option value="amount">Kwota</option><option value="name">Nazwa</option>
              </select>
            </Field>
            <button type="button" onClick={() => set({ descending: !q.descending })}
              title={q.descending ? 'Malejąco' : 'Rosnąco'} aria-label={q.descending ? 'Sortowanie malejąco. Zmień na rosnąco' : 'Sortowanie rosnąco. Zmień na malejąco'}
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-ink transition hover:bg-wash">
              {q.descending ? <ArrowDown size={18} /> : <ArrowUp size={18} />}
            </button>
            <div className="shrink-0">
              <span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">FILTROWANIE</span>
              <Btn variant="ghost" className="h-11 w-36 whitespace-nowrap" onClick={() => setOpen(!open)}>{open ? 'Mniej filtrów' : 'Więcej filtrów'}</Btn>
            </div>
            <Btn variant="soft" className="h-11" onClick={reset}><RotateCcw size={15} />Wyczyść</Btn>
          </div>
          <p id="expense-search-hint" className="mt-1 text-xs text-ink2">Wyszukaj wydatek po jego nazwie lub sprzedawcy.</p>
        </div>

        {open && (
          <div className="grid gap-3 border-t border-line pt-4 sm:grid-cols-2 lg:grid-cols-4">
            <Field label="Grupa"><select value={q.groupId} onChange={(e) => set({ groupId: e.target.value, participantId: '', payerId: '' })} className={inputCls}><option value="">Wszystkie</option>{activeGroups.map(({ group }) => <option key={group.id} value={group.id}>{group.name}</option>)}</select></Field>
            <MemberFilter label="Uczestnik" enabled={!!q.groupId} value={q.participantId} members={members} onChange={(participantId) => set({ participantId })} />
            <MemberFilter label="Płatnik" enabled={!!q.groupId} value={q.payerId} members={members} onChange={(payerId) => set({ payerId })} />
            <Field label="Kategoria"><select value={q.categoryId} disabled={ex.categoriesLoading || ex.categoriesError !== null} onChange={(e) => set({ categoryId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{ex.categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>
            <Field label="Data od"><input type="date" value={q.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
            <Field label="Data do"><input type="date" value={q.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
            <Field label="Kwota min. (zł)"><input inputMode="decimal" value={q.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
            <Field label="Kwota maks. (zł)"><input inputMode="decimal" value={q.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
            <Field label="Paragon"><select value={tri(q.hasReceipt)} onChange={(e) => set({ hasReceipt: fromTri(e.target.value) })} className={inputCls}><option value="">Dowolnie</option><option value="yes">Z paragonem</option><option value="no">Bez paragonu</option></select></Field>
            <Field label="Transakcja bankowa"><select value={tri(q.hasBankTransaction)} onChange={(e) => set({ hasBankTransaction: fromTri(e.target.value) })} className={inputCls}><option value="">Dowolnie</option><option value="yes">Z transakcją</option><option value="no">Bez transakcji</option></select></Field>
          </div>
        )}
      </Card>

      <CategoryLoadError />
      <Card className="!p-0">
        {!res ? (listQ.error !== null ? <div className="p-5"><FilterError code={listQ.error} /></div> : <p className="py-12 text-center text-ink2">Wczytywanie wydatków…</p>) : !res.ok ? (res.code === 501 ? <p className="py-12 text-center text-ink2">Lista wydatków jest chwilowo niedostępna.</p> : <div className="p-5"><FilterError code={res.code} /></div>) : (
          <div className="p-2">
            {res.data.items.length === 0 ? <p className="p-10 text-center text-ink2">Brak wydatków dla wybranych filtrów.</p> : (
              <Table head={['Wydatek', 'Grupa i płatnik', 'Kwota', 'Mój udział', 'Data']}>
                {res.data.items.map((e) => (
                  <tr key={e.id} onClick={() => navigate({ name: 'expense', id: e.id })} className="cursor-pointer hover:bg-wash">
                    <Td>
                      <p className="flex items-center gap-2 font-semibold">{e.name}
                        {e.hasReceipt && <span title="Z paragonem" aria-label="Z paragonem"><ReceiptText size={14} className="text-ink2" /></span>}
                        {e.hasBankTransaction && <span title="Z transakcją bankową" aria-label="Z transakcją bankową"><Landmark size={14} className="text-ink2" /></span>}
                      </p>
                    <p className="text-xs text-ink2">{e.merchant ? `Sprzedawca: ${e.merchant}` : 'Brak sprzedawcy'}</p>
                      <div className="mt-1.5 flex flex-wrap gap-1">{e.categoryIds.map((c) => <CatBadge key={c} id={c} compact />)}</div>
                    </Td>
                    <Td><p>{e.groupName}</p><p className="text-xs text-ink2">Płatnik: {names.user(e.payerId)}{e.payerId === profile.id && ' (Ty)'}</p></Td>
                    <Td className="num whitespace-nowrap font-semibold">{money(e.totalAmount)}</Td>
                    <Td className="num whitespace-nowrap">{e.myShareRate > 0 ? <><p className="font-semibold">{money(e.myShareAmount)}</p><p className="text-xs text-ink2">{String(e.myShareRate).replace('.', ',')}%</p></> : <span className="text-ink2">-</span>}</Td>
                    <Td className="whitespace-nowrap text-ink2">{new Date(e.transactionTime).toLocaleDateString('pl-PL', { dateStyle: 'medium' })}</Td>
                  </tr>
                ))}
              </Table>
            )}
            <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-3 py-3 text-sm">
              <span className="text-ink2">Strona {res.data.pageNumber} z {res.data.totalPages} · Wydatki na stronie: {res.data.items.length} · {res.data.pageSize} na stronę</span>
              <div className="flex gap-2">
                <Btn variant="ghost" disabled={!res.data.hasPreviousPage} onClick={() => setQ({ ...q, pageNumber: q.pageNumber - 1 })} className={cx('!min-h-9 !px-3')}><ChevronLeft size={16} />Poprzednia</Btn>
                <Btn variant="ghost" disabled={!res.data.hasNextPage} onClick={() => setQ({ ...q, pageNumber: q.pageNumber + 1 })} className="!min-h-9 !px-3">Następna<ChevronRight size={16} /></Btn>
              </div>
            </div>
          </div>
        )}
      </Card>
    </div>
  )
}
