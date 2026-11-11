import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useRemote } from '../../api/hooks'
import { ArrowLeft, Landmark, Loader2, Pencil, ReceiptText, ScanLine, Trash2, Upload } from 'lucide-react'
import { useApp } from '../../context'
import { useExpenses, type BankFilter } from '../../expensesContext'
import { useExpenseService } from '../../services/expenseService'
import { MAX_RECEIPT_BYTES, RECEIPT_EXT, emptyItem, type ExpenseForm as Form, type OcrAnalysis } from '../../mocks/expenses'
import { ME_ID } from '../../mocks/groups'
import { dayText, money, type Tx } from '../../mocks/bank'
import { Badge, Btn, Card, Field, H, PageTitle, cx, inputCls } from '../../ui'
import ExpenseForm, { type FormInit } from './ExpenseForm'
import { applyOcr } from './OcrPanel'
import { CatBadge, ExpErrorBox, ReceiptThumb } from './ExpenseUi'

type Step = 'source' | 'bank' | 'upload' | 'ocr' | 'form'
type Source = 'bank' | 'manual' | 'receipt'

const BackLink = ({ onClick, children }: { onClick: () => void; children: string }) => (
  <button onClick={onClick} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />{children}</button>
)

/* ---------- Step 1: choose how to create the expense ---------- */
function SourceStep({ onPick, onCancel }: { onPick: (s: Source) => void; onCancel: () => void }) {
  const tiles: { s: Source; icon: ReactNode; title: string; text: string }[] = [
    { s: 'bank', icon: <Landmark size={28} />, title: 'Z transakcji bankowej', text: 'Wybierz transakcję z połączonego konta i uzupełnij pozostałe dane.' },
    { s: 'manual', icon: <Pencil size={28} />, title: 'Ręcznie', text: 'Wprowadź wszystkie dane wydatku samodzielnie.' },
    { s: 'receipt', icon: <ReceiptText size={28} />, title: 'Z paragonu', text: 'Wgraj paragon, odczytaj dane i sprawdź je przed zapisaniem.' },
  ]
  return (
    <div className="mx-auto max-w-4xl space-y-8">
      <div><BackLink onClick={onCancel}>Anuluj</BackLink><PageTitle title="Dodaj wydatek" sub="Wybierz, jak chcesz dodać wydatek." /></div>
      <div className="grid gap-5 md:grid-cols-3">
        {tiles.map((t) => (
          <button key={t.s} onClick={() => onPick(t.s)} className="group flex flex-col items-start rounded-2xl border border-line bg-white p-7 text-left transition hover:-translate-y-0.5 hover:border-personal hover:bg-mint-soft/30 focus-visible:border-personal">
            <span className="mb-5 flex h-14 w-14 items-center justify-center rounded-2xl bg-mint-soft text-personal transition group-hover:bg-white">{t.icon}</span>
            <span className="font-display text-xl font-bold">{t.title}</span>
            <span className="mt-2 text-sm text-ink2">{t.text}</span>
          </button>
        ))}
      </div>
    </div>
  )
}

