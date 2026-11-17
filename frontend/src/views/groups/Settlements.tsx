import { useState } from 'react'
import { useRemote } from '../../api/hooks'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import { ME_ID, PAYMENT_METHODS, paymentLabel, userLabel, type GroupData, type PaymentMethod, type Settlement, type SettlementSuggestion } from '../../mocks/groups'
import { money } from '../../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, Modal, Table, Td, inputCls } from '../../ui'
import { useSettlementService } from '../../services/settlementService'
import { ErrorBox } from './GroupUi'

const when = (iso: string) => new Date(iso).toLocaleString('pl-PL', { dateStyle: 'medium', timeStyle: 'short' })
const toLocalInput = (d: Date) => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16)

type Draft = { fromUserId: string; toUserId: string; amount: string; method: PaymentMethod; settledAt: string }

function SettlementForm({ g, isAdmin, editing, seed, onClose }: { g: GroupData; isAdmin: boolean; editing?: Settlement; seed?: Partial<Draft>; onClose: () => void }) {
  const svc = useSettlementService()
  const { showToast } = useApp()
  const members = g.members.filter((m) => m.status === 'active').map((m) => m.userId)
  const [f, setF] = useState<Draft>(() => editing
    ? { fromUserId: editing.fromUserId, toUserId: editing.toUserId, amount: String(editing.amount).replace('.', ','), method: editing.method as PaymentMethod, settledAt: toLocalInput(new Date(editing.settledAt)) }
    : { fromUserId: ME_ID, toUserId: '', amount: '', method: 2, settledAt: toLocalInput(new Date()), ...seed })
  const [submitted, setSubmitted] = useState(false)
  const [apiErr, setApiErr] = useState<number | null>(null)
  const set = (p: Partial<Draft>) => setF((x) => ({ ...x, ...p }))

  const amount = Number(f.amount.replace(',', '.'))
  const errs: Record<string, string> = {}
  if (!f.fromUserId) errs.from = 'Wybierz osobę, która płaci.'
  if (!f.toUserId) errs.to = 'Wybierz osobę, która otrzymuje płatność.'
  else if (f.fromUserId === f.toUserId) errs.to = 'Płatnik i odbiorca muszą być różnymi osobami.'
  if (!f.amount.trim() || !Number.isFinite(amount) || amount <= 0) errs.amount = 'Podaj kwotę większą od zera.'
  if (!f.settledAt || new Date(f.settledAt).getTime() > Date.now()) errs.date = f.settledAt ? 'Wprowadź poprawną datę.' : 'Podaj datę i godzinę rozliczenia.'
  const show = (k: string) => (submitted ? errs[k] : undefined)

  const submit = async () => {
    setSubmitted(true); setApiErr(null)
    if (Object.keys(errs).length) return
    const input = { ...f, settledAt: new Date(f.settledAt).toISOString() }
    const r = editing ? await svc.updateSettlement(g.id, editing.id, input) : await svc.createSettlement(g.id, input)
    if (!r.ok) return setApiErr(r.code)
    showToast(editing ? 'Rozliczenie zostało zaktualizowane' : 'Rozliczenie zostało zapisane')
    onClose()
  }
  // Normal members may only record their own outgoing payments.
  const fromChoices = isAdmin ? members : members.filter((u) => u === ME_ID || u === editing?.fromUserId)
  return (
    <Modal title={editing ? 'Edytuj rozliczenie' : 'Dodaj rozliczenie'} onClose={onClose}>
      <p className="mb-4 text-sm text-ink2">Zapisz płatność, która została już wykonana poza 4Money. Aplikacja nie wykonuje przelewów.</p>
      <div className="space-y-1">
        <Field label="Od (płaci)" hint={show('from')} reserveHint><select value={f.fromUserId} onChange={(e) => set({ fromUserId: e.target.value })} className={inputCls}>{fromChoices.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}</select></Field>
        <Field label="Do (otrzymuje)" hint={show('to')} reserveHint><select value={f.toUserId} onChange={(e) => set({ toUserId: e.target.value })} className={inputCls}><option value="">Wybierz członka…</option>{members.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}</select></Field>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Kwota (PLN)" hint={show('amount')} reserveHint><input inputMode="decimal" value={f.amount} onChange={(e) => set({ amount: e.target.value })} className={inputCls} placeholder="0,00" /></Field>
          <Field label="Metoda płatności"><select value={f.method} onChange={(e) => set({ method: Number(e.target.value) as PaymentMethod })} className={inputCls}>{PAYMENT_METHODS.map((m) => <option key={m.v} value={m.v}>{m.label}</option>)}</select></Field>
        </div>
        <Field label="Data i godzina rozliczenia" hint={show('date')} reserveHint><input type="datetime-local" value={f.settledAt} max={toLocalInput(new Date())} onChange={(e) => set({ settledAt: e.target.value })} className={inputCls} /></Field>
        <ErrorBox code={apiErr} />
      </div>
      <div className="mt-3 flex justify-end gap-2"><Btn variant="ghost" onClick={onClose}>Anuluj</Btn><Btn onClick={submit}>{editing ? 'Zapisz zmiany' : 'Zapisz rozliczenie'}</Btn></div>
    </Modal>
  )
}

