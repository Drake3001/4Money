import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Bell, Gauge, HandCoins, OctagonAlert, ReceiptText, UserPlus } from 'lucide-react'
import { useApp, type NotifFilter } from '../context'
import { dateText, type Notif } from '../mocks/bank'
import { useRemote } from '../api/hooks'
import { describeError } from '../api/errors'
import { Btn, Card, PageTitle, Pager, Segmented, cx } from '../ui'

// Known numeric types get their own icon; any other value falls back to a neutral bell and still shows title and text.
const TYPE_ICON: Record<number, { icon: ReactNode; cls: string }> = {
  1: { icon: <ReceiptText size={18} />, cls: 'bg-shared-soft text-shared-deep' },
  2: { icon: <Gauge size={18} />, cls: 'bg-warn-soft text-warn' },
  3: { icon: <OctagonAlert size={18} />, cls: 'bg-danger-soft text-danger' },
  4: { icon: <UserPlus size={18} />, cls: 'bg-safe-soft text-safe' },
  5: { icon: <HandCoins size={18} />, cls: 'bg-mint-soft text-ink' },
}
const typeMeta = (t: number) => TYPE_ICON[t] ?? { icon: <Bell size={18} />, cls: 'bg-wash text-ink2' }

function Item({ n, onOpen, links }: { n: Notif; onOpen: () => void; links?: boolean }) {
  const { navigate } = useApp()
  const m = typeMeta(n.type)
  const title = n.type === 5 ? 'Zapisano rozliczenie' : n.title
  return (
    <div className={cx('flex items-start gap-3 rounded-xl p-3 transition hover:bg-wash', !n.isRead && 'bg-wash/60')}>
      <button onClick={onOpen} className="flex min-w-0 flex-1 items-start gap-3 text-left">
        <span className={cx('hidden h-9 w-9 shrink-0 items-center justify-center rounded-lg sm:flex', m.cls)}>{m.icon}</span>
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2 text-sm font-semibold">{title}{!n.isRead && <span aria-label="Nieprzeczytane" className="h-2 w-2 shrink-0 rounded-full bg-personal" />}</span>
          <span className="mt-0.5 block break-words text-sm text-ink2">{n.text}</span>
          <span className="mt-1 block text-xs text-ink2">{dateText(n.createdAt)}{n.readAt ? ` · przeczytano ${dateText(n.readAt)}` : ''}</span>
        </span>
      </button>
      {links && (n.expenseId || n.groupId) && (
        <span className="ml-auto flex w-24 shrink-0 flex-col items-end gap-2 self-center sm:w-auto sm:max-w-48">
          {n.expenseId && <Btn variant="ghost" className="w-full !px-3 !py-2 !text-xs" onClick={() => { onOpen(); navigate({ name: 'expense', id: n.expenseId! }) }}>{n.type === 1 ? 'Zobacz wydatek' : 'Przejdź do wydatku'}</Btn>}
          {n.groupId && <Btn variant="ghost" className="w-full !px-3 !py-2 !text-xs" onClick={() => { onOpen(); navigate({ name: 'group', id: n.groupId!, ...(n.type === 5 ? { tab: 'settlements' } : {}) }) }}>{n.type === 5 ? 'Zobacz rozliczenia w grupie' : n.type === 4 ? 'Zobacz grupę' : 'Przejdź do grupy'}</Btn>}
        </span>
      )}
    </div>
  )
}