/* ---------- Flow 1: pick an available bank transaction ---------- */
function BankStep({ onBack, onSelect }: { onBack: () => void; onSelect: (tx: Tx) => void }) {
  const ex = useExpenses()
  const { accounts } = useApp()
  const [f, setF] = useState<BankFilter>({ accountId: '', dateFrom: '', dateTo: '', minAmount: '', maxAmount: '', description: '' })
  const [sel, setSel] = useState<string | null>(null)
  const set = (p: Partial<BankFilter>) => setF((x) => ({ ...x, ...p }))
  // Text and amount filters are applied after a short pause so that typing does not fire a request per keystroke.
  const [applied, setApplied] = useState(f)
  useEffect(() => { const t = setTimeout(() => setApplied(f), 350); return () => clearTimeout(t) }, [f])
  const q = useRemote(() => ex.availableBankTransactions(applied), [applied])
  const res = q.data
  const list = res?.ok ? res.data : []
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div><BackLink onClick={onBack}>Wybór źródła</BackLink><PageTitle title="Wybierz transakcję bankową" sub="Pokazujemy Twoje wychodzące, zaksięgowane transakcje w PLN, które nie są jeszcze przypisane do wydatku." /></div>
      <Card className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <Field label="Konto bankowe"><select value={f.accountId} onChange={(e) => set({ accountId: e.target.value })} className={inputCls}><option value="">Wszystkie</option>{accounts.filter((a) => a.currencyCode === 'PLN' && !a.isClosed).map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select></Field>
        <Field label="Data od"><input type="date" value={f.dateFrom} onChange={(e) => set({ dateFrom: e.target.value })} className={inputCls} /></Field>
        <Field label="Data do"><input type="date" value={f.dateTo} onChange={(e) => set({ dateTo: e.target.value })} className={inputCls} /></Field>
        <Field label="Kwota od (zł)"><input inputMode="decimal" value={f.minAmount} onChange={(e) => set({ minAmount: e.target.value })} className={inputCls} /></Field>
        <Field label="Kwota do (zł)"><input inputMode="decimal" value={f.maxAmount} onChange={(e) => set({ maxAmount: e.target.value })} className={inputCls} /></Field>
        <Field label="Opis"><input value={f.description} onChange={(e) => set({ description: e.target.value })} className={inputCls} placeholder="np. Orlen" /></Field>
      </Card>
      <Card className="!p-2">
        {!res ? <div className="p-8 text-center text-ink2">{q.error !== null ? <ExpErrorBox code={q.error} /> : 'Wczytywanie transakcji…'}</div> : !res.ok ? <div className="p-4"><ExpErrorBox code={res.code} /></div> : list.length === 0 ? <p className="p-8 text-center text-ink2">Brak dostępnych transakcji dla tych filtrów.</p> : (
          <div role="radiogroup" aria-label="Dostępne transakcje bankowe" className="max-h-[420px] divide-y divide-line overflow-y-auto">
            {list.map((t) => (
              <button key={t.id} role="radio" aria-checked={sel === t.id} onClick={() => setSel(t.id)} className={cx('flex w-full items-center gap-4 rounded-xl px-3 py-3 text-left transition', sel === t.id ? 'bg-mint-soft/50' : 'hover:bg-wash')}>
                <span className={cx('flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2', sel === t.id ? 'border-personal' : 'border-line')}>{sel === t.id && <span className="h-2.5 w-2.5 rounded-full bg-personal" />}</span>
                <span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{t.description ?? 'Bez opisu'}</span><span className="block text-xs text-ink2">{dayText(t.date)} · {accounts.find((a) => a.id === t.bankAccountId)?.name}</span></span>
                <Badge>{t.currencyCode}</Badge>
                <span className="num w-28 shrink-0 text-right font-semibold">{money(Math.abs(Number(t.amount)))}</span>
              </button>
            ))}
          </div>
        )}
      </Card>
      <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={onBack}>Wstecz</Btn><Btn disabled={!sel} onClick={() => { const t = list.find((x) => x.id === sel); if (t) onSelect(t) }}>Wybierz transakcję</Btn></div>
    </div>
  )
}

