import { useState } from 'react'
import { ArrowDown, ArrowUp, ChevronRight, KeyRound, Plus } from 'lucide-react'
import { useApp } from '../../context'
import { useRemote } from '../../api/hooks'
import { groupsApi } from '../../api/groups'
import { ErrorBox, GroupIcon, GroupMembershipBadge } from './GroupUi'
import { useGroups } from '../../groupsContext'
import { ME_ID, type GroupSortBy, type MembershipFilter } from '../../mocks/groups'
import { Badge, Btn, Card, Field, PageTitle, inputCls } from '../../ui'

function MemberCount({ id }: { id: string }) {
  const groups = useGroups()
  const q = useRemote(() => groupsApi.memberCount(id), [id, groups.rev])
  return <p className="mt-2 text-center text-xs text-ink2" title={q.error !== null ? 'Nie udało się pobrać liczby członków.' : undefined}>Członkowie: {q.error !== null ? '—' : q.data ?? '…'}</p>
}

export default function GroupsList() {
  const { navigate } = useApp()
  const groups = useGroups()
  const [filter, setFilter] = useState<MembershipFilter>('active')
  const [sortBy, setSortBy] = useState<GroupSortBy>('name')
  const [descending, setDescending] = useState(false)
  const [name, setName] = useState('')
  const q = useRemote(() => groups.fetchList(filter, descending, sortBy), [filter, sortBy, descending, groups.rev])
  const search = name.trim().toLocaleLowerCase('pl')
  const items = (q.data ?? []).filter(({ group }) => group.name.toLocaleLowerCase('pl').includes(search))
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <PageTitle
        title="Grupy"
        sub="Twoje grupy i wspólne wydatki"
        aside={
          <div className="flex flex-wrap gap-2">
            <Btn variant="ghost" onClick={() => navigate({ name: 'invite' })}><KeyRound size={16} />Mam zaproszenie</Btn>
            <Btn onClick={() => navigate({ name: 'group-new' })}><Plus size={16} />Nowa grupa</Btn>
          </div>
        }
      />
      <div className="flex flex-wrap items-center justify-between gap-3">
        <label className="flex max-w-full items-center gap-2 text-sm text-ink2">
          <span className="text-xs font-semibold uppercase tracking-wide">FILTROWANIE</span>
          <select value={filter} onChange={(e) => setFilter(e.target.value as MembershipFilter)} className={`${inputCls} !w-auto`}><option value="active">Aktywne</option><option value="former">Byłe</option><option value="all">Wszystkie</option></select>
        </label>
        <div className="flex max-w-full flex-wrap items-center gap-2">
          <label className="flex max-w-full items-center gap-2 text-sm text-ink2">
            <span className="text-xs font-semibold uppercase tracking-wide">SORTOWANIE</span>
            <select className={`${inputCls} max-w-full`} style={{ width: 'auto' }} value={sortBy} onChange={(e) => setSortBy(e.target.value === 'joinedAt' ? 'joinedAt' : 'name')}>
              <option value="name">Nazwa</option>
              <option value="joinedAt">Data dołączenia</option>
            </select>
          </label>
          <button type="button" onClick={() => setDescending((value) => !value)}
            title={descending ? 'Malejąco' : 'Rosnąco'} aria-label={descending ? 'Sortowanie malejąco. Zmień na rosnąco' : 'Sortowanie rosnąco. Zmień na malejąco'}
            className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-ink transition hover:bg-wash">
            {descending ? <ArrowDown size={18} /> : <ArrowUp size={18} />}
          </button>
        </div>
      </div>

      <Field label="Nazwa grupy"><input type="search" placeholder="Szukaj grupy po nazwie…" value={name} onChange={(e) => setName(e.target.value)} className={inputCls} /></Field>
      {q.error !== null ? <ErrorBox code={q.error} /> : q.loading && !q.data ? <Card className="py-12 text-center text-ink2">Wczytywanie grup…</Card> : items.length === 0 ? (
        <Card className="py-12 text-center text-ink2">
          {search ? 'Brak grup dla podanej nazwy.' : filter === 'former' ? 'Nie jesteś byłym członkiem żadnej grupy.' : filter === 'all' ? 'Brak grup.' : 'Nie jesteś aktywnym członkiem żadnej grupy.'}
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {items.map(({ group: g, myStatus }) => {
            const body = (
              <>
                <div className="flex shrink-0 flex-col items-center">
                  <GroupIcon group={g} size={48} />
                  {myStatus === 'active' && <MemberCount id={g.id} />}
                </div>
                <div className="min-w-0 flex-1">
                  <h3 className="font-display text-lg font-bold">{g.name}</h3>
                  <p className="mt-0.5 line-clamp-2 text-sm text-ink2">{g.description ?? 'Brak opisu'}</p>
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    <Badge>{g.currencyCode}</Badge>
                    <GroupMembershipBadge active={myStatus === 'active'}>{myStatus === 'active' ? 'Aktywny członek' : 'Były członek'}</GroupMembershipBadge>
                    {g.adminUserId === ME_ID && myStatus === 'active' && <Badge tone="info">Administrator</Badge>}
                  </div>
                  {myStatus === 'former' && <p className="mt-2 text-xs text-ink2">Nie jesteś już członkiem tej grupy - szczegóły i lista członków są niedostępne.</p>}
                </div>
              </>
            )
            // Former members only see the historical entry; details and members are not accessible.
            return myStatus === 'active' ? (
              <button key={g.id} onClick={() => navigate({ name: 'group', id: g.id })} className="flex items-start gap-4 rounded-2xl border border-line bg-white p-5 text-left transition hover:border-ink/40">
                {body}<ChevronRight size={18} className="mt-1 shrink-0 text-ink2/60" />
              </button>
            ) : (
              <div key={g.id} className="flex items-start gap-4 rounded-2xl border border-line bg-wash/60 p-5">{body}</div>
            )
          })}
        </div>
      )}
      <p className="text-xs text-ink2">Zarchiwizowane grupy nie są wyświetlane na liście.</p>
    </div>
  )
}
