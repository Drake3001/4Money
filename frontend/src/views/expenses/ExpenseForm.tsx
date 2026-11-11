import { useEffect, useMemo, useRef, useState } from 'react'
import { useRemote } from '../../api/hooks'
import { ArrowLeft, Landmark, Loader2, Plus, ScanLine, Trash2, Upload } from 'lucide-react'
import { useApp } from '../../context'
import { useExpenses } from '../../expensesContext'
import { useExpenseService } from '../../services/expenseService'
import { useGroups } from '../../groupsContext'
import {
  ME_ID, MAX_RECEIPT_BYTES, RECEIPT_EXT, SELECTABLE_CATEGORIES, computeExpense, emptyItem, equalShares, fmtDay, fmtPct, formFromExpense,
  isNum, itemTotal, num, userLabel, r2, shareAmounts, toLocalParts, type ExpenseDetail, type ExpenseForm as Form, type OcrAnalysis, type Preview,
} from '../../mocks/expenses'
import { dayText, money, type Tx } from '../../mocks/bank'
import { Badge, Btn, Card, Field, H, PageTitle, Table, Td, cx, inputCls } from '../../ui'
import BankPicker from './BankPicker'
import OcrPanel from './OcrPanel'
import { CatBadge, ExpErrorBox, ReceiptThumb } from './ExpenseUi'

const nowParts = () => toLocalParts(new Date().toISOString())

export type FormInit = { form?: Partial<Form>; receiptId?: string | null; tx?: Tx; sourceLabel?: string; ocrWarnings?: { warnings: string[]; needsReview: boolean } }

// One common form for editing and for all three "Dodaj wydatek" sources (manual / bank transaction / receipt).
// `initial` only changes how the form starts; the data model and the preview/save flow are identical.
type Props = { id?: string; initial?: FormInit; onBack?: () => void; backLabel?: string; onSaved?: () => void; onInitialReceiptDetached?: () => void }

// When editing, the expense is loaded first (GET /api/expenses/{expenseId}); the form itself always starts from complete data.
export default function ExpenseForm(props: Props) {
  const { id } = props
  const { navigate } = useApp()
  const svc = useExpenseService()
  const q = useRemote(() => svc.getExpense(id!), [id], !!id)
  if (!id) return <FormBody {...props} />
  if (q.data?.ok) return <FormBody {...props} existing={q.data.data} />
  const code = q.error ?? (q.data && !q.data.ok ? q.data.code : null)
  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <PageTitle title="Edycja wydatku" />
      {code === null ? <p className="text-ink2">Wczytywanie wydatku…</p> : <ExpErrorBox code={code} />}
      <Btn variant="ghost" onClick={() => navigate({ name: 'expenses' })}>Wróć do wydatków</Btn>
    </div>
  )
}

