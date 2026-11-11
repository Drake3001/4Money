import { useState } from 'react'
import { useRemote } from '../../api/hooks'
import { ArrowLeft, Landmark, Pencil, ReceiptText, Trash2 } from 'lucide-react'
import { useApp } from '../../context'
import { useExpenses } from '../../expensesContext'
import { useExpenseService } from '../../services/expenseService'
import { fmtDateTime, itemTotal, fmtPct, shareAmounts, type OcrAnalysis } from '../../mocks/expenses'
import { dateText, dayText, money } from '../../mocks/bank'
import { Badge, Btn, Card, H, KeyValue, Modal, PageTitle, Table, Td } from '../../ui'
import { CatBadge, ExpErrorBox, ReceiptThumb, useNames } from './ExpenseUi'

function OcrSummary({ title, a }: { title: string; a: OcrAnalysis | null }) {
  return (
    <div className="rounded-xl bg-wash p-3 text-sm">
      <p className="mb-1 font-semibold">{title}</p>
      {!a ? <p className="text-ink2">Brak analizy.</p> : (
        <ul className="space-y-0.5 text-ink2">
          <li>Sprzedawca: {a.merchant ?? 'nie wykryto'}</li>
          <li>Kwota: {a.amount != null ? money(a.amount) : 'nie wykryto'}</li>
          <li>Data: {a.transactionDate ?? 'nie wykryto'}{a.transactionTime ? `, ${a.transactionTime}` : ''}</li>
          <li>Pozycje: {a.items.length}{a.needsReview ? ' · wymaga sprawdzenia' : ''}</li>
        </ul>
      )}
    </div>
  )
}

