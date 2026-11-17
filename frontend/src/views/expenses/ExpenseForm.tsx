import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { useRemote } from '../../api/hooks'
import { ArrowLeft, ChevronDown, Landmark, Loader2, Lock, LockOpen, Plus, ScanLine, Trash2, Upload } from 'lucide-react'
import { useApp } from '../../context'
import { useExpenses } from '../../expensesContext'
import { useExpenseService } from '../../services/expenseService'
import { useGroups } from '../../groupsContext'
import {
  DISCOUNT_CATEGORY_ID, ME_ID, RECEIPT_EXT, bankFormValues, computeExpense, emptyItem, fmtDay, fmtPct, formFromExpense,
  isNum, itemTotal, num, userLabel, r2, toLocalParts, type ExpenseDetail, type ExpenseForm as Form, type OcrAnalysis, type Preview,
} from '../../mocks/expenses'
import { dayText, money, type Tx } from '../../mocks/bank'
import { Badge, Btn, Card, Field as BaseField, H, KeyValue, PageTitle, Segmented, Table, Td, cx, inputCls } from '../../ui'
import BankPicker from './BankPicker'
import OcrPanel from './OcrPanel'
import { CatBadge, CategoryLoadError, ExpErrorBox, ReceiptThumb, TransactionModal } from './ExpenseUi'
import { addShare, balanceShares, changeShare, changeShareAmount, removeShare } from './shareDistribution'

const nowParts = () => toLocalParts(new Date().toISOString())

function Field({ label, error, hint, summaryError = false, children }: { label: string; error?: string; hint?: string; summaryError?: boolean; children: ReactNode }) {
  return (
    <div className={cx(error && '[&_input]:!border-danger [&_select]:!border-danger [&_button]:!border-danger')}>
      <BaseField label={label}>{children}</BaseField>
      <div className="mt-0.5 h-5 overflow-y-auto text-xs leading-tight">
        {!summaryError && (error || hint) && <p role={error ? 'alert' : undefined} className={error ? 'text-danger' : 'text-ink2'}>{error || hint}</p>}
      </div>
    </div>
  )
}

function ShareValueInput({ value, disabled, invalid, label, onChange }: { value: string; disabled: boolean; invalid: boolean; label: string; onChange: (value: string) => void }) {
  const [draft, setDraft] = useState<string | null>(null)
  return <input inputMode="decimal" value={draft ?? value} disabled={disabled}
    onChange={(e) => { setDraft(e.target.value); onChange(e.target.value) }} onBlur={() => setDraft(null)}
    className={cx(inputCls, '!w-24 text-right disabled:bg-wash disabled:opacity-60', invalid && '!border-danger')}
    aria-invalid={invalid} aria-label={label} />
}

export type FormInit = { form?: Partial<Form>; receiptId?: string | null; tx?: Tx; sourceLabel?: string }

// One common form for editing and for all three "Dodaj wydatek" sources (manual / bank transaction / receipt).
// `initial` only changes how the form starts; the data model and the preview/save flow are identical.
type Props = { id?: string; initial?: FormInit; onBack?: () => void; onCancel?: () => void; backLabel?: string; onSaved?: () => void; onInitialReceiptDetached?: () => void }

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