function FormBody({ existing, initial, onBack, backLabel, onSaved, onInitialReceiptDetached }: Props & { existing?: ExpenseDetail }) {
  const { navigate, showToast, goBack, accounts } = useApp()
  const ex = useExpenses() // receipts seen in this session
  const svc = useExpenseService()
  const groups = useGroups()
  const id = existing?.id
  const groupsQ = useRemote(() => groups.fetchList('active'), [groups.rev])
  const myGroups = (groupsQ.data ?? []).map((x) => x.group)
  const groupName = (gid: string) => existing?.groupName ?? myGroups.find((g) => g.id === gid)?.name ?? ''

  const [form, setForm] = useState<Form>(() => {
    if (existing) return formFromExpense(existing)
    // Group, participants, name, merchant, products and the exact time are never pre-filled from a source: the user provides them.
    const p = nowParts()
    return { groupId: '', payerId: ME_ID, name: '', merchant: '', amount: '', date: p.date, time: p.time, items: [emptyItem()], shares: [], bankTransactionId: null, receiptId: initial?.receiptId ?? null, ...initial?.form }
  })
  const membersQ = useRemote(() => groups.fetchActiveMembers(form.groupId), [form.groupId, groups.rev], !!form.groupId)
  const mem = useMemo(() => (form.groupId ? membersQ.data ?? [] : []), [membersQ.data, form.groupId])
  // Bank transactions are shown from the loaded object (picked in this session, or fetched by id).
  const [txs, setTxs] = useState<Record<string, Tx>>(() => (initial?.tx ? { [initial.tx.id]: initial.tx } : {}))
  const tx = form.bankTransactionId ? txs[form.bankTransactionId] : undefined
  useEffect(() => {
    const tid = form.bankTransactionId
    if (!tid || txs[tid]) return
    void svc.getBankTransaction(tid).then((r) => { if (r.ok) setTxs((t) => ({ ...t, [tid]: r.data })) })
  }, [form.bankTransactionId]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (existing?.receiptId) void svc.getReceipt(existing.id) }, []) // eslint-disable-line react-hooks/exhaustive-deps
  // The concurrency token is internal state and is never displayed.
  const [version, setVersion] = useState(existing?.version ?? '')
  const [step, setStep] = useState<'edit' | 'preview'>('edit')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [submitted, setSubmitted] = useState(false)
  const [apiErr, setApiErr] = useState<number | null>(null)
  const [conflict, setConflict] = useState(false)
  const [bankOpen, setBankOpen] = useState(false)
  const [busy, setBusy] = useState<'upload' | 'ocr' | 'save' | null>(null)
  const [recErr, setRecErr] = useState<number | null>(null)
  const [ocrOpen, setOcrOpen] = useState(false)
  const uploaded = useRef<string[]>([])
  const saved = useRef(false)
  const file = useRef<HTMLInputElement>(null)

  // Leaving without saving releases local receipt files and preview URLs.
  useEffect(() => () => { if (!saved.current) uploaded.current.forEach((r) => svc.deleteReceipt(r)) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const set = (p: Partial<Form>) => setForm((f) => ({ ...f, ...p }))
  const payerChoices = useMemo(() => (existing && !mem.includes(existing.payerId) ? [existing.payerId, ...mem] : mem), [existing, mem])
  const { problems } = computeExpense(form, { activePayers: existing && form.payerId === existing.payerId ? [...mem, existing.payerId] : mem, bankAmount: tx ? Number(tx.amount) : null })
  const show = (k: string) => (submitted ? problems.fields[k] : undefined)
  const receipt = form.receiptId ? ex.getReceipt(form.receiptId) : undefined

  const known = r2(form.items.reduce((s, it) => (isNum(it.unitPrice) && isNum(it.quantity) ? s + itemTotal({ unitPrice: num(it.unitPrice), quantity: num(it.quantity), discount: it.discount.trim() && isNum(it.discount) ? num(it.discount) : 0 }) : s), 0))
  const est = tx ? Math.abs(Number(tx.amount)) : isNum(form.amount) ? num(form.amount) : known
  const sharePct = r2(form.shares.reduce((s, x) => s + (isNum(x.percentage) ? num(x.percentage) : 0), 0))

  if (existing && !existing.canEdit) return <div className="mx-auto max-w-2xl space-y-4"><PageTitle title="Edycja wydatku" /><ExpErrorBox code={403} /><Btn variant="ghost" onClick={() => navigate({ name: 'expense', id })}>Wróć do wydatku</Btn></div>

  const back = () => (id ? navigate({ name: 'expense', id }) : goBack())
  const stepBack = onBack ?? back

  const changeGroup = async (gid: string) => {
    set({ groupId: gid, payerId: tx ? form.payerId : '', shares: [] })
    if (!gid) return
    let m: string[] = []
    try { m = await groups.fetchActiveMembers(gid) } catch { /* the member list shows the error; the form stays empty */ }
    setForm((f) => (f.groupId !== gid ? f : { ...f, payerId: tx ? f.payerId : m.includes(ME_ID) ? ME_ID : m[0] ?? '', shares: equalShares(m).map((s) => ({ userId: s.userId, percentage: String(s.percentage) })) }))
  }

  const pickBank = (picked: Tx) => {
    // The amount and payer become constrained by the bank source (the payer is the current user, owner of the transaction).
    // Name, merchant, time and products stay untouched.
    setTxs((t) => ({ ...t, [picked.id]: picked })); set({ bankTransactionId: picked.id, amount: '', payerId: ME_ID })
    setBankOpen(false)
  }

  const upload = (f?: File) => {
    if (!f) return
    setRecErr(null); setBusy('upload')
    svc.uploadReceipt(f).then((r) => {
      setBusy(null)
      if (file.current) file.current.value = ''
      if (!r.ok) return setRecErr(r.code)
      if (form.receiptId && uploaded.current.includes(form.receiptId)) { svc.deleteReceipt(form.receiptId); uploaded.current = uploaded.current.filter((x) => x !== form.receiptId) }
      uploaded.current.push(r.data.id)
      set({ receiptId: r.data.id }); setOcrOpen(false)
    })
  }
  const detach = () => {
    if (form.receiptId && form.receiptId === initial?.receiptId) onInitialReceiptDetached?.()
    if (form.receiptId && uploaded.current.includes(form.receiptId)) { svc.deleteReceipt(form.receiptId); uploaded.current = uploaded.current.filter((x) => x !== form.receiptId) }
    set({ receiptId: null }); setOcrOpen(false); setRecErr(null)
  }
  const analyze = async () => {
    if (!form.receiptId) return
    setRecErr(null); setBusy('ocr')
    const r = await svc.analyzeReceipt(form.receiptId)
    setBusy(null)
    if (!r.ok) return setRecErr(r.code)
    setOcrOpen(true)
  }

  // A bank-backed expense must send the debit's absolute amount (the form hides the amount field in that case).
  const payload = (): Form => (tx ? { ...form, amount: String(Math.abs(Number(tx.amount))) } : form)
  const goPreview = async () => {
    setSubmitted(true); setApiErr(null)
    if (problems.list.length) return
    setBusy('save')
    const r = await svc.previewExpense(payload(), existing?.id, existing ? version : undefined)
    setBusy(null)
    if (!r.ok) return setApiErr(r.code)
    setPreview(r.data); setStep('preview'); window.scrollTo(0, 0)
  }
  const save = async () => {
    setApiErr(null); setBusy('save')
    const r = existing ? await svc.updateExpense(existing.id, payload(), version) : await svc.createExpense(payload())
    setBusy(null)
    if (!r.ok) { if (r.code === 409 && existing) setConflict(true); return setApiErr(r.code) }
    saved.current = true
    onSaved?.()
    showToast(existing ? 'Zapisano zmiany w wydatku' : 'Wydatek został zapisany')
    navigate({ name: 'expense', id: r.data.id })
  }
  const reloadCurrent = async () => { if (!existing) return; const r = await svc.getExpense(existing.id); if (!r.ok) return setApiErr(r.code); setForm(formFromExpense(r.data)); setVersion(r.data.version); setConflict(false); setApiErr(null); setStep('edit') }
  const keepMine = async () => { if (!existing) return; const r = await svc.getExpense(existing.id); if (!r.ok) return setApiErr(r.code); setVersion(r.data.version); setConflict(false); setApiErr(null); setStep('edit'); showToast('Pobrano aktualną wersję. Sprawdź swoje zmiany i zapisz ponownie.') }

  const setItem = (iid: string, p: Partial<Form['items'][number]>) => set({ items: form.items.map((i) => (i.id === iid ? { ...i, ...p } : i)) })
  const setShare = (uid: string, v: string) => set({ shares: form.shares.map((s) => (s.userId === uid ? { ...s, percentage: v } : s)) })
  const toggleShare = (uid: string) => set({ shares: form.shares.some((s) => s.userId === uid) ? form.shares.filter((s) => s.userId !== uid) : [...form.shares, { userId: uid, percentage: '' }] })
  const splitEqual = () => set({ shares: equalShares(form.shares.map((s) => s.userId)).map((s) => ({ userId: s.userId, percentage: String(s.percentage) })) })
  const shareUsers = [...new Set([...mem, ...form.shares.map((s) => s.userId)])]

  const title = existing ? 'Edytuj wydatek' : 'Dodaj wydatek'

  /* ---------- Preview step ---------- */
  if (step === 'preview' && preview) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <div><button onClick={() => setStep('edit')} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />Wróć do edycji</button><PageTitle title="Podgląd wydatku" sub="Wynik wyliczenia wydatku. Nic nie zostało jeszcze zapisane." /></div>
        <Card>
          <p className="text-sm text-ink2">Kwota końcowa (wynik podglądu)</p>
          <p className="num font-display text-3xl font-bold">{money(preview.totalAmount)} <span className="text-base font-medium text-ink2">PLN</span></p>
          <p className="mt-2 text-sm text-ink2">{form.name.trim()} · {groupName(form.groupId)} · płaci {userLabel(form.payerId)} · {fmtDay(form.date)}</p>
        </Card>
        <Card className="!p-2">
          <div className="px-3 pt-3"><H>Produkty po obliczeniach</H></div>
          <Table head={['Produkt', 'Kategoria', 'Cena jedn.', 'Ilość', 'Rabat', 'Razem']}>
            {preview.items.map((i) => (
              <tr key={i.id} className={cx(i.isSystemGenerated && 'bg-wash')}>
                <Td className="font-medium">{i.name}{i.isSystemGenerated && <span className="ml-2 text-xs text-ink2">(dodane automatycznie, nie można edytować)</span>}</Td>
                <Td><CatBadge id={i.categoryId} compact /></Td><Td className="num">{money(i.unitPrice)}</Td><Td className="num">{String(i.quantity).replace('.', ',')}</Td>
                <Td className="num">{i.discount ? money(i.discount) : '—'}</Td><Td className="num font-semibold">{money(i.total)}</Td>
              </tr>
            ))}
          </Table>
          {preview.unknownAmount > 0 && <p className="px-3 py-3 text-sm text-ink2">Kwota wydatku jest większa niż suma produktów, więc różnica {money(preview.unknownAmount)} trafia do pozycji „Nieznane”.</p>}
        </Card>
        <Card className="!p-2">
          <div className="px-3 pt-3"><H>Uczestnicy</H></div>
          <Table head={['Uczestnik', 'Udział', 'Kwota']}>{preview.shares.map((s) => <tr key={s.userId}><Td className="font-medium">{userLabel(s.userId)}</Td><Td className="num">{fmtPct(s.percentage)}</Td><Td className="num font-semibold">{money(s.amount)}</Td></tr>)}</Table>
        </Card>
        <Card>
          <H>Źródła</H>
          <p className="text-sm text-ink2">Transakcja bankowa: {tx ? `${tx.description ?? 'Bez opisu'} (${money(Math.abs(Number(tx.amount)))})` : 'brak'}</p>
          <p className="text-sm text-ink2">Paragon: {receipt ? receipt.fileName : 'brak'}</p>
        </Card>
        <ExpErrorBox code={apiErr} />
        {conflict && <ConflictBox onReload={reloadCurrent} onKeep={keepMine} />}
        <div className="flex flex-wrap justify-end gap-2"><Btn variant="ghost" onClick={() => setStep('edit')}>Wróć do edycji</Btn><Btn onClick={save} disabled={busy === 'save'}>{existing ? 'Zapisz zmiany' : 'Zapisz wydatek'}</Btn></div>
      </div>
    )
  }

  /* ---------- Edit step ---------- */
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div><button onClick={stepBack} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />{onBack ? backLabel ?? 'Wstecz' : existing ? 'Wydatek' : 'Wydatki'}</button><PageTitle title={title} sub={initial?.sourceLabel ? `${initial.sourceLabel} · wszystkie kwoty w PLN` : 'Wszystkie kwoty w PLN.'} /></div>

      {initial?.ocrWarnings && (
        <details className="rounded-xl bg-wash p-3 text-sm">
          <summary className="cursor-pointer font-semibold">Dane wstępnie wypełnione z paragonu – sprawdź je{initial.ocrWarnings.warnings.length > 0 ? ` (ostrzeżenia: ${initial.ocrWarnings.warnings.length})` : initial.ocrWarnings.needsReview ? ' (wymaga weryfikacji)' : ''}</summary>
          <p className="mt-2 text-ink2">Wszystkie pola możesz swobodnie poprawić. Puste pola nie zostały odczytane z paragonu.</p>
          {initial.ocrWarnings.warnings.length > 0 && <ul className="mt-2 list-disc space-y-1 pl-5 text-ink2">{initial.ocrWarnings.warnings.map((w) => <li key={w}>{w}</li>)}</ul>}
        </details>
      )}
      {conflict && <ConflictBox onReload={reloadCurrent} onKeep={keepMine} />}
      {submitted && problems.list.length > 0 && (
        <div role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger"><p className="mb-1 font-semibold">Popraw formularz:</p><ul className="list-disc space-y-0.5 pl-5">{problems.list.slice(0, 6).map((p, i) => <li key={i}>{p}</li>)}</ul></div>
      )}
      {!conflict && <ExpErrorBox code={apiErr} />}

      <Card className="space-y-5">
        <H>Podstawowe informacje</H>
        <div className="grid gap-4 sm:grid-cols-2">
          {existing ? (
            <div><span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">Grupa</span><p className="rounded-xl border border-line bg-wash px-3 py-2.5 text-sm font-semibold">{groupName(form.groupId)}</p><p className="mt-1 text-xs text-ink2">Grupy nie można zmienić po utworzeniu wydatku.</p></div>
          ) : (
            <Field label="Grupa" hint={show('groupId')}><select value={form.groupId} onChange={(e) => changeGroup(e.target.value)} className={inputCls}><option value="">{myGroups.length === 0 ? 'Brak grup' : 'Wybierz grupę…'}</option>{myGroups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</select></Field>
          )}
          <div>
            <Field label="Płatnik"><select value={form.payerId} disabled={!!tx} onChange={(e) => set({ payerId: e.target.value })} className={cx(inputCls, tx && 'opacity-60')}>{payerChoices.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}{form.payerId && !payerChoices.includes(form.payerId) && <option value={form.payerId}>{userLabel(form.payerId)}</option>}</select></Field>
            <p className={cx('mt-1 text-xs', show('payerId') ? 'text-danger' : 'text-ink2')}>{show('payerId') ?? (tx ? 'Płatnikiem jesteś Ty – właściciel powiązanej transakcji bankowej.' : 'Wybierz spośród aktywnych członków grupy (identyfikatory użytkowników).')}</p>
          </div>
          <div className="sm:col-span-2"><Field label="Nazwa wydatku" hint={show('name')}><input value={form.name} onChange={(e) => set({ name: e.target.value })} className={cx(inputCls, show('name') && '!border-danger')} placeholder="np. Zakupy tygodniowe" /></Field></div>
          <Field label="Sprzedawca (opcjonalnie)"><input value={form.merchant} onChange={(e) => set({ merchant: e.target.value })} className={inputCls} /></Field>
          <div>
            <Field label="Kwota całkowita (opcjonalnie)"><input inputMode="decimal" value={tx ? String(Math.abs(Number(tx.amount))).replace('.', ',') : form.amount} disabled={!!tx} onChange={(e) => set({ amount: e.target.value })} className={cx(inputCls, tx && 'opacity-60', show('amount') && '!border-danger')} placeholder={known > 0 ? `${String(known).replace('.', ',')} (suma produktów)` : '0,00'} /></Field>
            <p className={cx('mt-1 text-xs', show('amount') ? 'text-danger' : 'text-ink2')}>{show('amount') ?? (tx ? 'Kwota wynika z powiązanej transakcji bankowej.' : 'Puste pole: kwota zostanie policzona z produktów.')}</p>
          </div>
          <Field label="Data transakcji" hint={show('date')}><input type="date" value={form.date} onChange={(e) => set({ date: e.target.value })} className={cx(inputCls, show('date') && '!border-danger')} /></Field>
        </div>
        <div className="text-sm"><span className="font-semibold">Waluta:</span> PLN <span className="text-ink2">(stała, nie można zmienić)</span></div>
      </Card>

      <Card className="space-y-4">
        <H>Źródła</H>
        <div className="rounded-xl border border-line p-4">
          <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><Landmark size={16} />Transakcja bankowa (opcjonalnie)</p>
          {tx ? (
            <div className="space-y-2 text-sm">
              <p className="font-medium">{tx.description ?? 'Bez opisu'}</p>
              <p className="text-ink2">{accounts.find((a) => a.id === tx.bankAccountId)?.name} · {dayText(tx.date)} · <span className="num font-semibold text-ink">{money(Math.abs(Number(tx.amount)))}</span></p>
              <p className="text-xs text-ink2">Nazwa, sprzedawca, czas i produkty pozostają pod Twoją kontrolą.</p>
              <div className="flex gap-2"><Btn variant="ghost" className="!min-h-8 !px-3 !py-1" onClick={() => setBankOpen(true)}>Zmień</Btn><Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => set({ bankTransactionId: null })}>Odłącz transakcję</Btn></div>
            </div>
          ) : <Btn variant="ghost" onClick={() => setBankOpen(true)}><Plus size={16} />Dołącz transakcję bankową</Btn>}
        </div>

        <div className="rounded-xl border border-line p-4">
          <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><ScanLine size={16} />Paragon (opcjonalnie)</p>
          <input ref={file} type="file" className="hidden" accept={['image/*', 'application/pdf', ...RECEIPT_EXT.map((e) => `.${e}`)].join(',')} onChange={(e) => upload(e.target.files?.[0])} aria-label="Wybierz plik paragonu" />
          {receipt ? (
            <div className="flex flex-wrap items-start gap-4">
              <ReceiptThumb r={receipt} />
              <div className="min-w-0 flex-1 space-y-2 text-sm">
                <p className="font-medium">{receipt.fileName}</p>
                <p className="text-xs text-ink2">Wgranie pliku nie uruchamia OCR. Analizę zlecasz osobno.</p>
                <div className="flex flex-wrap gap-2">
                  {receipt.isLocal && <Btn onClick={analyze} disabled={busy !== null}>{busy === 'ocr' ? <Loader2 size={16} className="animate-spin" /> : <ScanLine size={16} />}Analizuj paragon</Btn>}
                  {receipt.latestAnalysis && !ocrOpen && <Btn variant="ghost" onClick={() => setOcrOpen(true)}>Pokaż wynik analizy</Btn>}
                  <Btn variant="ghost" onClick={() => file.current?.click()} disabled={busy !== null}>Zamień plik</Btn>
                  <Btn variant="soft" onClick={detach}>Odłącz paragon</Btn>
                </div>
              </div>
            </div>
          ) : (
            <div className="space-y-2">
              <Btn variant="ghost" onClick={() => file.current?.click()} disabled={busy !== null}>{busy === 'upload' ? <Loader2 size={16} className="animate-spin" /> : <Upload size={16} />}Wgraj paragon</Btn>
              <p className="text-xs text-ink2">Jeden plik: JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP lub PDF, maksymalnie {MAX_RECEIPT_BYTES / 1024 / 1024} MiB. Wgranie nie uruchamia analizy OCR.</p>
            </div>
          )}
          {recErr && <div className="mt-3"><ExpErrorBox code={recErr} /></div>}
        </div>
      </Card>

      {ocrOpen && receipt && <OcrPanel receipt={ex.getReceipt(receipt.id) ?? receipt} form={form} hasBank={!!tx} onApply={(f) => { setForm(f); showToast('Dane z OCR wczytane do formularza – sprawdź je przed zapisem') }} onClose={() => setOcrOpen(false)} />}

      <Card className="space-y-4">
        <H aside={<Btn variant="ghost" className="!min-h-9" onClick={() => set({ items: [...form.items, emptyItem()] })}><Plus size={16} />Dodaj produkt</Btn>}>Produkty</H>
        <p className="text-xs text-ink2">Rabat dotyczy całej pozycji i zwykle jest zerem lub wartością ujemną (np. -2,50). Ilość musi być dodatnia.</p>
        {form.items.length === 0 && <p className="rounded-xl bg-wash p-3 text-sm text-ink2">Brak produktów. Jeśli podasz kwotę, cała zostanie przypisana do pozycji „Nieznane”.</p>}
        {form.items.map((it, n) => {
          const errs = submitted ? problems.items[it.id] : undefined
          const total = isNum(it.unitPrice) && isNum(it.quantity) ? itemTotal({ unitPrice: num(it.unitPrice), quantity: num(it.quantity), discount: it.discount.trim() && isNum(it.discount) ? num(it.discount) : 0 }) : null
          return (
            <div key={it.id} className="rounded-xl border border-line p-3">
              <div className="grid grid-cols-2 gap-3 md:grid-cols-12">
                <div className="col-span-2 md:col-span-4"><Field label="Nazwa"><input value={it.name} onChange={(e) => setItem(it.id, { name: e.target.value })} className={inputCls} /></Field></div>
                <div className="col-span-2 md:col-span-3"><Field label="Kategoria *" hint={!it.categoryId && it.name.trim() ? 'Wymaga wyboru kategorii.' : undefined}><select value={it.categoryId} onChange={(e) => setItem(it.id, { categoryId: e.target.value })} className={cx(inputCls, !it.categoryId && (it.name.trim() || submitted) && '!border-warn-bar')}><option value="">Wybierz kategorię…</option>{SELECTABLE_CATEGORIES.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field></div>
                <div className="md:col-span-2"><Field label="Cena jedn. (zł)"><input inputMode="decimal" value={it.unitPrice} onChange={(e) => setItem(it.id, { unitPrice: e.target.value })} className={inputCls} /></Field></div>
                <div className="md:col-span-1"><Field label="Ilość"><input inputMode="decimal" value={it.quantity} onChange={(e) => setItem(it.id, { quantity: e.target.value })} className={inputCls} /></Field></div>
                <div className="md:col-span-2"><Field label="Rabat (zł)"><input inputMode="decimal" value={it.discount} onChange={(e) => setItem(it.id, { discount: e.target.value })} className={inputCls} placeholder="0" /></Field></div>
                <div className="md:col-span-2"><Field label="Jednostka"><input value={it.unit} onChange={(e) => setItem(it.id, { unit: e.target.value })} className={inputCls} placeholder="szt., kg…" /></Field></div>
                <div className="col-span-2 flex items-end justify-between gap-3 md:col-span-10">
                  <p className="text-sm text-ink2">Razem: <span className="num font-semibold text-ink">{total !== null ? money(total) : '—'}</span>{isNum(it.discount) && num(it.discount) > 0 && <span className="ml-2 text-xs text-warn">Rabat jest dodatni – zwykle powinien być ujemny.</span>}</p>
                  <button onClick={() => set({ items: form.items.filter((_, i) => i !== n) })} aria-label="Usuń produkt" className="rounded-lg p-2 text-ink2 hover:bg-danger-soft hover:text-danger"><Trash2 size={16} /></button>
                </div>
              </div>
              {errs && <ul className="mt-2 list-disc pl-5 text-xs text-danger">{errs.map((e) => <li key={e}>{e}</li>)}</ul>}
            </div>
          )
        })}
        <div className="rounded-xl bg-wash p-3 text-sm">
          <p className="flex justify-between"><span className="text-ink2">Suma produktów (wstępne wyliczenie)</span><span className="num font-semibold">{money(known)}</span></p>
          {est > known + 0.001 && <p className="mt-1 flex justify-between"><span className="text-ink2">Nieznane (dodawane automatycznie, nie można edytować) – wstępnie</span><span className="num font-semibold">{money(r2(est - known))}</span></p>}
          {known > est + 0.001 && est > 0 && <p className="mt-1 text-danger">Produkty przekraczają kwotę wydatku.</p>}
          <p className="mt-2 text-xs text-ink2">To szybkie wyliczenie pomocnicze. Ostateczne kwoty, pozycję „Nieznane” i zaokrąglenia ustali „Podgląd wydatku”.</p>
        </div>
      </Card>

      <Card className="space-y-4">
        <H aside={<Btn variant="ghost" className="!min-h-9" onClick={splitEqual} disabled={form.shares.length === 0}>Podziel równo</Btn>}>Uczestnicy i udziały</H>
        <p className="text-xs text-ink2">Udziały to procenty i muszą dawać dokładnie 100%. Każdy uczestnik musi mieć udział większy od 0%. Kwoty w złotówkach (≈) są wstępnym wyliczeniem – ostateczne poda podgląd.</p>
        {shareUsers.map((u) => {
          const s = form.shares.find((x) => x.userId === u)
          const pct = s && isNum(s.percentage) ? num(s.percentage) : 0
          return (
            <div key={u} className="flex flex-wrap items-center gap-3 rounded-xl border border-line p-3">
              <label className="flex min-w-[170px] items-center gap-2 text-sm font-semibold"><input type="checkbox" checked={!!s} onChange={() => toggleShare(u)} className="h-4 w-4" />{userLabel(u)}{!mem.includes(u) && <Badge>Były członek</Badge>}</label>
              {s && (
                <>
                  <input type="range" min={0} max={100} step={0.5} value={pct} onChange={(e) => setShare(u, e.target.value)} aria-label={`Udział ${userLabel(u)}`} className="min-w-[120px] flex-1" />
                  <div className="flex items-center gap-1"><input inputMode="decimal" value={s.percentage} onChange={(e) => setShare(u, e.target.value)} className={cx(inputCls, '!w-20 text-right')} aria-label={`Procent ${userLabel(u)}`} /><span className="text-sm">%</span></div>
                  <span className="num w-24 text-right text-sm text-ink2">≈ {money(shareAmounts(est, [{ userId: u, percentage: pct }])[0].amount)}</span>
                </>
              )}
            </div>
          )
        })}
        <p className={cx('text-sm font-semibold', Math.abs(sharePct - 100) < 0.001 ? 'text-safe' : 'text-danger')}>Suma udziałów: {fmtPct(sharePct)} {Math.abs(sharePct - 100) < 0.001 ? '✓' : '(wymagane 100%)'}</p>
        {show('shares') && <p className="text-xs text-danger">{show('shares')}</p>}
      </Card>

      <div className="flex flex-wrap justify-end gap-2">
        <Btn variant="ghost" onClick={back}>Anuluj</Btn>
        <Btn onClick={goPreview} disabled={busy === 'save'}>Podgląd wydatku</Btn>
      </div>
      {bankOpen && <BankPicker onPick={pickBank} onClose={() => setBankOpen(false)} />}
    </div>
  )
}

function ConflictBox({ onReload, onKeep }: { onReload: () => void; onKeep: () => void }) {
  return (
    <div role="alert" className="space-y-3 rounded-xl bg-warn-soft p-4 text-sm text-warn">
      <p className="font-semibold">Ten wydatek został zmieniony w innym miejscu.</p>
      <p>Twoje zmiany w formularzu zostały zachowane i nic nie zostało nadpisane. Odśwież aktualną wersję, aby kontynuować.</p>
      <div className="flex flex-wrap gap-2">
        <Btn variant="ghost" onClick={onKeep}>Pobierz aktualną wersję i zachowaj moje zmiany</Btn>
        <Btn variant="ghost" onClick={onReload}>Odrzuć moje zmiany i wczytaj aktualne dane</Btn>
      </div>
    </div>
  )
}
void ({} as OcrAnalysis)