/* ---------- Flow 3a: upload a single receipt, then analyse explicitly ---------- */
function UploadStep({ receiptId, onUploaded, onRemove, onBack, onAnalyzed, onSkip }: {
  receiptId: string | null; onUploaded: (id: string) => void; onRemove: () => void; onBack: () => void; onAnalyzed: (a: OcrAnalysis) => void; onSkip: () => void
}) {
  const ex = useExpenses()
  const svc = useExpenseService()
  const input = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState<'upload' | 'ocr' | null>(null)
  const [err, setErr] = useState<number | null>(null)
  const receipt = receiptId ? ex.getReceipt(receiptId) : undefined

  const upload = (f?: File) => {
    if (!f) return
    setErr(null); setBusy('upload')
    svc.uploadReceipt(f).then((r) => {
      setBusy(null)
      if (input.current) input.current.value = ''
      if (!r.ok) return setErr(r.code)
      if (receiptId) svc.deleteReceipt(receiptId)
      onUploaded(r.data.id)
    })
  }
  const analyze = async () => {
    if (!receiptId) return
    setErr(null); setBusy('ocr')
    const r = await svc.analyzeReceipt(receiptId)
    setBusy(null)
    if (!r.ok) return setErr(r.code)
    onAnalyzed(r.data)
  }
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div><BackLink onClick={onBack}>Wybór źródła</BackLink><PageTitle title="Wgraj paragon" sub="Wgraj jeden plik. Wgranie nie uruchamia jeszcze odczytu danych." /></div>
      <input ref={input} type="file" className="hidden" accept={['image/*', 'application/pdf', ...RECEIPT_EXT.map((e) => `.${e}`)].join(',')} onChange={(e) => upload(e.target.files?.[0])} aria-label="Wybierz plik paragonu" />
      {!receipt ? (
        <button onClick={() => input.current?.click()} disabled={busy !== null} className="flex w-full flex-col items-center gap-3 rounded-2xl border-2 border-dashed border-line bg-white px-6 py-14 text-center transition hover:border-personal hover:bg-mint-soft/30 disabled:opacity-60">
          {busy === 'upload' ? <Loader2 size={32} className="animate-spin text-personal" /> : <Upload size={32} className="text-personal" />}
          <span className="font-display text-lg font-bold">Wgraj paragon</span>
          <span className="text-sm text-ink2">Kliknij, aby wybrać plik: JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP lub PDF.</span>
          <span className="text-xs text-ink2">Maksymalny rozmiar: {MAX_RECEIPT_BYTES / 1024 / 1024} MiB · dokładnie jeden plik</span>
        </button>
      ) : (
        <Card className="space-y-4">
          <div className="flex flex-wrap items-start gap-5">
            <div className="w-40"><ReceiptThumb r={receipt} size="lg" /></div>
            <div className="min-w-0 flex-1 space-y-2 text-sm">
              <p className="font-semibold">{receipt.fileName}</p>
              <p className="text-ink2">{receipt.size != null ? `${(receipt.size / 1024).toFixed(0)} KiB` : ''}</p>
              <p className="text-ink2">Paragon został wgrany. Dane nie zostały jeszcze odczytane – uruchom analizę.</p>
              <div className="flex flex-wrap gap-2">
                <Btn variant="ghost" className="!min-h-8 !px-3 !py-1" onClick={() => input.current?.click()} disabled={busy !== null}>Zamień plik</Btn>
                <Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => { onRemove(); setErr(null) }} disabled={busy !== null}><Trash2 size={14} />Usuń</Btn>
              </div>
            </div>
          </div>
          <ExpErrorBox code={err} />
          <div className="flex flex-wrap justify-end gap-2">
            <Btn variant="ghost" onClick={onSkip} disabled={busy !== null}>Pomiń analizę i uzupełnij ręcznie</Btn>
            <Btn onClick={analyze} disabled={busy !== null}>{busy === 'ocr' ? <Loader2 size={16} className="animate-spin" /> : <ScanLine size={16} />}Analizuj paragon</Btn>
          </div>
        </Card>
      )}
      {!receipt && err && <ExpErrorBox code={err} />}
    </div>
  )
}