export default function SettlementsTab({ g, isAdmin }: { g: GroupData; isAdmin: boolean }) {
  const groups = useGroups()
  const svc = useSettlementService()
  const { showToast } = useApp()
  const listQ = useRemote(() => groups.fetchSettlements(g.id), [g.id, groups.rev])
  const sugQ = useRemote(() => groups.fetchSuggestions(g.id), [g.id, groups.rev])
  const list = listQ.data ?? []
  const sug = sugQ.data
  const [form, setForm] = useState<{ editing?: Settlement; seed?: Partial<Draft> } | null>(null)
  const [view, setView] = useState<string | null>(null)
  const [del, setDel] = useState<string | null>(null)
  const [err, setErr] = useState<number | null>(null)
  const can = (s: Settlement) => isAdmin || s.fromUserId === ME_ID
  const detail = view ? list.find((s) => s.id === view) : undefined
  const toDelete = del ? list.find((s) => s.id === del) : undefined
  const useSuggestion = (s: SettlementSuggestion) => setForm({ seed: { fromUserId: s.fromUserId, toUserId: s.toUserId, amount: String(s.amount).replace('.', ',') } })

  return (
    <div className="space-y-6">
      <Card>
        <H>Sugerowane rozliczenia</H>
        <p className="mb-3 text-sm text-ink2">Propozycje na podstawie bieżących sald w grupie. To tylko informacja - nic nie jest wykonywane automatycznie. Gdy już zapłacisz, zapisz rozliczenie osobno.</p>
        {sugQ.error !== null ? <ErrorBox code={sugQ.error} /> : !sug ? <p role="status" className="text-sm text-ink2">Wczytywanie propozycji…</p> : sug.suggestions.length === 0 ? <p className="rounded-xl bg-wash p-4 text-center text-sm text-ink2">Wszystko rozliczone - brak sugerowanych płatności.</p> : (
          <ul className="divide-y divide-line">
            {sug.suggestions.map((s, i) => (
              <li key={i} className="flex flex-wrap items-center justify-between gap-3 py-3">
                <p className="text-sm"><span className="font-semibold">{userLabel(s.fromUserId)}</span> <span className="text-ink2">powinien(-na) zapłacić</span> <span className="font-semibold">{userLabel(s.toUserId)}</span></p>
                <div className="flex items-center gap-3"><span className="num font-display text-lg font-bold">{money(s.amount)}</span>{(isAdmin || s.fromUserId === ME_ID) && <Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => useSuggestion(s)}>Zapisz jako rozliczenie</Btn>}</div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <div>
        <H aside={<Btn onClick={() => setForm({})}><Plus size={16} />Dodaj rozliczenie</Btn>}>Historia rozliczeń</H>
        <Card className="!p-2">
          {listQ.error !== null ? <div className="p-4"><ErrorBox code={listQ.error} /></div> : list.length === 0 ? <p className="p-8 text-center text-ink2">Brak zapisanych rozliczeń w tej grupie.</p> : (
            <Table head={['Data', 'Od', 'Do', 'Kwota', 'Metoda', '']}>
              {list.map((s) => (
                <tr key={s.id}>
                  <Td className="text-ink2">{when(s.settledAt)}</Td>
                  <Td className="font-medium">{userLabel(s.fromUserId)}</Td>
                  <Td className="font-medium">{userLabel(s.toUserId)}</Td>
                  <Td className="num font-semibold">{money(s.amount)}</Td>
                  <Td><Badge>{paymentLabel(s.method)}</Badge></Td>
                  <Td>
                    <div className="flex justify-end gap-1.5">
                      <Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => setView(s.id)}>Szczegóły</Btn>
                      {can(s) && <button aria-label="Edytuj rozliczenie" title="Edytuj" onClick={() => setForm({ editing: s })} className="rounded-lg p-2 text-ink2 hover:bg-wash"><Pencil size={16} /></button>}
                      {can(s) && <button aria-label="Usuń rozliczenie" title="Usuń" onClick={() => { setErr(null); setDel(s.id) }} className="rounded-lg p-2 text-ink2 hover:bg-danger-soft hover:text-danger"><Trash2 size={16} /></button>}
                    </div>
                  </Td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
        <p className="mt-2 text-xs text-ink2">Rozliczenie to płatność, która została już wykonana poza 4Money. Aplikacja nie wykonuje przelewów.</p>
      </div>

      {form && <SettlementForm g={g} isAdmin={isAdmin} editing={form.editing} seed={form.seed} onClose={() => setForm(null)} />}

      {detail && (
        <Modal title="Szczegóły rozliczenia" onClose={() => setView(null)}>
          <KeyValue rows={[
            ['Od', userLabel(detail.fromUserId)], ['Do', userLabel(detail.toUserId)],
            ['Kwota', <span key="a" className="num font-semibold">{money(detail.amount)}</span>], ['Waluta', <Badge key="c">{g.currencyCode}</Badge>],
            ['Metoda płatności', paymentLabel(detail.method)], ['Data rozliczenia', when(detail.settledAt)],
          ]} />
          <div className="mt-3 flex justify-end gap-2">
            {can(detail) && <Btn variant="ghost" onClick={() => { setView(null); setForm({ editing: detail }) }}>Edytuj</Btn>}
            <Btn onClick={() => setView(null)}>Zamknij</Btn>
          </div>
        </Modal>
      )}

      {toDelete && (
        <Modal title="Usunąć rozliczenie?" onClose={() => setDel(null)}>
          <p className="mb-4 text-sm text-ink2">Rozliczenie {userLabel(toDelete.fromUserId)} → {userLabel(toDelete.toUserId)} na kwotę {money(toDelete.amount)} zostanie usunięte z historii grupy. Sugerowane rozliczenia zostaną przeliczone.</p>
          <ErrorBox code={err} />
          <div className="mt-3 flex justify-end gap-2">
            <Btn variant="ghost" onClick={() => setDel(null)}>Anuluj</Btn>
            <Btn variant="danger" onClick={async () => { const r = await svc.deleteSettlement(g.id, toDelete.id); if (r.ok) { setDel(null); showToast('Rozliczenie zostało usunięte') } else setErr(r.code) }}>Usuń rozliczenie</Btn>
          </div>
        </Modal>
      )}
    </div>
  )
}
