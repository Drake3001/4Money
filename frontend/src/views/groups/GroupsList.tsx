import { useState } from 'react'
import { ChevronRight, KeyRound, Plus } from 'lucide-react'
import { useApp } from '../../context'
import { useRemote } from '../../api/hooks'
import { ErrorBox, GroupIcon } from './GroupUi'
import { useGroups } from '../../groupsContext'
import { ME_ID, type MembershipFilter } from '../../mocks/groups'
import { Badge, Btn, Card, PageTitle, Segmented } from '../../ui'

export default function GroupsList() {
  const { navigate } = useApp()
  const groups = useGroups()
  const [filter, setFilter] = useState<MembershipFilter>('active')
  const q = useRemote(() => groups.fetchList(filter), [filter, groups.rev])
  const items = q.data ?? []
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <PageTitle
        title="Grupy"
        sub="Twoje grupy i członkostwo w nich. Waluta wszystkich grup to PLN."
        aside={
          <div className="flex flex-wrap gap-2">
            <Btn variant="ghost" onClick={() => navigate({ name: 'invite' })}><KeyRound size={16} />Mam zaproszenie</Btn>
            <Btn onClick={() => navigate({ name: 'group-new' })}><Plus size={16} />Nowa grupa</Btn>
          </div>
        }
      />
      <Segmented<MembershipFilter> value={filter} onChange={setFilter} options={[{ v: 'active', label: 'Aktywne' }, { v: 'former', label: 'Byłe' }, { v: 'all', label: 'Wszystkie' }]} />

      {q.error !== null ? <ErrorBox code={q.error} /> : q.loading && !q.data ? <Card className="py-12 text-center text-ink2">Wczytywanie grup…</Card> : items.length === 0 ? (
        <Card className="py-12 text-center text-ink2">
          {filter === 'former' ? 'Nie jesteś byłym członkiem żadnej grupy.' : 'Nie masz jeszcze żadnych grup.'}
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {items.map(({ group: g, myStatus }) => {
            const body = (
              <>
                <GroupIcon group={g} size={48} />
                <div className="min-w-0 flex-1">
                  <h3 className="font-display text-lg font-bold">{g.name}</h3>
                  <p className="mt-0.5 line-clamp-2 text-sm text-ink2">{g.description ?? 'Brak opisu'}</p>
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    <Badge>{g.currencyCode}</Badge>
                    <Badge tone={myStatus === 'active' ? 'ok' : 'neutral'}>{myStatus === 'active' ? 'Aktywny członek' : 'Były członek'}</Badge>
                    {g.adminUserId === ME_ID && myStatus === 'active' && <Badge tone="info">Administrator</Badge>}
                  </div>
                  {myStatus === 'former' && <p className="mt-2 text-xs text-ink2">Nie jesteś już członkiem tej grupy – szczegóły i lista członków są niedostępne.</p>}
                </div>
              </>
            )
            // Former members only see the historical entry; details and members are not accessible.
            return myStatus === 'active' ? (
              <button key={g.id} onClick={() => navigate({ name: 'group', id: g.id })} className="flex items-start gap-4 rounded-2xl border border-line bg-white p-5 text-left transition hover:border-personal/40">
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
