import { banking } from '../api/banking'
import { useRemote } from '../api/hooks'
import { describeError } from '../api/errors'
import { useApp } from '../context'
import { dayText, fmtMoney, transactionStatus, type Tx } from '../mocks/bank'
import { Badge, KeyValue, Modal, cx } from '../ui'

export const txTone = (status: string) => status.toUpperCase() === 'BOOKED' ? 'ok' : status.toUpperCase() === 'PENDING' ? 'warn' : 'neutral'
export const TxAmount = ({ t }: { t: Tx }) => <span className={cx('num whitespace-nowrap font-semibold', Number(t.amount) > 0 && 'text-safe')}>{Number(t.amount) > 0 ? '+' : ''}{fmtMoney(t.amount, t.currencyCode)}</span>

function TransactionContent({ tx }: { tx: Tx }) {
  const { accounts } = useApp()
  return <>
    <p className="mb-4 text-3xl"><TxAmount t={tx} /></p>
    <KeyValue rows={[
      ['Opis', tx.description ?? '-'], ['Data', dayText(tx.date)], ['Waluta', tx.currencyCode],
      ['Status', <Badge key="status" tone={txTone(tx.status)}>{transactionStatus(tx.status)}</Badge>],
      ['Konto', accounts.find((account) => account.id === tx.bankAccountId)?.name ?? tx.bankAccountId],
    ]} />
  </>
}

export function BankTransactionDetails({ id }: { id: string }) {
  const q = useRemote(() => banking.transaction(id), [id])
  return q.data ? <TransactionContent tx={q.data} /> : <p className="text-ink2">{q.loading ? 'Ładowanie…' : (q.error !== null && describeError(q.error)) || 'Nie znaleziono transakcji.'}</p>
}

export function BankTransactionModal({ id, onClose }: { id: string; onClose: () => void }) {
  return <Modal title="Szczegóły transakcji" onClose={onClose}><BankTransactionDetails id={id} /></Modal>
}

export function TransactionModal({ tx, onClose }: { tx: Tx; onClose: () => void }) {
  return <Modal title="Szczegóły transakcji" onClose={onClose}><TransactionContent tx={tx} /></Modal>
}
