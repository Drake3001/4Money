import { useState } from 'react'
import { AlertTriangle } from 'lucide-react'
import type { ExpenseForm, FormItem, OcrAnalysis, Receipt } from '../../mocks/expenses'
import { emptyItem, category, fmtDay, uid } from '../../mocks/expenses'
import { money } from '../../mocks/bank'
import { Badge, Btn, Card, H, cx } from '../../ui'
import { CatBadge, ReceiptThumb } from './ExpenseUi'

export type OcrMode = 'image' | 'overwrite' | 'append' | 'restore'
const MODES: { v: OcrMode; label: string; desc: string }[] = [
  { v: 'image', label: 'Tylko obraz', desc: 'Dołącz sam paragon, bez używania danych z OCR.' },
  { v: 'overwrite', label: 'Nadpisz danymi z OCR', desc: 'Użyj danych zakupu z OCR i zastąp zwykłe produkty. Grupa, płatnik i uczestnicy zostają.' },
  { v: 'append', label: 'Dopisz produkty z OCR', desc: 'Dodaj produkty z OCR do istniejących. Nie usuwamy duplikatów automatycznie.' },
  { v: 'restore', label: 'Przywróć pierwotny OCR', desc: 'Użyj pierwszej analizy tego paragonu zamiast najnowszej.' },
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
    lines.push(`Produkty: dopisano ${a.items.length} z OCR do ${form.items.filter((i) => i.name.trim() || i.unitPrice.trim()).length} istniejących.`)
    return { next, lines }
  }
  const upd = (label: string, cur: string, val: string | undefined, set: (v: string) => void) => {
    if (val === undefined || val === '') lines.push(`${label}: nie wykryto – bez zmian${cur ? ` (${cur})` : ''}.`)
    else { lines.push(`${label}: ${cur || 'puste'} → ${val}`); set(val) }
  }
  upd('Nazwa', form.name, a.name, (v) => (next.name = v))
  upd('Sprzedawca', form.merchant, a.merchant, (v) => (next.merchant = v))
  if (hasBank) lines.push('Kwota: ustalona przez powiązaną transakcję bankową – bez zmian.')
  else upd('Kwota', form.amount, a.amount != null ? String(a.amount) : undefined, (v) => (next.amount = v))
  upd('Data', form.date, a.transactionDate, (v) => (next.date = v))
  next.items = a.items.length ? a.items.map(toFormItem) : form.items
  lines.push(a.items.length ? `Produkty: zastąpiono ${form.items.filter((i) => i.name.trim()).length} istniejących produktami z OCR (${a.items.length}).` : 'Produkty: OCR ich nie wykrył – bez zmian.')
  return { next, lines }
}

function Status({ missing, review }: { missing?: boolean; review?: boolean }) {
  if (missing) return <Badge tone="warn">Nie wykryto</Badge>
  if (review) return <Badge tone="info">Wymaga sprawdzenia</Badge>
  return <Badge tone="ok">Wykryto</Badge>
}