/* ---------- Flow 3b: read-only OCR result (no correction here; edits happen in the expense form) ---------- */
function OcrResultStep({ analysis, receiptId, onBack, onContinue }: { analysis: OcrAnalysis; receiptId: string; onBack: () => void; onContinue: () => void }) {
  const ex = useExpenses()
  const receipt = ex.getReceipt(receiptId)
  const missing = <Badge tone="warn">Nie wykryto</Badge>
  const val = (v: ReactNode | undefined | null, ok = v !== undefined && v !== null && v !== '') => (ok ? <span className="font-semibold">{v}</span> : missing)
  const rows: [string, ReactNode][] = [
    ['Nazwa wydatku', val(analysis.name)],
    ['Sprzedawca', val(analysis.merchant)],
    ['Kwota', analysis.amount != null ? <span className="flex items-center gap-2 font-semibold">{money(analysis.amount)}{analysis.needsReview && <Badge tone="info">Wymaga weryfikacji</Badge>}</span> : missing],
    ['Data transakcji', val(analysis.transactionDate ? dayText(analysis.transactionDate) : undefined)],
  ]
  const noCat = analysis.items.filter((i) => !i.categoryId).length
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div><BackLink onClick={onBack}>Wgrany paragon</BackLink><PageTitle title="Wynik analizy paragonu" sub="To tylko podsumowanie odczytu. Dane poprawisz w formularzu wydatku." /></div>
      {analysis.needsReview && <p className="rounded-xl bg-warn-soft p-3 text-sm text-warn">Część danych wymaga weryfikacji – sprawdź je w formularzu wydatku.</p>}
      {analysis.warnings.length > 0 && (
        <div className="rounded-xl bg-wash p-3 text-sm"><p className="mb-1 font-semibold">Ostrzeżenia z analizy</p><ul className="list-disc space-y-1 pl-5 text-ink2">{analysis.warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>
      )}
      <div className="grid gap-6 md:grid-cols-[200px_1fr]">
        {receipt && <ReceiptThumb r={receipt} size="lg" />}
        <Card>
          <H>Odczytane dane</H>
          <dl className="divide-y divide-line">
            {rows.map(([k, v]) => <div key={k} className="flex flex-wrap items-center justify-between gap-2 py-3 text-sm"><dt className="text-ink2">{k}</dt><dd>{v}</dd></div>)}
          </dl>
        </Card>
      </div>
      <Card className="!p-2">
        <div className="px-3 pt-3"><H>Odczytane produkty</H></div>
        {analysis.items.length === 0 ? <p className="p-6 text-center text-sm text-ink2">Nie wykryto produktów.</p> : (
          <div className="soft-scroll overflow-x-auto">
            <table className="w-full min-w-[520px] text-left text-sm">
              <thead><tr className="border-b border-line text-xs uppercase tracking-wide text-ink2"><th className="px-3 py-2">Produkt</th><th className="px-3 py-2">Proponowana kategoria</th><th className="px-3 py-2 text-right">Cena</th><th className="px-3 py-2 text-right">Ilość</th><th className="px-3 py-2 text-right">Rabat</th></tr></thead>
              <tbody className="divide-y divide-line">
                {analysis.items.map((i, n) => (
                  <tr key={n}>
                    <td className="px-3 py-2.5 font-medium">{i.name}</td>
                    <td className="px-3 py-2.5">{i.categoryId ? <CatBadge id={i.categoryId} compact /> : <Badge tone="warn">Nie rozpoznano kategorii</Badge>}</td>
                    <td className="num px-3 py-2.5 text-right">{i.unitPrice != null ? money(i.unitPrice) : <Badge tone="warn">Brak ceny</Badge>}</td>
                    <td className="num px-3 py-2.5 text-right">{i.quantity ?? 1}</td>
                    <td className="num px-3 py-2.5 text-right">{i.discount ? money(i.discount) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {noCat > 0 && <p className="px-3 pb-3 text-xs text-ink2">Kategorie nierozpoznanych produktów wybierzesz w formularzu wydatku.</p>}
      </Card>
      <div className="flex flex-wrap justify-end gap-2"><Btn variant="ghost" onClick={onBack}>Wstecz</Btn><Btn onClick={onContinue}>Przejdź do formularza wydatku</Btn></div>
    </div>
  )
}

/* ---------- The flow ---------- */
export default function AddExpenseFlow() {
  const { goBack } = useApp()
  const svc = useExpenseService()
  const [step, setStep] = useState<Step>('source')
  const [source, setSource] = useState<Source | null>(null)
  const [bankTx, setBankTx] = useState<Tx | null>(null)
  const [receiptId, setReceiptId] = useState<string | null>(null)
  const [analysis, setAnalysis] = useState<OcrAnalysis | null>(null)
  const [reviewed, setReviewed] = useState<Form | null>(null)
  const saved = useRef(false)
  const temp = useRef<string | null>(null)
  temp.current = receiptId

  // Leaving without saving releases the selected local receipt file.
  useEffect(() => () => { if (!saved.current && temp.current) svc.deleteReceipt(temp.current) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const dropReceipt = () => { if (receiptId) svc.deleteReceipt(receiptId); setReceiptId(null); setAnalysis(null); setReviewed(null) }
  const toSource = () => { dropReceipt(); setSource(null); setBankTx(null); setStep('source') }

  if (step === 'source') return <SourceStep onCancel={goBack} onPick={(s) => { setSource(s); setStep(s === 'bank' ? 'bank' : s === 'manual' ? 'form' : 'upload') }} />
  if (step === 'bank') return <BankStep onBack={toSource} onSelect={(t) => { setBankTx(t); setStep('form') }} />
  if (step === 'upload') {
    return <UploadStep receiptId={receiptId} onUploaded={(id) => { setReceiptId(id); setAnalysis(null) }} onRemove={dropReceipt} onBack={toSource}
      onAnalyzed={(a) => { setAnalysis(a); setStep('ocr') }} onSkip={() => { setReviewed(null); setStep('form') }} />
  }
  if (step === 'ocr' && analysis && receiptId) {
    // Prefill the common form with every usable OCR value; undetected values stay empty for the user to fill in.
    const toForm = () => {
      const blank: Form = { groupId: '', payerId: ME_ID, name: '', merchant: '', amount: '', date: '', time: '', items: [], shares: [], bankTransactionId: null, receiptId }
      const f = applyOcr(blank, analysis, 'overwrite', false).next
      setReviewed({ ...f, items: f.items.length ? f.items : [emptyItem()] }); setStep('form')
    }
    return <OcrResultStep analysis={analysis} receiptId={receiptId} onBack={() => setStep('upload')} onContinue={toForm} />
  }

  // Common form, initialised differently per source.
  const tx = bankTx ?? undefined
  const initial: FormInit =
    source === 'bank' && bankTx ? { sourceLabel: 'Źródło: transakcja bankowa', tx: bankTx, form: { bankTransactionId: bankTx.id, amount: '', payerId: ME_ID, date: tx?.date ?? '', time: '' } }
    : source === 'receipt' ? { sourceLabel: 'Źródło: paragon', receiptId, ocrWarnings: reviewed && analysis ? { warnings: analysis.warnings, needsReview: analysis.needsReview } : undefined, form: reviewed ? { ...reviewed, receiptId } : { receiptId } }
    : { sourceLabel: 'Wprowadzone ręcznie' }
  const back = source === 'bank' ? () => setStep('bank') : source === 'receipt' ? () => setStep(reviewed && analysis ? 'ocr' : 'upload') : () => toSource()
  const backLabel = source === 'bank' ? 'Wybór transakcji' : source === 'receipt' ? (reviewed ? 'Wynik analizy' : 'Wgrany paragon') : 'Wybór źródła'
  return <ExpenseForm key={`${source}-${bankTx?.id}-${receiptId}-${reviewed ? 'r' : 'n'}`} initial={initial} onBack={back} backLabel={backLabel} onSaved={() => { saved.current = true }} onInitialReceiptDetached={dropReceipt} />
}