export default function ExpenseDetail({ id }: { id: string }) {
  const { navigate, showToast } = useApp()
  const ex = useExpenses()
  const svc = useExpenseService()
  const names = useNames()
  const q = useRemote(() => svc.getExpense(id), [id])
  const e = q.data?.ok ? q.data.data : undefined
  const failCode = q.error ?? (q.data && !q.data.ok ? q.data.code : null)
  // The version token is kept internally and never shown.
  const [version, setVersion] = useState('')
  const curVersion = version || e?.version || ''
  const txQ = useRemote(() => svc.getBankTransaction(e!.bankTransactionId!), [e?.bankTransactionId], !!e?.bankTransactionId)
  const tx = txQ.data?.ok ? txQ.data.data : undefined
  const recQ = useRemote(() => svc.getReceipt(e!.id), [e?.id, e?.receiptId], !!e?.receiptId)
  const [confirm, setConfirm] = useState(false)
  const [err, setErr] = useState<number | null>(null)
  const [receiptOpen, setReceiptOpen] = useState(false)

  if (!e) return (
    <div className="mx-auto max-w-2xl space-y-4">
      <PageTitle title="Wydatek" />
      {failCode === null ? <p className="text-ink2">Wczytywanie wydatku…</p> : <ExpErrorBox code={failCode} />}
      <Btn variant="ghost" onClick={() => navigate({ name: 'expenses' })}>Wróć do wydatków</Btn>
    </div>
  )

  const receipt = e.receiptId ? ex.getReceipt(e.receiptId) : undefined
  const parts = shareAmounts(e.totalAmount, e.shares)

  const remove = async () => {
    const r = await svc.deleteExpense(e.id, curVersion)
    if (!r.ok) return setErr(r.code)
    showToast('Wydatek został usunięty')
    navigate({ name: 'expenses' })
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <button onClick={() => navigate({ name: 'expenses' })} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />Wydatki</button>
        <PageTitle title={e.name} sub={e.merchant ?? 'Brak sprzedawcy'} aside={
          <div className="flex flex-wrap gap-2">
            {e.canEdit && <Btn variant="ghost" onClick={() => navigate({ name: 'expense-edit', id: e.id })}><Pencil size={16} />Edytuj wydatek</Btn>}
            {e.canDelete && <Btn variant="danger" onClick={() => { setErr(null); setConfirm(true) }}><Trash2 size={16} />Usuń wydatek</Btn>}
          </div>
        } />
        {!e.canEdit && <p className="mt-3 rounded-xl bg-wash p-3 text-sm text-ink2">Ten wydatek jest dostępny tylko do odczytu. Nie masz uprawnień do jego zmiany.</p>}
      </div>

      <Card>
        <KeyValue rows={[
          ['Kwota całkowita', <span key="a" className="num font-display text-xl">{money(e.totalAmount)}</span>],
          ['Waluta', <Badge key="c">PLN</Badge>],
          ['Czas transakcji', fmtDateTime(e.transactionTime)],
          ['Grupa', e.groupName],
          ['Płatnik', names.user(e.payerId)],
        ]} />
      </Card>

      <Card>
        <H>Źródła</H>
        <div className="grid gap-4 md:grid-cols-2">
          <div className="rounded-xl border border-line p-4">
            <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><ReceiptText size={16} />Paragon</p>
            {receipt ? (
              <div className="flex items-start gap-3">
                <ReceiptThumb r={receipt} />
                <div className="text-sm"><p className="font-medium">{receipt.fileName}</p><p className="text-xs text-ink2">Dodano: {dateText(receipt.createdAt)}</p><Btn variant="soft" className="mt-2 !min-h-8 !px-3 !py-1" onClick={() => setReceiptOpen(true)}>Zobacz paragon</Btn></div>
              </div>
            ) : e.receiptId ? <p className="text-sm text-ink2">{recQ.loading ? 'Wczytywanie paragonu…' : 'Paragon jest chwilowo niedostępny.'}</p> : <p className="text-sm text-ink2">Brak dołączonego paragonu.</p>}
          </div>
          <div className="rounded-xl border border-line p-4">
            <p className="mb-2 flex items-center gap-2 text-sm font-semibold"><Landmark size={16} />Transakcja bankowa</p>
            {tx ? (
              <div className="text-sm"><p className="font-medium">{tx.description ?? 'Bez opisu'}</p><p className="text-xs text-ink2">{dayText(tx.date)}</p><p className="num mt-1 font-semibold">{money(Math.abs(Number(tx.amount)))}</p>
                <Btn variant="soft" className="mt-2 !min-h-8 !px-3 !py-1" onClick={() => navigate({ name: 'transaction', id: tx.id })}>Szczegóły transakcji</Btn></div>
            ) : e.bankTransactionId ? <p className="text-sm text-ink2">{txQ.loading ? 'Wczytywanie transakcji…' : 'Transakcja bankowa jest niedostępna.'}</p> : <p className="text-sm text-ink2">Brak powiązanej transakcji bankowej.</p>}
          </div>
        </div>
      </Card>

      <Card className="!p-2">
        <div className="px-3 pt-3"><H>Produkty</H></div>
        <Table head={['Produkt', 'Kategoria', 'Jedn.', 'Cena jedn.', 'Ilość', 'Rabat', 'Razem']}>
          {e.items.map((i) => (
            <tr key={i.id}>
              <Td className="font-medium">{i.name}{i.isSystemGenerated && <span className="ml-2 text-xs text-ink2">(dodane automatycznie)</span>}</Td>
              <Td><CatBadge id={i.categoryId} compact /></Td>
              <Td className="text-ink2">{i.unit ?? '—'}</Td>
              <Td className="num">{money(i.unitPrice)}</Td>
              <Td className="num">{String(i.quantity).replace('.', ',')}</Td>
              <Td className="num">{i.discount ? money(i.discount) : '—'}</Td>
              <Td className="num font-semibold">{money(itemTotal(i))}</Td>
            </tr>
          ))}
        </Table>
      </Card>

      <Card className="!p-2">
        <div className="px-3 pt-3"><H>Uczestnicy</H></div>
        <Table head={['Uczestnik', 'Udział', 'Kwota']}>
          {parts.map((p) => (
            <tr key={p.userId}><Td className="font-medium">{names.user(p.userId)}</Td><Td className="num">{fmtPct(p.percentage)}</Td><Td className="num font-semibold">{money(p.amount)}</Td></tr>
          ))}
        </Table>
      </Card>

      {confirm && (
        <Modal title="Usunąć wydatek?" onClose={() => setConfirm(false)}>
          <p className="mb-4 text-sm text-ink2">Usunięcie wydatku jest trwałe. Razem z nim zostanie usunięty dołączony paragon. Powiązana transakcja bankowa nie zostanie usunięta.</p>
          <ExpErrorBox code={err}>
            {err === 409 && <button className="font-semibold underline" onClick={() => { setVersion(''); setErr(null); q.reload() }}>Odśwież dane wydatku</button>}
          </ExpErrorBox>
          <div className="mt-5 flex justify-end gap-2">
            <Btn variant="ghost" onClick={() => setConfirm(false)}>Anuluj</Btn>
            <Btn variant="danger" onClick={remove}>Usuń wydatek</Btn>
          </div>
        </Modal>
      )}

      {receiptOpen && receipt && (
        <Modal title="Paragon" onClose={() => setReceiptOpen(false)}>
          <ReceiptThumb r={receipt} size="lg" />
          <div className="mt-4"><KeyValue rows={[['Plik', receipt.fileName], ['Dodano', dateText(receipt.createdAt)]]} /></div>
          <div className="mt-3 grid gap-3 sm:grid-cols-2"><OcrSummary title="Pierwotna analiza OCR" a={receipt.originalAnalysis} /><OcrSummary title="Najnowsza analiza OCR" a={receipt.latestAnalysis} /></div>
          <div className="mt-5 flex justify-end"><Btn variant="ghost" onClick={() => setReceiptOpen(false)}>Zamknij</Btn></div>
        </Modal>
      )}
    </div>
  )
}