function FormBody({ existing, initial, onBack, onCancel, backLabel, onSaved, onInitialReceiptDetached }: Props & { existing?: ExpenseDetail }) {
  const { navigate, showToast, goBack, accounts } = useApp()
  const ex = useExpenses() // receipts seen in this session
  const svc = useExpenseService()
  const groups = useGroups()
  const unitListId = useId()
  const id = existing?.id
  const groupsQ = useRemote(() => groups.fetchList('active'), [groups.rev])
  const myGroups = (groupsQ.data ?? []).map((x) => x.group)
  const groupName = (gid: string) => existing?.groupName ?? myGroups.find((g) => g.id === gid)?.name ?? ''

  const [form, setForm] = useState<Form>(() => {
    if (existing) return formFromExpense(existing)
    // Source values initialize the form once; subsequent edits remain under the user's control.
    const p = nowParts()
    return { groupId: '', payerId: ME_ID, name: '', merchant: '', amount: '', date: p.date, time: p.time, items: [], shares: [], bankTransactionId: null, receiptId: initial?.receiptId ?? null, ...initial?.form }
  })
  const membersQ = useRemote(() => groups.fetchActiveMembers(form.groupId), [form.groupId, groups.rev], !!form.groupId)
  const mem = useMemo(() => (form.groupId ? membersQ.data ?? [] : []), [membersQ.data, form.groupId])
  // Bank transactions are shown from the loaded object (picked in this session, or fetched by id).
  const [txs, setTxs] = useState<Record<string, Tx>>(() => (initial?.tx ? { [initial.tx.id]: initial.tx } : {}))
  const tx = form.bankTransactionId ? txs[form.bankTransactionId] : undefined
  const hasBank = !!form.bankTransactionId
  const otherBankOwner = hasBank && form.payerId !== ME_ID
  const bankOwnerHint = `Transakcja bankowa jest powiązana z płatnikiem: ${userLabel(form.payerId)}. Tylko jej właściciel może ją zmienić lub odłączyć.`
  const bankAmount = tx ? Math.abs(Number(tx.amount)) : hasBank && isNum(form.amount) ? num(form.amount) : null
  const [bankLoadError, setBankLoadError] = useState(false)
  useEffect(() => {
    const tid = form.bankTransactionId
    if (!tid || txs[tid] || otherBankOwner) return
    let cancelled = false
    setBankLoadError(false)
    void svc.getBankTransaction(tid).then((r) => {
      if (cancelled) return
      if (r.ok) setTxs((t) => ({ ...t, [tid]: r.data }))
      else setBankLoadError(true)
    }).catch(() => { if (!cancelled) setBankLoadError(true) })
    return () => { cancelled = true }
  }, [form.bankTransactionId, otherBankOwner]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (existing?.receiptId) void svc.getReceipt(existing.id) }, []) // eslint-disable-line react-hooks/exhaustive-deps
  // The concurrency token is internal state and is never displayed.
  const [version, setVersion] = useState(existing?.version ?? '')
  const [step, setStep] = useState<'edit' | 'preview'>('edit')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [submitted, setSubmitted] = useState(false)
  const [apiErr, setApiErr] = useState<number | null>(null)
  const [conflict, setConflict] = useState(false)
  const [bankOpen, setBankOpen] = useState(false)
  const [transactionOpen, setTransactionOpen] = useState(false)
  const [payerHint, setPayerHint] = useState(false)
  const [blockedPayer, setBlockedPayer] = useState<string | null>(null)
  const [shareLocks, setShareLocks] = useState<Set<string>>(() => new Set())
  const [shareMode, setShareMode] = useState<'percent' | 'amount'>('percent')
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
  const { problems } = computeExpense(form, { activePayers: existing && form.payerId === existing.payerId ? [...mem, existing.payerId] : mem, bankAmount })
  const show = (k: string) => (submitted ? problems.fields[k] : undefined)
  const hasDiscountErrors = form.items.some((it) => {
    const errors = problems.itemFields[it.id]
    return !!errors?.discount || (it.categoryId === DISCOUNT_CATEGORY_ID && !!(errors?.unitPrice || errors?.quantity))
  })
  const receipt = form.receiptId ? ex.getReceipt(form.receiptId) : undefined

  const itemTotals = new Map(form.items.map((it) => [it.id, problems.items[it.id] ? null : itemTotal({ unitPrice: num(it.unitPrice), quantity: num(it.quantity), discount: it.discount.trim() ? num(it.discount) : 0 })]))
  const known = r2([...itemTotals.values()].reduce<number>((sum, total) => sum + (total ?? 0), 0))
  const est = bankAmount ?? (isNum(form.amount) ? num(form.amount) : known)
  const sharePct = r2(form.shares.reduce((s, x) => s + (isNum(x.percentage) ? num(x.percentage) : 0), 0))

  if (existing && !existing.canEdit) return <div className="mx-auto max-w-2xl space-y-4"><PageTitle title="Edycja wydatku" /><ExpErrorBox code={403} /><Btn variant="ghost" onClick={() => navigate({ name: 'expense', id })}>Wróć do wydatku</Btn></div>

  const back = () => (id ? navigate({ name: 'expense', id }) : goBack())
  const stepBack = onBack ?? back

  const changeGroup = async (gid: string) => {
    setShareLocks(new Set())
    set({ groupId: gid, payerId: hasBank ? form.payerId : '', shares: [] })
    if (!gid) return
    let m: string[] = []
    try { m = await groups.fetchActiveMembers(gid) } catch { /* the member list shows the error; the form stays empty */ }
    setForm((f) => {
      if (f.groupId !== gid) return f
      const payerId = hasBank ? f.payerId : m.includes(ME_ID) ? ME_ID : m[0] ?? ''
      return { ...f, payerId, shares: payerId ? [{ userId: payerId, percentage: '100' }] : [] }
    })
  }

  const pickBank = (picked: Tx) => {
    if (otherBankOwner) return
    // The amount and payer become constrained by the bank source (the payer is the current user, owner of the transaction).
    // Prefill name, date and total only on explicit attachment; merchant and products stay untouched.
    setTxs((t) => ({ ...t, [picked.id]: picked })); set(bankFormValues(picked)); setPayer(ME_ID)
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
    if (!uploaded.current.includes(form.receiptId)) uploaded.current.push(form.receiptId)
    setOcrOpen(true)
  }

  // A bank-backed expense must send the debit's absolute amount (the form hides the amount field in that case).
  const payload = (): Form => (hasBank && bankAmount !== null ? { ...form, amount: String(bankAmount) } : form)
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
    if (problems.list.length) { setSubmitted(true); setStep('edit'); return }
    setApiErr(null); setBusy('save')
    const r = existing ? await svc.updateExpense(existing.id, payload(), version) : await svc.createExpense(payload())
    setBusy(null)
    if (!r.ok) { if (r.code === 409 && existing) setConflict(true); return setApiErr(r.code) }
    saved.current = true
    onSaved?.()
    showToast(existing ? 'Zapisano zmiany w wydatku' : 'Wydatek został zapisany')
    navigate({ name: 'expense', id: r.data.id })
  }
  const reloadCurrent = async () => { if (!existing) return; const r = await svc.getExpense(existing.id); if (!r.ok) return setApiErr(r.code); setForm(formFromExpense(r.data)); setShareLocks(new Set()); setVersion(r.data.version); setConflict(false); setApiErr(null); setStep('edit') }
  const keepMine = async () => { if (!existing) return; const r = await svc.getExpense(existing.id); if (!r.ok) return setApiErr(r.code); setVersion(r.data.version); setConflict(false); setApiErr(null); setStep('edit'); showToast('Pobrano aktualną wersję. Sprawdź swoje zmiany i zapisz ponownie.') }

  const setItem = (iid: string, p: Partial<Form['items'][number]>) => setForm((f) => ({
    ...f,
    items: f.items.map((i) => (i.id === iid ? {
      ...i, ...p, ...(p.categoryId === DISCOUNT_CATEGORY_ID ? { quantity: '1', discount: '0' } : {}),
    } : i)),
  }))
  const setShare = (uid: string, value: string) => setForm((f) => ({ ...f, shares: changeShare(f.shares, shareLocks, uid, value) }))
  const setShareAmount = (uid: string, value: string) => setForm((f) => ({ ...f, shares: changeShareAmount(f.shares, shareLocks, uid, value, est) }))
  const splitEqual = () => setForm((f) => ({ ...f, shares: balanceShares(f.shares, shareLocks, true) }))
  const setPayer = (payerId: string) => {
    const onlyPayer = form.shares.length === 0 || (form.shares.length === 1 && form.shares[0].userId === form.payerId && !shareLocks.has(form.payerId))
    set({ payerId, ...(onlyPayer ? { shares: payerId ? [{ userId: payerId, percentage: '100' }] : [] } : {}) })
  }
  const availableParticipants = mem.filter((userId) => !form.shares.some((share) => share.userId === userId))
  const unlockedCount = form.shares.filter((share) => !shareLocks.has(share.userId)).length
  const lockedPct = form.shares.reduce((sum, share) => sum + (shareLocks.has(share.userId) ? num(share.percentage) : 0), 0)
  const canAddParticipant = !form.shares.length || (unlockedCount > 0 && lockedPct < 100)

  const title = existing ? 'Edytuj wydatek' : 'Dodaj wydatek'

  /* ---------- Preview step ---------- */
  if (step === 'preview' && preview) {
    return (
      <div className="mx-auto max-w-4xl space-y-6">
        <div><button onClick={() => setStep('edit')} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />Wróć do edycji</button><PageTitle title="Podgląd wydatku" sub="Dane z formularza wydatku. Zweryfikuj przed zapisem." /></div>
        <Card>
          <H>Podstawowe informacje</H>
          <KeyValue rows={[
            ['Grupa', groupName(form.groupId)], ['Płatnik', userLabel(form.payerId)],
            ['Nazwa', form.name.trim()], ['Sprzedawca', form.merchant.trim() || '-'],
            ['Kwota całkowita', money(preview.totalAmount)], ['Data', fmtDay(form.date)], ['Waluta', 'PLN'],
          ]} />
        </Card>
        <Card>
          <H>Źródła</H>
          <p className="text-sm text-ink2">Transakcja bankowa: {otherBankOwner ? `dołączona — właściciel: ${userLabel(form.payerId)}` : tx ? `${tx.description ?? 'Bez opisu'} (${money(Math.abs(Number(tx.amount)))})` : hasBank ? 'dołączona (szczegóły niedostępne)' : 'brak'}</p>
          <p className="text-sm text-ink2">Paragon: {receipt ? receipt.fileName : 'brak'}</p>
        </Card>
        <Card className="!p-2">
          <div className="px-3 pt-3"><H>Produkty</H></div>
          <Table head={['Produkt', 'Kategoria', 'Cena jedn.', 'Ilość', 'Rabat', 'Razem']}>
            {preview.items.map((i) => (
              <tr key={i.id}>
                <Td className="font-medium">{i.name}</Td>
                <Td><CatBadge id={i.categoryId} compact /></Td><Td className="num">{money(i.unitPrice)}</Td><Td className="num">{String(i.quantity).replace('.', ',')}</Td>
                <Td className="num">{i.discount ? money(i.discount) : '-'}</Td><Td className="num font-semibold">{money(i.total)}</Td>
              </tr>
            ))}
          </Table>
          {preview.unknownAmount > 0 && <p className="px-3 py-3 text-sm text-ink2">Kwota wydatku jest większa niż suma produktów, więc różnica {money(preview.unknownAmount)} trafi do pozycji „Nieznane”.</p>}
        </Card>
        <Card className="!p-2">
          <div className="px-3 pt-3"><H>Uczestnicy i udziały</H></div>
          <Table head={['Uczestnik', 'Udział', 'Kwota']}>{preview.shares.map((s) => <tr key={s.userId}><Td className="font-medium">{userLabel(s.userId)}</Td><Td className="num">{fmtPct(s.percentage)}</Td><Td className="num font-semibold">{money(s.amount)}</Td></tr>)}</Table>
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
      <div><button onClick={stepBack} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />{onBack ? backLabel ?? 'Wstecz' : existing ? 'Wydatek' : 'Wydatki'}</button><PageTitle title={title} sub="Uzupełnij dane wydatku" /></div>

      {conflict && <ConflictBox onReload={reloadCurrent} onKeep={keepMine} />}
      {!conflict && <ExpErrorBox code={apiErr} />}

      <Card className="space-y-3">
        <H>Podstawowe informacje</H>
        <div className="grid gap-x-4 gap-y-2 sm:grid-cols-2">
          {existing ? (
            <div><span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">Grupa</span><p className="rounded-xl border border-line bg-wash px-3 py-2.5 text-sm font-semibold">{groupName(form.groupId)}</p><p className="mt-0.5 h-5 overflow-y-auto text-xs leading-tight text-ink2">Grupy nie można zmienić po utworzeniu wydatku.</p></div>
          ) : (
            <Field label="Grupa" error={show('groupId')}><select value={form.groupId} onChange={(e) => changeGroup(e.target.value)} className={inputCls}><option value="">{myGroups.length === 0 ? 'Brak grup' : 'Wybierz grupę…'}</option>{myGroups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}</select></Field>
          )}
          <div className="relative">
            <Field label="Płatnik" error={show('payerId')} hint={form.groupId ? (otherBankOwner ? 'Płatnik jest właścicielem dołączonej transakcji bankowej.' : hasBank ? 'Płatnikiem jesteś Ty - właściciel powiązanej transakcji bankowej.' : 'Wybierz spośród aktywnych członków grupy.') : undefined}>
              {form.groupId ? (
                <select value={form.payerId} disabled={hasBank} onChange={(e) => setPayer(e.target.value)} className={cx(inputCls, hasBank && 'opacity-60')}>
                  <option value="">Wybierz płatnika…</option>{payerChoices.map((u) => <option key={u} value={u}>{userLabel(u)}</option>)}{form.payerId && !payerChoices.includes(form.payerId) && <option value={form.payerId}>{userLabel(form.payerId)}</option>}
                </select>
              ) : (
                <button type="button" aria-label="Płatnik" aria-describedby={payerHint ? 'form-payer-hint' : undefined}
                  onClick={() => setPayerHint(true)} onBlur={() => setPayerHint(false)} onKeyDown={(e) => { if (e.key === 'Escape') setPayerHint(false) }}
                  className={cx(inputCls, 'flex items-center justify-between text-left')}>Wybierz płatnika…<ChevronDown size={16} /></button>
              )}
            </Field>
            {!form.groupId && payerHint && <p id="form-payer-hint" role="status" className="absolute left-0 top-[62px] z-20 rounded-lg border border-line bg-white p-2 text-xs text-ink2 shadow-card">Najpierw wybierz grupę.</p>}
          </div>
          <div className="sm:col-span-2"><Field label="Nazwa wydatku" error={show('name')}><input value={form.name} onChange={(e) => set({ name: e.target.value })} className={cx(inputCls, show('name') && '!border-danger')} placeholder="np. Zakupy tygodniowe" /></Field></div>
          <Field label="Sprzedawca (opcjonalnie)"><input value={form.merchant} onChange={(e) => set({ merchant: e.target.value })} className={inputCls} /></Field>
          <Field label={hasBank ? 'Kwota całkowita' : 'Kwota całkowita (opcjonalnie)'} error={show('amount')} hint={hasBank ? 'Kwota wynika z dołączonej transakcji bankowej.' : 'Puste pole: kwota zostanie policzona z produktów.'}><input inputMode="decimal" value={hasBank && bankAmount !== null ? String(bankAmount).replace('.', ',') : form.amount} disabled={hasBank} onChange={(e) => set({ amount: e.target.value })} className={cx(inputCls, hasBank && 'opacity-60')} /></Field>
          <Field label="Data" error={show('date')}><input type="date" value={form.date} onChange={(e) => set({ date: e.target.value })} className={cx(inputCls, show('date') && '!border-danger')} /></Field>
        </div>
        <div className="text-sm"><span className="font-semibold">WALUTA: PLN</span><p className="mt-1 text-xs text-ink2">4Money obecnie obsługuje wyłącznie PLN.</p></div>
      </Card>

      <Card className="space-y-4">
        <H>Źródła</H>
        <div className="rounded-xl border border-line p-4">
          <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><Landmark size={16} />Transakcja bankowa (opcjonalnie)</p>
          {otherBankOwner ? (
            <div className="space-y-2 text-sm">
              <p className="font-medium">Transakcja bankowa jest dołączona</p>
              <p id="bank-owner-hint" className="text-xs text-ink2">{bankOwnerHint}</p>
            </div>
          ) : tx ? (
            <div className="space-y-2 text-sm">
              <p className="font-medium">{tx.description ?? 'Bez opisu'}</p>
              <p className="text-ink2">{accounts.find((a) => a.id === tx.bankAccountId)?.name} · {dayText(tx.date)} · <span className="num font-semibold text-ink">{money(Math.abs(Number(tx.amount)))}</span></p>
              <p className="text-xs text-ink2">Nazwa wydatku, data i kwota całkowita zostały uzupełnione na podstawie dołączonej transakcji.</p>
              <div className="flex flex-wrap gap-2"><Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => setTransactionOpen(true)}>Szczegóły transakcji</Btn><Btn variant="ghost" className="!min-h-8 !px-3 !py-1" onClick={() => setBankOpen(true)}>Zmień</Btn><Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => set({ bankTransactionId: null, name: '', amount: '', date: nowParts().date })}>Odłącz transakcję</Btn></div>
            </div>
          ) : hasBank ? (
            <div className="space-y-2 text-sm">
              <p className="font-medium">Transakcja bankowa jest dołączona</p>
              <p className="text-xs text-ink2" role="status">{bankLoadError ? 'Nie udało się pobrać szczegółów transakcji. Powiązanie pozostaje zachowane.' : 'Wczytywanie szczegółów transakcji…'}</p>
              <div className="flex flex-wrap gap-2"><Btn variant="ghost" onClick={() => setBankOpen(true)}>Zmień</Btn><Btn variant="soft" onClick={() => set({ bankTransactionId: null, name: '', amount: '', date: nowParts().date })}>Odłącz transakcję</Btn></div>
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
                <p className="text-xs text-ink2">Dołączenie pliku nie uruchomi analizy paragonu.</p>
                <div className="flex flex-wrap gap-2">
                  <Btn onClick={analyze} disabled={busy !== null}>{busy === 'ocr' ? <Loader2 size={16} className="animate-spin" /> : <ScanLine size={16} />}Analizuj paragon</Btn>
                  {receipt.latestAnalysis && <Btn variant="ghost" onClick={() => setOcrOpen(true)}>Pokaż wynik analizy paragonu</Btn>}
                  <Btn variant="ghost" onClick={() => file.current?.click()} disabled={busy !== null}>Zamień plik</Btn>
                  <Btn variant="soft" onClick={detach}>Odłącz paragon</Btn>
                </div>
              </div>
            </div>
          ) : (
            <div className="space-y-2">
              <Btn variant="ghost" onClick={() => file.current?.click()} disabled={busy !== null}>{busy === 'upload' ? <Loader2 size={16} className="animate-spin" /> : <Upload size={16} />}Dołącz paragon</Btn>
              <p className="text-xs text-ink2">Dołącz jeden plik: JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP lub PDF. Dołączenie pliku nie uruchomi analizy paragonu.</p>
            </div>
          )}
          {recErr && <div className="mt-3"><ExpErrorBox code={recErr} /></div>}
        </div>
      </Card>

      {ocrOpen && receipt && <OcrPanel receipt={ex.getReceipt(receipt.id) ?? receipt} form={form} hasBank={hasBank} onApply={(f) => { setForm(f); showToast('Dane z analizy wczytane do formularza - sprawdź je przed zapisem') }} onClose={() => setOcrOpen(false)} />}

      <Card className="space-y-4">
        <H aside={<Btn variant="ghost" className="!min-h-9" onClick={() => set({ items: [...form.items, emptyItem()] })}><Plus size={16} />Dodaj produkt</Btn>}>Produkty</H>
        <CategoryLoadError />
        <datalist id={unitListId}>{['szt.', 'kg', 'g', 'l', 'ml', 'm', 'cm', 'opak.', 'para'].map((unit) => <option key={unit} value={unit} />)}</datalist>
        <p className="text-xs text-ink2">Rabat dotyczy całej pozycji i domyślnie jest zerem. Jeśli chcesz dodać rabat, wprowadź wartość ujemną.</p>
        <p className="text-xs text-ink2">Dla kategorii „Zniżka” wprowadź ujemną cenę jednostkową oraz ilość równą 1. Rabat pozostaw zerowy.</p>
        {form.items.length === 0 && <p className="rounded-xl bg-wash p-3 text-sm text-ink2">Brak produktów. Cała kwota wydatku trafi do pozycji „Nieznane”.</p>}
        {form.items.map((it, n) => {
          const errs = submitted || it.categoryId === DISCOUNT_CATEGORY_ID ? problems.itemFields[it.id] : { discount: problems.itemFields[it.id]?.discount }
          const missingName = !it.name.trim()
          const missingCategory = !it.categoryId
          const total = itemTotals.get(it.id) ?? null
          return (
            <div key={it.id} className="rounded-xl border border-line p-3">
              <div className="grid gap-3 sm:grid-cols-[minmax(130px,1fr)_minmax(0,3fr)_auto]">
                <div className="min-w-0"><Field label="Nazwa" error={missingName ? 'Nazwij produkt i wybierz jego kategorię.' : undefined} summaryError><input value={it.name} onChange={(e) => setItem(it.id, { name: e.target.value })} className={inputCls} aria-invalid={missingName} /></Field></div>
                <div className="grid min-w-0 grid-cols-2 gap-x-2 gap-y-1 sm:grid-cols-3 xl:grid-cols-[minmax(130px,1.8fr)_minmax(50px,.7fr)_minmax(75px,1fr)_minmax(50px,.7fr)_minmax(75px,1fr)]">
                  <Field label="Kategoria" error={missingCategory ? 'Nazwij produkt i wybierz jego kategorię.' : undefined} summaryError><select value={it.categoryId} disabled={ex.categoriesLoading || ex.categoriesError !== null} onChange={(e) => setItem(it.id, { categoryId: e.target.value })} className={inputCls} aria-invalid={missingCategory}><option value="">{ex.categoriesLoading ? 'Ładowanie kategorii…' : ex.categoriesError !== null ? 'Kategorie niedostępne' : 'Wybierz kategorię…'}</option>{ex.categories.filter((c) => !c.isSystemGenerated).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></Field>
                  <Field label="Jedn."><input value={it.unit} list={unitListId} onChange={(e) => setItem(it.id, { unit: e.target.value })} className={inputCls} /></Field>
                  <Field label="Cena jedn." error={errs?.unitPrice} summaryError><input inputMode="decimal" value={it.unitPrice} onChange={(e) => setItem(it.id, { unitPrice: e.target.value })} className={inputCls} aria-invalid={!!errs?.unitPrice} /></Field>
                  <Field label="Ilość" error={errs?.quantity} summaryError><input inputMode="decimal" value={it.quantity} onChange={(e) => setItem(it.id, { quantity: e.target.value })} className={inputCls} aria-invalid={!!errs?.quantity} /></Field>
                  <Field label="Rabat" error={errs?.discount} summaryError><input inputMode="decimal" value={it.discount} onChange={(e) => setItem(it.id, { discount: e.target.value })} className={inputCls} placeholder="0" aria-invalid={!!errs?.discount} /></Field>
                </div>
                <button onClick={() => set({ items: form.items.filter((_, i) => i !== n) })} aria-label="Usuń produkt" className="mt-5 justify-self-end self-start rounded-lg p-2 text-ink2 hover:bg-danger-soft hover:text-danger"><Trash2 size={16} /></button>
              </div>
              <p className="text-sm text-ink2"><span className="font-semibold">RAZEM:</span> <span className="num font-semibold text-ink">{total !== null ? money(total) : '-'}</span></p>
              {(missingName || missingCategory) && <p role="alert" className="mt-1 text-xs text-danger">Nazwij produkt i wybierz jego kategorię.</p>}
              {[errs?.unitPrice, errs?.quantity, errs?.discount].filter(Boolean).map((error) => <p key={error} role="alert" className="mt-1 text-xs text-danger">{error}</p>)}
            </div>
          )
        })}
        <div className="rounded-xl bg-wash p-3 text-sm">
          <p className="flex justify-between"><span className="text-ink2">Suma produktów</span><span className="num font-semibold">{money(known)}</span></p>
          {est > known + 0.001 && <p className="mt-1 flex justify-between gap-3"><span className="text-ink2">Różnica zostanie dodana jako pozycja „Nieznane”, aby suma produktów zgadzała się z kwotą wydatku.</span><span className="num shrink-0 font-semibold">{money(r2(est - known))}</span></p>}
          {known > est + 0.001 && est > 0 && <p className="mt-1 text-danger">Suma produktów przekracza wprowadzoną kwotę całkowitą wydatku.</p>}
          <p className="mt-2 text-xs text-ink2">To wstępne wyliczenie. Ostateczną kwotę zobaczysz w widoku „Podgląd wydatku”.</p>
        </div>
      </Card>

      <Card className="space-y-4">
        <H aside={form.groupId && <Btn variant="ghost" className="!min-h-9" onClick={splitEqual} disabled={unlockedCount === 0}>Podziel równo</Btn>}>Uczestnicy i udziały</H>
        {!form.groupId ? <p className="text-sm text-ink2">Najpierw wybierz grupę.</p> : <>
        <p className="text-xs text-ink2">Suma udziałów musi wynosić 100%. Każdy uczestnik musi mieć udział większy od 0%. Kwoty w złotówkach są wstępnym wyliczeniem. Ostateczne kwoty zobaczysz w widoku „Podgląd wydatku”.</p>
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs font-semibold uppercase tracking-wide text-ink2">Podział według</span>
          <Segmented<'percent' | 'amount'> value={shareMode} onChange={setShareMode} options={[{ v: 'percent', label: 'Procentów (%)' }, { v: 'amount', label: 'Kwot (zł)', disabled: est <= 0 }]} />
        </div>
        <div className="max-w-sm">
          <BaseField label="Dodaj uczestnika"><select value="" disabled={availableParticipants.length === 0 || !canAddParticipant} title={availableParticipants.length > 0 && !canAddParticipant ? 'Odblokuj udział, aby dodać uczestnika.' : undefined} onChange={(e) => { const uid = e.target.value; if (uid) setForm((f) => ({ ...f, shares: addShare(f.shares, shareLocks, uid) })) }} className={cx(inputCls, 'disabled:cursor-default disabled:bg-wash disabled:text-ink2 disabled:opacity-60')}>
            <option value="" hidden={availableParticipants.length > 0} disabled>{availableParticipants.length ? 'Wybierz członka grupy…' : 'Wszyscy członkowie grupy zostali dodani'}</option>{availableParticipants.map((uid) => <option key={uid} value={uid}>{userLabel(uid)}</option>)}
          </select></BaseField>
        </div>
        {form.shares.map((s) => {
          const u = s.userId
          const pct = isNum(s.percentage) ? num(s.percentage) : 0
          const invalid = !isNum(s.percentage) || pct <= 0 || pct > 100
          const locked = shareLocks.has(u)
          const amountMode = shareMode === 'amount'
          const amount = r2(est * pct / 100)
          const fixed = locked || unlockedCount <= 1 || (amountMode && est <= 0)
          return (
            <div key={u} className="flex flex-wrap items-center gap-3 rounded-xl border border-line p-3">
              <span className="flex min-w-[170px] items-center gap-2 text-sm font-semibold">{userLabel(u)}{!mem.includes(u) && <Badge>Były członek</Badge>}</span>
              <input type="range" min={0} max={amountMode ? Math.ceil(est) : 100} step={1} value={amountMode ? Math.round(Math.min(est, Math.max(0, amount))) : Math.min(100, Math.max(0, pct))} disabled={fixed}
                onChange={(e) => amountMode ? setShareAmount(u, String(Math.min(est, num(e.target.value)))) : setShare(u, e.target.value)} aria-label={`Udział ${userLabel(u)} w ${amountMode ? 'złotych' : 'procentach'}`} aria-invalid={invalid} className="min-w-[120px] flex-1 disabled:opacity-40" />
              <button type="button" aria-pressed={locked} disabled={!locked && invalid} aria-label={`${locked ? 'Odblokuj' : 'Zablokuj'} udział ${userLabel(u)}`} title={locked ? 'Odblokuj udział' : 'Zablokuj udział'} onClick={() => setShareLocks((current) => { const next = new Set(current); if (next.has(u)) next.delete(u); else next.add(u); return next })} className={cx('rounded-lg p-2 transition disabled:opacity-40', locked ? 'bg-wash text-ink' : 'text-ink2 hover:bg-wash')}>
                {locked ? <Lock size={16} /> : <LockOpen size={16} />}
              </button>
              <div className="flex items-center gap-1"><ShareValueInput key={shareMode} value={amountMode ? (isNum(s.percentage) ? String(amount).replace('.', ',') : '') : s.percentage} disabled={locked || (amountMode && est <= 0)} invalid={invalid} onChange={(value) => amountMode ? setShareAmount(u, value) : setShare(u, value)} label={`${amountMode ? 'Kwota' : 'Procent'} ${userLabel(u)}`} /><span className="text-sm">{amountMode ? 'zł' : '%'}</span></div>
              <span className="num w-24 text-right text-sm text-ink2">{amountMode ? fmtPct(pct) : `≈ ${money(amount)}`}</span>
              <div className="relative" tabIndex={u === form.payerId ? 0 : undefined} aria-label={u === form.payerId ? `Usuwanie płatnika ${userLabel(u)} niedostępne` : undefined} aria-describedby={u === form.payerId && blockedPayer === u ? 'payer-removal-hint' : undefined}
                onMouseEnter={() => { if (u === form.payerId) setBlockedPayer(u) }} onMouseLeave={() => setBlockedPayer(null)} onFocus={() => { if (u === form.payerId) setBlockedPayer(u) }} onBlur={() => setBlockedPayer(null)} onKeyDown={(e) => { if (e.key === 'Escape') setBlockedPayer(null) }}>
                <button type="button" aria-label={`Usuń uczestnika ${userLabel(u)}`} disabled={u === form.payerId || removeShare(form.shares, shareLocks, u, form.payerId) === form.shares} onClick={() => {
                  setForm((f) => ({ ...f, shares: removeShare(f.shares, shareLocks, u, f.payerId) }))
                  setShareLocks((current) => { const next = new Set(current); next.delete(u); return next })
                }} className="rounded-lg p-2 text-ink2 enabled:hover:bg-danger-soft enabled:hover:text-danger disabled:pointer-events-none disabled:cursor-default disabled:opacity-40"><Trash2 size={16} /></button>
                {u === form.payerId && blockedPayer === u && <p id="payer-removal-hint" role="tooltip" className="absolute right-0 top-full z-20 mt-1 w-60 rounded-lg border border-line bg-white p-2 text-xs text-ink2 shadow-card">Płatnik nie może zostać usunięty z wydatku</p>}
              </div>
            </div>
          )
        })}
        <p className={cx('text-sm font-semibold', Math.abs(sharePct - 100) < 0.001 ? 'text-safe' : 'text-danger')}>Suma udziałów: {fmtPct(sharePct)} {Math.abs(sharePct - 100) < 0.001 ? '✓' : '✕'}</p>
        </>}
      </Card>

      <div className="flex flex-wrap justify-end gap-2">
        <Btn variant="ghost" onClick={onCancel ?? back}>Anuluj</Btn>
        <Btn onClick={goPreview} disabled={busy === 'save' || hasDiscountErrors}>Podgląd wydatku</Btn>
      </div>
      {transactionOpen && tx && <TransactionModal tx={tx} onClose={() => setTransactionOpen(false)} />}
      {bankOpen && !otherBankOwner && <BankPicker onPick={pickBank} onClose={() => setBankOpen(false)} />}
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
