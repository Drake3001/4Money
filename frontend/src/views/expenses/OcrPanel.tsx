import { useState } from 'react'
import { AlertTriangle, X } from 'lucide-react'
import type { ExpenseForm, FormItem, OcrAnalysis, OcrItem, Receipt } from '../../mocks/expenses'
import { emptyItem, fmtDay, itemTotal, uid } from '../../mocks/expenses'
import { money } from '../../mocks/bank'
import { Badge, Btn, Card, H, cx } from '../../ui'
import { CatBadge, ReceiptThumb } from './ExpenseUi'

export type OcrMode = 'image' | 'overwrite' | 'append' | 'restore'
const MODES: { v: OcrMode; label: string; desc: string }[] = [
  { v: 'image', label: 'Tylko obraz', desc: 'Dołącz sam paragon, bez używania danych z analizy.' },
  { v: 'overwrite', label: 'Nadpisz danymi z analizy', desc: 'Użyj danych zakupu z analizy i zastąp zwykłe produkty. Grupa, płatnik i uczestnicy zostają.' },
  { v: 'append', label: 'Dopisz produkty z analizy', desc: 'Dodaj produkty z analizy do istniejących. Duplikaty nie zostaną usunięte automatycznie.' },
  { v: 'restore', label: 'Użyj pierwotnej analizy', desc: 'Użyj pierwotnej analizy tego paragonu zamiast najnowszej.' },
]

const toFormItem = (o: OcrAnalysis['items'][number]): FormItem => ({
  ...emptyItem(), id: uid('it'), categoryId: o.categoryId ?? '', name: o.name, unit: o.unit ?? '',
  unitPrice: o.unitPrice != null ? String(o.unitPrice) : '', quantity: o.quantity != null ? String(o.quantity) : '1', discount: o.discount ? String(o.discount) : '',
})

// Computes the proposed form after applying OCR. Nothing is saved or applied until the user confirms.
export function applyOcr(form: ExpenseForm, a: OcrAnalysis, mode: OcrMode, hasBank: boolean): { next: ExpenseForm; lines: string[] } {
  const lines: string[] = []
  let next = { ...form }
  if (mode === 'image') return { next, lines: ['Paragon zostanie dołączony do wydatku. Dane wydatku się nie zmienią.'] }
  if (mode === 'append') {
    next.items = [...form.items.filter((i) => i.name.trim() || i.unitPrice.trim()), ...a.items.map(toFormItem)]
    lines.push(`Liczba nowych produktów: ${a.items.length}.`)
    return { next, lines }
  }
  const upd = (label: string, cur: string, val: string | undefined, set: (v: string) => void) => {
    if (val === undefined || val === '') lines.push(`${label}: nie wykryto - bez zmian${cur ? ` - ${cur}` : ''}.`)
    else { lines.push(`${label}: ${cur || 'puste'} → ${val}`); set(val) }
  }
  upd('Nazwa', form.name, a.name, (v) => (next.name = v))
  upd('Sprzedawca', form.merchant, a.merchant, (v) => (next.merchant = v))
  if (hasBank) lines.push('Kwota: ustalona przez powiązaną transakcję bankową - bez zmian.')
  else upd('Kwota', form.amount, a.amount != null ? String(a.amount) : undefined, (v) => (next.amount = v))
  upd('Data', form.date, a.transactionDate, (v) => (next.date = v))
  next.items = a.items.length ? a.items.map(toFormItem) : form.items
  lines.push(a.items.length ? `Liczba nowych produktów: ${a.items.length}.` : 'Produkty: analiza ich nie wykryła - bez zmian.')
  return { next, lines }
}

function Status({ missing, review }: { missing?: boolean; review?: boolean }) {
  if (missing) return <Badge tone="warn">Nie wykryto</Badge>
  if (review) return <Badge tone="info">Wymaga sprawdzenia</Badge>
  return <Badge tone="ok">Wykryto</Badge>
}

