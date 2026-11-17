import { ArrowRight, Plus } from 'lucide-react'
import { useRemote } from '../api/hooks'
import type { ReactNode } from 'react'
import { useApp } from '../context'
import { CONNECTION_STATUS_PL, dateText, label } from '../mocks/bank'
import { Badge, Btn, Card, H } from '../ui'
import { TransactionsPanel } from './Banking'
import { NotificationsBell } from './Notifications'

export default function Dashboard() {
  const { navigate, unread, connections, accounts, bankingLoaded, lastRefresh, fetchNotifications, notifVersion } = useApp()
  const notesQ = useRemote(() => fetchNotifications('all', 1, 3), [notifVersion])
  const notes = notesQ.data?.items ?? []
  const lastSync = connections.map((c) => c.lastSuccessfulSyncAt).filter(Boolean).sort().pop()
  const stat = (title: string, value: ReactNode, sub?: ReactNode) => (
    <Card><p className="text-sm font-semibold text-ink2">{title}</p><div className="num mt-2 font-display text-2xl font-bold">{value}</div>{sub && <p className="mt-1 text-sm text-ink2">{sub}</p>}</Card>
  )
  const more = (to: () => void) => <button className="flex items-center gap-1 text-sm font-semibold text-ink" onClick={to}>Wszystkie <ArrowRight size={14} /></button>
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-display text-2xl font-bold">Pulpit</h1>
          <p className="mt-1 text-ink2">Podsumowanie Twoich wydatków, kont bankowych i powiadomień</p>
        </div>
        <div className="flex flex-wrap items-center gap-3"><Btn onClick={() => navigate({ name: 'expense-new' })}><Plus size={16} />Dodaj wydatek</Btn><NotificationsBell /></div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {stat('Połączenia z bankami', connections.length, connections.map((c) => `${c.providerName}: ${label(CONNECTION_STATUS_PL, c.connectionStatus).toLowerCase()}`).join(' · ') || 'Brak połączeń')}
        {stat('Konta bankowe', bankingLoaded ? accounts.length : '-')}
        {stat('Ostatnia udana synchronizacja', <span className="text-lg">{dateText(lastSync)}</span>, lastRefresh ? <>Ostatnie odświeżenie: <Badge>{lastRefresh.status}</Badge></> : undefined)}
        {stat('Nieprzeczytane', unread, <button className="font-semibold text-ink" onClick={() => navigate({ name: 'notifications' })}>Zobacz powiadomienia</button>)}
      </div>

      <div><H aside={more(() => navigate({ name: 'banking', tab: 'transactions' }))}>Ostatnie transakcje bankowe</H><TransactionsPanel mode="recent" /></div>

      <Card className="!p-3">
        <div className="px-2 pt-2"><H aside={more(() => navigate({ name: 'notifications' }))}>Ostatnie powiadomienia</H></div>
        {notes.map((n) => (
          <button key={n.id} onClick={() => navigate({ name: 'notifications' })} className="flex w-full items-center justify-between gap-3 rounded-xl px-3 py-2.5 text-left transition hover:bg-wash">
            <span className="min-w-0"><span className="block truncate text-sm font-semibold">{n.title}</span><span className="block truncate text-xs text-ink2">{n.text}</span></span>
            {!n.isRead && <span aria-label="Nieprzeczytane" className="h-2 w-2 shrink-0 rounded-full bg-personal" />}
          </button>
        ))}
      </Card>
    </div>
  )
}