export default function OcrPanel({ receipt, form, hasBank, onApply, onClose }: { receipt: Receipt; form: ExpenseForm; hasBank: boolean; onApply: (f: ExpenseForm) => void; onClose: () => void }) {
  const latest = receipt.latestAnalysis
  const original = receipt.originalAnalysis
  const [mode, setMode] = useState<OcrMode>('overwrite')
  const [pending, setPending] = useState<{ next: ExpenseForm; lines: string[] } | null>(null)
  const source = mode === 'restore' ? original : latest

  if (!latest) return null
  const a = latest
  const noCat = a.items.filter((i) => !i.categoryId).length

  return (
    <Card className="space-y-5 !border-personal/40">
      <H aside={<Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={onClose}>Zamknij</Btn>}>Wynik analizy paragonu</H>
      {a.needsReview && <p className="flex items-start gap-2 rounded-xl bg-warn-soft p-3 text-sm text-warn"><AlertTriangle size={16} className="mt-0.5 shrink-0" />Analiza wymaga sprawdzenia. Zweryfikuj dane przed użyciem.</p>}
      {a.warnings.length > 0 && <ul className="list-disc space-y-1 rounded-xl bg-wash p-3 pl-7 text-sm text-ink2">{a.warnings.map((w) => <li key={w}>{w}</li>)}</ul>}

      <div className="grid gap-5 md:grid-cols-[180px_1fr]">
        <ReceiptThumb r={receipt} size="lg" />
        <dl className="divide-y divide-line text-sm">
          {([
            ['Sprzedawca', a.merchant ?? '—', a.merchant === undefined, false],
            ['Kwota', a.amount != null ? money(a.amount) : '—', a.amount == null, false],
            ['Data', a.transactionDate ? fmtDay(a.transactionDate) : '—', !a.transactionDate, false],
            ['Nazwa', a.name ?? '—', !a.name, false],
          ] as [string, string, boolean, boolean][]).map(([k, v, miss]) => (
            <div key={k} className="flex items-center justify-between gap-3 py-2.5"><dt className="text-ink2">{k}</dt><dd className="flex items-center gap-2 font-semibold">{v}<Status missing={miss} /></dd></div>
          ))}
          <div className="flex items-center justify-between gap-3 py-2.5"><dt className="text-ink2">Pozycje</dt><dd className="flex items-center gap-2 font-semibold">{a.items.length}<Status missing={a.items.length === 0} review={noCat > 0} /></dd></div>
        </dl>
      </div>

      {a.items.length > 0 && (
        <div className="soft-scroll overflow-x-auto">
          <table className="w-full min-w-[480px] text-left text-sm">
            <thead><tr className="border-b border-line text-xs uppercase tracking-wide text-ink2"><th className="px-2 py-2">Pozycja</th><th className="px-2 py-2">Proponowana kategoria</th><th className="px-2 py-2 text-right">Cena</th><th className="px-2 py-2 text-right">Ilość</th></tr></thead>
            <tbody className="divide-y divide-line">
              {a.items.map((i, n) => (
                <tr key={n}>
                  <td className="px-2 py-2 font-medium">{i.name}</td>
                  <td className="px-2 py-2">{i.categoryId ? <CatBadge id={i.categoryId} compact /> : <Badge tone="warn">Wymaga wyboru kategorii</Badge>}</td>
                  <td className="num px-2 py-2 text-right">{i.unitPrice != null ? money(i.unitPrice) : <Badge tone="warn">Brak ceny</Badge>}</td>
                  <td className="num px-2 py-2 text-right">{i.quantity ?? 1}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {noCat > 0 && <p className="mt-2 text-xs text-ink2">Dla pozycji bez kategorii musisz wybrać kategorię w formularzu przed zapisem wydatku. Kategoria „Nieznane” nie jest wybierana ręcznie ani automatycznie.</p>}
        </div>
      )}

      <div>
        <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-ink2">Jak użyć wyniku OCR</p>
        <div className="grid gap-2 sm:grid-cols-2" role="radiogroup">
          {MODES.map((m) => {
            const disabled = (m.v === 'restore' && !original) || (m.v !== 'image' && !(m.v === 'restore' ? original : latest))
            return (
              <button key={m.v} type="button" role="radio" aria-checked={mode === m.v} disabled={disabled} onClick={() => { setMode(m.v); setPending(null) }}
                className={cx('rounded-xl border p-3 text-left disabled:cursor-not-allowed disabled:opacity-45', mode === m.v ? 'border-personal bg-mint-soft/40' : 'border-line hover:border-ink/30')}>
                <p className="text-sm font-semibold">{m.label}</p><p className="mt-0.5 text-xs text-ink2">{m.desc}</p>
              </button>
            )
          })}
        </div>
        {!pending ? (
          <div className="mt-3"><Btn variant="ghost" disabled={!source && mode !== 'image'} onClick={() => source && setPending(applyOcr(form, source, mode, hasBank))}>Pokaż zmiany</Btn></div>
        ) : (
          <div className="mt-3 space-y-3 rounded-xl bg-wash p-4">
            <p className="text-sm font-semibold">Zmiany do zatwierdzenia (nic nie jest jeszcze zapisane)</p>
            <ul className="list-disc space-y-1 pl-5 text-sm text-ink2">{pending.lines.map((l) => <li key={l}>{l}</li>)}</ul>
            <div className="flex flex-wrap gap-2">
              <Btn onClick={() => { onApply(pending.next); setPending(null); onClose() }}>Zastosuj w formularzu</Btn>
              <Btn variant="ghost" onClick={() => setPending(null)}>Anuluj</Btn>
            </div>
            <p className="text-xs text-ink2">Po zastosowaniu możesz ręcznie poprawić dane. Wydatek zapiszesz dopiero na końcu formularza.</p>
          </div>
        )}
      </div>
    </Card>
  )
}
void category