export function OcrItemsTable({ items }: { items: OcrItem[] }) {
  return (
    <div className="soft-scroll overflow-x-auto">
      <table className="w-full min-w-[560px] text-left text-sm">
        <thead className="border-b border-line text-xs uppercase tracking-wide text-ink2">
          <tr>
            <th className="px-2 py-2">Pozycja</th>
            <th className="px-2 py-2">Proponowana kategoria</th>
            <th className="px-2 py-2 text-right">Cena jedn.</th>
            <th className="px-2 py-2 text-right">Ilość</th>
            <th className="px-2 py-2 text-right">Rabat</th>
            <th className="px-2 py-2 text-right">Razem</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {items.map((item, index) => {
            const { unitPrice, quantity, discount } = item
            const total = unitPrice != null && quantity != null ? itemTotal({ unitPrice, quantity, discount: discount ?? 0 }) : null
            return (
              <tr key={index}>
                <td className="px-2 py-2 font-medium">
                  {item.name}
                  {item.needsReview && <div className="mt-1"><Status review /></div>}
                </td>
                <td className="px-2 py-2">{item.categoryId ? <CatBadge id={item.categoryId} compact /> : <Badge tone="warn">Wymaga wyboru kategorii</Badge>}</td>
                <td className="num px-2 py-2 text-right">{unitPrice != null ? money(unitPrice) : <Badge tone="warn">Brak ceny</Badge>}</td>
                <td className="num px-2 py-2 text-right">{quantity ?? '–'}</td>
                <td className="num px-2 py-2 text-right">{discount ? money(discount) : '–'}</td>
                <td className="num px-2 py-2 text-right">{total != null ? money(total) : '–'}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

export default function OcrPanel({ receipt, form, hasBank, onApply, onClose }: { receipt: Receipt; form: ExpenseForm; hasBank: boolean; onApply: (f: ExpenseForm) => void; onClose: () => void }) {
  const latest = receipt.latestAnalysis
  const original = receipt.originalAnalysis
  const [mode, setMode] = useState<OcrMode>('overwrite')
  const [changesOpen, setChangesOpen] = useState(false)
  const source = mode === 'restore' ? original : latest
  const pending = source ? applyOcr(form, source, mode, hasBank) : null

  if (!latest) return null
  const a = latest
  const noCat = a.items.filter((i) => !i.categoryId).length

  return (
    <Card className="space-y-5 !border-ink/40">
      <H aside={<Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={onClose}>Zamknij</Btn>}>Wynik analizy paragonu</H>
      {a.needsReview && <p className="flex items-start gap-2 rounded-xl bg-warn-soft p-3 text-sm text-warn"><AlertTriangle size={16} className="mt-0.5 shrink-0" />Analiza wymaga sprawdzenia. Zweryfikuj dane przed zapisaniem wydatku.</p>}

      <div className="grid gap-5 md:grid-cols-[180px_1fr]">
        <ReceiptThumb r={receipt} size="lg" />
        <dl className="divide-y divide-line text-sm">
          {([
            ['Sprzedawca', a.merchant ?? '-', a.merchant === undefined, false],
            ['Kwota', a.amount != null ? money(a.amount) : '-', a.amount == null, false],
            ['Data', a.transactionDate ? fmtDay(a.transactionDate) : '-', !a.transactionDate, false],
            ['Nazwa', a.name ?? '-', !a.name, false],
          ] as [string, string, boolean, boolean][]).map(([k, v, miss]) => (
            <div key={k} className="flex items-center justify-between gap-3 py-2.5"><dt className="text-ink2">{k}</dt><dd className="flex items-center gap-2 font-semibold">{v}<Status missing={miss} /></dd></div>
          ))}
          <div className="flex items-center justify-between gap-3 py-2.5"><dt className="text-ink2">Pozycje</dt><dd className="flex items-center gap-2 font-semibold">{a.items.length}<Status missing={a.items.length === 0} review={noCat > 0} /></dd></div>
        </dl>
      </div>

      {a.items.length > 0 && (
        <div>
          <OcrItemsTable items={a.items} />
          {noCat > 0 && <p className="mt-2 text-xs text-ink2">Dla pozycji bez kategorii musisz wybrać kategorię w formularzu przed zapisem wydatku. Kategoria „Nieznane” nie jest wybierana ręcznie ani automatycznie.</p>}
        </div>
      )}

      <div>
        <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-ink2">Jak użyć wyniku analizy</p>
        <div className="grid gap-2 sm:grid-cols-2" role="radiogroup">
          {MODES.map((m) => {
            const disabled = (m.v === 'restore' && !original) || (m.v !== 'image' && !(m.v === 'restore' ? original : latest))
            return (
              <button key={m.v} type="button" role="radio" aria-checked={mode === m.v} disabled={disabled} onClick={() => { setMode(m.v); setChangesOpen(false) }}
                className={cx('rounded-xl border p-3 text-left disabled:cursor-not-allowed disabled:opacity-45', mode === m.v ? 'border-ink bg-mint-soft/40' : 'border-line hover:border-ink/30')}>
                <p className="text-sm font-semibold">{m.label}</p><p className="mt-0.5 text-xs text-ink2">{m.desc}</p>
              </button>
            )
          })}
        </div>
        <div className="mt-3 flex flex-wrap gap-2">
          <Btn variant="ghost" disabled={!pending} onClick={() => setChangesOpen(true)}>Pokaż zmiany</Btn>
          <Btn disabled={!pending} onClick={() => { if (pending) { onApply(pending.next); onClose() } }}>Zastosuj zmiany</Btn>
        </div>
        {changesOpen && pending && (
          <div className="mt-3 space-y-3 rounded-xl bg-wash p-4">
            <div className="flex items-start justify-between gap-3">
              <p className="text-sm font-semibold">Zmiany do zatwierdzenia</p>
              <button type="button" aria-label="Zamknij podgląd zmian" onClick={() => setChangesOpen(false)} className="rounded-lg p-1 text-ink2 hover:bg-line hover:text-ink"><X size={16} /></button>
            </div>
            <ul className="list-disc space-y-1 pl-5 text-sm text-ink2">{pending.lines.map((l) => <li key={l}>{l}</li>)}</ul>
            <p className="text-xs text-ink2">Po zastosowaniu możesz ręcznie poprawić dane. Wydatek zapiszesz dopiero na końcu formularza.</p>
          </div>
        )}
      </div>
    </Card>
  )
}