export function NotificationsBell() {
  const { fetchNotifications, notifVersion, unread, markRead, markAllRead, navigate } = useApp()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false) }
    document.addEventListener('mousedown', h)
    return () => document.removeEventListener('mousedown', h)
  }, [])
  const latestQ = useRemote(() => fetchNotifications('all', 1, 5), [notifVersion, open], open)
  const latest = latestQ.data?.items ?? []
  return (
    <div ref={ref} className="relative">
      <button aria-label="Powiadomienia" onClick={() => setOpen(!open)} className="relative rounded-xl border border-line bg-white p-2.5 transition hover:bg-wash">
        <Bell size={18} />
        {unread > 0 && <span className="num absolute -right-1.5 -top-1.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-ink px-1 text-xs font-bold text-white">{unread}</span>}
      </button>
      {open && (
        <div className="animate-rise absolute right-0 top-12 z-40 w-[min(380px,calc(100vw-2rem))] rounded-2xl border border-line bg-white p-2 shadow-card">
          <div className="flex items-center justify-between px-3 py-2">
            <b className="font-display text-lg">Powiadomienia</b>
            <button onClick={() => void markAllRead()} disabled={!unread} className="text-xs font-semibold text-ink disabled:opacity-40">Oznacz wszystkie jako przeczytane</button>
          </div>
          <div className="soft-scroll max-h-[420px] overflow-y-auto">
            {latestQ.loading && !latest.length && <p className="p-4 text-center text-sm text-ink2">Ładowanie…</p>}
            {latestQ.error !== null && describeError(latestQ.error) && <p role="alert" className="m-2 rounded-xl bg-danger-soft p-3 text-sm text-danger">{describeError(latestQ.error)}</p>}
            {latest.map((n) => <Item key={n.id} n={n} onOpen={() => { void markRead(n.id); setOpen(false); navigate({ name: 'notifications' }) }} />)}
          </div>
          <button onClick={() => { setOpen(false); navigate({ name: 'notifications' }) }} className="mt-1 w-full rounded-xl py-2 text-sm font-semibold text-ink hover:bg-wash">Zobacz wszystkie</button>
        </div>
      )}
    </div>
  )
}

export function NotificationsPage() {
  const { fetchNotifications, notifVersion, unread, markRead, markAllRead } = useApp()
  const [f, setF] = useState<NotifFilter>('all')
  const [pageNumber, setPage] = useState(1)
  const [busy, setBusy] = useState(false)
  const list = useRef<HTMLDivElement>(null)
  useEffect(() => { list.current?.scrollTo({ top: 0 }) }, [f, pageNumber])
  const q = useRemote(() => fetchNotifications(f, pageNumber, 20), [f, pageNumber, notifVersion])
  const page = q.data ?? { items: [], totalCount: 0, pageNumber, pageSize: 20, totalPages: 1, hasPreviousPage: false, hasNextPage: false }
  const readAll = async () => {
    setBusy(true); await markAllRead(); setBusy(false)
  }
  return (
    <div className="mx-auto grid h-full min-h-0 max-w-4xl grid-rows-[auto_auto_minmax(0,1fr)_auto] gap-6">
      <PageTitle title="Powiadomienia" sub={unread ? `Nieprzeczytane: ${unread}` : 'Brak nieprzeczytanych powiadomień'} aside={<Btn variant="ghost" onClick={readAll} disabled={!unread || busy}>Oznacz wszystkie jako przeczytane</Btn>} />
      <Segmented className="justify-self-start" value={f} onChange={(v) => { setF(v); setPage(1) }} options={[{ v: 'all', label: 'Wszystkie' }, { v: 'unread', label: 'Nieprzeczytane' }, { v: 'read', label: 'Przeczytane' }]} />
      <Card className="flex min-h-0 flex-col overflow-hidden !p-2">
        <div ref={list} role="region" aria-label="Lista powiadomień" tabIndex={0} className="soft-scroll min-h-0 flex-1 overflow-y-auto overscroll-contain">
          {q.error !== null && describeError(q.error) && <p role="alert" className="m-2 rounded-xl bg-danger-soft p-3 text-sm text-danger">{describeError(q.error)} <button className="font-semibold underline" onClick={q.reload}>Spróbuj ponownie</button></p>}
          {page.items.map((n) => <Item key={n.id} n={n} links onOpen={() => void markRead(n.id)} />)}
          {q.loading && !page.items.length && <p className="p-8 text-center text-ink2">Ładowanie…</p>}
          {!q.loading && q.error === null && !page.items.length && <p className="p-8 text-center text-ink2">Brak powiadomień.</p>}
        </div>
        <div className="shrink-0"><Pager page={page} onPage={setPage} itemSummary={`Powiadomienia na stronie: ${page.items.length}`} /></div>
      </Card>
      <p className="text-xs text-ink2">Kliknięcie powiadomienia oznacza je jako przeczytane. Najnowsze powiadomienia znajdują się na górze listy.</p>
    </div>
  )
}
