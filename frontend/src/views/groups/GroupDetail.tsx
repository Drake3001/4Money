import { useState } from 'react'
import { ArrowLeft, Copy, LogOut, Pencil, UserMinus } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import { useRemote } from '../../api/hooks'
import { ME_ID, invitationState, userLabel, type GroupData, type InvitationState, type Member, type MembershipFilter } from '../../mocks/groups'
import { money } from '../../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, Modal, PageTitle, Segmented, Table, Td, inputCls } from '../../ui'
import { ErrorBox, GroupIcon, GroupMembershipBadge, GroupRoleBadge, dateOnly } from './GroupUi'
import SettlementsTab from './Settlements'

const when = (iso: string) => new Date(iso).toLocaleString('pl-PL', { dateStyle: 'medium', timeStyle: 'short' })

type Dialog = 'archive' | 'leave' | 'transfer' | { remove: string } | { member: string } | null

function InfoTab({ g, isAdmin, active, onDialog }: { g: GroupData; isAdmin: boolean; active: boolean; onDialog: (d: Dialog) => void }) {
  const { navigate } = useApp()
  return (
    <div className="space-y-6">
      <Card>
        <KeyValue rows={[
          ['Nazwa', g.name],
          ['Opis', g.description ?? 'Brak opisu'],
          ['Waluta', <Badge key="c">{g.currencyCode}</Badge>],
          ['Twoja rola', <GroupRoleBadge key="r" isAdmin={isAdmin} />],
          ['Twoje członkostwo', <GroupMembershipBadge key="m" active={active} />],
          ['Członkowie', g.members.filter((m) => m.status === 'active').length],
        ]} />
      </Card>
      {isAdmin && (
        <Card>
          <H>Zarządzanie grupą</H>
          <div className="flex flex-wrap gap-2">
            <Btn variant="ghost" onClick={() => navigate({ name: 'group-edit', id: g.id })}><Pencil size={16} />Edytuj grupę</Btn>
            <Btn variant="ghost" onClick={() => onDialog('transfer')}>Przekaż rolę administratora</Btn>
            <Btn variant="danger" onClick={() => onDialog('archive')}>Archiwizuj grupę</Btn>
          </div>
        </Card>
      )}
      {active && (
        <Card>
          <H>Twoje członkostwo</H>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="max-w-md text-sm text-ink2">{isAdmin ? 'Nie możesz opuścić grupy będąc jej administratorem. Najpierw przekaż rolę administratora innemu aktywnemu członkowi grupy.' : 'Jesteś aktywnym członkiem grupy. Po opuszczeniu grupy staniesz się byłym członkiem grupy.'}</p>
            <Btn variant="danger" onClick={() => onDialog('leave')}><LogOut size={16} />Opuść grupę</Btn>
          </div>
        </Card>
      )}
    </div>
  )
}

function MembersTab({ g, isAdmin, onDialog }: { g: GroupData; isAdmin: boolean; onDialog: (d: Dialog) => void }) {
  const [filter, setFilter] = useState<MembershipFilter>('active')
  const rows = g.members.filter((m) => filter === 'all' || m.status === filter)
  return (
    <div className="space-y-4">
      <Segmented<MembershipFilter> value={filter} onChange={setFilter} options={[{ v: 'active', label: 'Aktywni' }, { v: 'former', label: 'Byli' }, { v: 'all', label: 'Wszyscy' }]} />
      <Card className="!p-2">
        {rows.length === 0 ? <p className="p-8 text-center text-ink2">Brak członków w tym widoku.</p> : (
          <Table head={['Członek', 'Rola', 'Status', 'Dołączył(a)', '']}>
            {rows.map((m) => (
              <tr key={m.userId}>
                <Td><p className="font-semibold">{userLabel(m.userId, m.displayName)}</p></Td>
                <Td><GroupRoleBadge isAdmin={m.userId === g.adminUserId} /></Td>
                <Td><GroupMembershipBadge active={m.status === 'active'}>{m.status === 'active' ? 'Aktywny' : 'Były'}</GroupMembershipBadge></Td>
                <Td className="text-ink2">{dateOnly(m.joinedAt)}</Td>
                <Td className="text-right">
                  <div className="flex justify-end gap-2">
                    <Btn variant="soft" className="!min-h-8 !px-3 !py-1" onClick={() => onDialog({ member: m.userId })}>Szczegóły</Btn>
                    {isAdmin && m.status === 'active' && m.userId !== g.adminUserId && <Btn variant="danger" className="!min-h-8 !px-3 !py-1" onClick={() => onDialog({ remove: m.userId })}><UserMinus size={14} />Usuń z grupy</Btn>}
                  </div>
                </Td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </div>
  )
}

const STATE_LABEL: Record<InvitationState, { label: string; tone: 'ok' | 'neutral' | 'warn' }> = {
  usable: { label: 'Aktywne', tone: 'ok' }, revoked: { label: 'Odwołane', tone: 'neutral' },
  expired: { label: 'Wygasłe', tone: 'warn' }, exhausted: { label: 'Wykorzystane', tone: 'warn' },
}

function InvitationsTab({ g }: { g: GroupData }) {
  const { showToast } = useApp()
  const groups = useGroups()
  const tomorrow = new Date(Date.now() + 7 * 864e5)
  const local = (d: Date) => new Date(d.getTime() - d.getTimezoneOffset() * 6e4).toISOString().slice(0, 16)
  const [maxUses, setMaxUses] = useState('5')
  const [expiresAt, setExpiresAt] = useState(local(tomorrow))
  const [err, setErr] = useState<number | null>(null)
  const [created, setCreated] = useState<{ token: string } | null>(null)

  const link = created ? `${window.location.origin}/?token=${created.token}` : ''
  const create = async (e: React.FormEvent) => {
    e.preventDefault(); setErr(null)
    const r = await groups.createInvitation(g.id, { maxUses: Number(maxUses), expiresAt: expiresAt ? new Date(expiresAt).toISOString() : '' })
    if (!r.ok) return setErr(r.code)
    setCreated({ token: r.data.token })
  }
  const copy = async (kind: 'link' | 'code') => {
    if (!created) return
    const label = kind === 'link' ? 'link' : 'kod'
    try { await navigator.clipboard.writeText(kind === 'link' ? link : created.token); showToast(`Skopiowano ${label} zaproszenia`) } catch { showToast(`Nie udało się skopiować - zaznacz ${label} ręcznie`) }
  }
  const revoke = async (id: string) => {
    const r = await groups.revokeInvitation(g.id, id)
    if (!r.ok) return setErr(r.code)
    showToast('Zaproszenie odwołane')
  }

  return (
    <div className="space-y-6">
      <Card>
        <H>Nowe zaproszenie</H>
        <form onSubmit={create} noValidate className="grid gap-4 sm:grid-cols-2">
          <Field label="Maksymalna liczba użyć"><input type="number" min={1} step={1} value={maxUses} onChange={(e) => setMaxUses(e.target.value)} className={inputCls} /></Field>
          <Field label="Data ważności"><input type="datetime-local" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} className={inputCls} /></Field>
          <div className="sm:col-span-2 space-y-3">
            <p className="text-xs text-ink2">Minimum 1 użycie. Zaproszenia nie są wysyłane na adres e-mail - link lub kod przekazujesz samodzielnie.</p>
            <ErrorBox code={err} />
            <Btn type="submit">Utwórz zaproszenie</Btn>
          </div>
        </form>
      </Card>

      {created && (
        <Card className="!border-warn-bar/60 !bg-warn-soft/40">
          <H>Zaproszenie utworzone</H>
          <p className="mb-3 text-sm font-semibold text-warn">Kod jest widoczny tylko teraz. Po zamknięciu tego okna nie zobaczysz go ponownie.</p>
          <p className="text-xs font-semibold uppercase tracking-wide text-ink2">Kod</p>
          <p className="num mb-3 break-all rounded-lg bg-white px-3 py-2 text-sm">{created.token}</p>
          <p className="text-xs font-semibold uppercase tracking-wide text-ink2">Link zaproszenia</p>
          <p className="mb-4 break-all rounded-lg bg-white px-3 py-2 text-sm">{link}</p>
          <div className="flex flex-wrap gap-2">
            <Btn onClick={() => copy('link')}><Copy size={16} />Kopiuj link zaproszenia</Btn>
            <Btn onClick={() => copy('code')}><Copy size={16} />Kopiuj kod</Btn>
            <Btn variant="ghost" onClick={() => setCreated(null)}>Zamknij</Btn>
          </div>
        </Card>
      )}

      <Card className="!p-2">
        <div className="px-3 pt-3"><H>Historia zaproszeń</H></div>
        {g.invitations.length === 0 ? <p className="p-8 text-center text-ink2">Brak zaproszeń.</p> : (
          <Table head={['Użycia', 'Ważne do', 'Status', '']}>
            {g.invitations.map((i) => {
              const st = invitationState(i)
              return (
                <tr key={i.id}>
                  <Td className="num">{i.useCount} / {i.maxUses}</Td>
                  <Td className="text-ink2">{when(i.expiresAt)}</Td>
                  <Td><Badge tone={STATE_LABEL[st].tone}>{STATE_LABEL[st].label}</Badge></Td>
                  <Td className="text-right">{i.isActive && <Btn variant="danger" className="!min-h-8 !px-3 !py-1" onClick={() => revoke(i.id)}>Odwołaj zaproszenie</Btn>}</Td>
                </tr>
              )
            })}
          </Table>
        )}
      </Card>
      <p className="text-xs text-ink2">Zaproszenie jest aktywne dla nowego członka wtedy, gdy nie zostało odwołane, nie wygasło i nie wyczerpano limitu użyć. Kodów nie można ponownie wyświetlić.</p>
    </div>
  )
}

function MemberModal({ m, isAdmin, onClose }: { m: Member; isAdmin: boolean; onClose: () => void }) {
  return (
    <Modal title="Szczegóły członka" onClose={onClose}>
      <KeyValue rows={[
        ['Imię i nazwisko', userLabel(m.userId, m.displayName)],
        ['Rola', <GroupRoleBadge key="r" isAdmin={isAdmin} />],
        ['Status', <GroupMembershipBadge key="s" active={m.status === 'active'}>{m.status === 'active' ? 'Aktywny' : 'Były'}</GroupMembershipBadge>], ['Dołączył(a)', when(m.joinedAt)],
        ['Suma wpłat', money(m.totalPaid)], ['Suma udziałów', money(m.totalShare)],
      ]} />
      <div className="mt-5 flex justify-end"><Btn variant="ghost" onClick={onClose}>Zamknij</Btn></div>
    </Modal>
  )
}

export default function GroupDetail({ id, tab = 'info' }: { id: string; tab?: string }) {
  const { navigate, showToast } = useApp()
  const groups = useGroups()
  const q = useRemote(() => groups.fetchGroup(id), [id, groups.rev])
  const g = q.data
  const [dialog, setDialog] = useState<Dialog>(null)
  const [err, setErr] = useState<number | null>(null)
  const [target, setTarget] = useState('')

  if (!g && q.loading) return <div className="mx-auto max-w-2xl py-12 text-center text-ink2">Wczytywanie grupy…</div>
  if (!g) {
    return (
      <div className="mx-auto max-w-2xl space-y-4">
        <PageTitle title="Grupa" />
        <ErrorBox code={q.error ?? 404} />
        <Btn variant="ghost" onClick={() => navigate({ name: 'groups' })}>Wróć do grup</Btn>
        </div>
    )
  }

  // A former member (or non-member) may not open group details or the member list.
  if (g.members.find((m) => m.userId === ME_ID)?.status !== 'active') {
    return (
      <div className="mx-auto max-w-2xl space-y-4">
        <PageTitle title="Grupa" />
        <div role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">Nie jesteś już członkiem tej grupy, więc jej szczegóły i lista członków są niedostępne. Na liście grup nadal widać ją w historii członkostwa.</div>
        <Btn variant="ghost" onClick={() => navigate({ name: 'groups' })}>Wróć do grup</Btn>
      </div>
    )
  }
  const isAdmin = g.adminUserId === ME_ID
  const mine = g.members.find((m) => m.userId === ME_ID)
  const active = mine?.status === 'active'
  const current = tab === 'invitations' && !isAdmin ? 'info' : tab
  const close = () => { setDialog(null); setErr(null); setTarget('') }
  const done = (msg: string, goList = false) => { close(); showToast(msg); if (goList) navigate({ name: 'groups' }) }

  const candidates = g.members.filter((m) => m.status === 'active' && m.userId !== ME_ID)
  const dialogType = typeof dialog === 'string' ? dialog : null
  const removeId = dialog && typeof dialog === 'object' && 'remove' in dialog ? dialog.remove : null
  const memberId = dialog && typeof dialog === 'object' && 'member' in dialog ? dialog.member : null

  const tabs = [{ v: 'info', label: 'Informacje' }, { v: 'members', label: 'Członkowie' }, { v: 'settlements', label: 'Rozliczenia' }, ...(isAdmin ? [{ v: 'invitations', label: 'Zaproszenia' }] : [])]

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div>
        <button onClick={() => navigate({ name: 'groups' })} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />Grupy</button>
        <div className="flex items-center gap-4">
          <GroupIcon group={g} size={64} />
          <div className="min-w-0">
            <h1 className="font-display text-2xl font-bold">{g.name}</h1>
            <p className="mt-0.5 text-ink2">{g.description ?? 'Brak opisu'}</p>
            <div className="mt-2 flex flex-wrap gap-1.5">
              <Badge>{g.currencyCode}</Badge>
              <GroupMembershipBadge active={!!active}>{active ? 'Aktywny członek' : 'Były członek'}</GroupMembershipBadge>
              <GroupRoleBadge isAdmin={isAdmin} />
            </div>
          </div>
        </div>
      </div>

      <Segmented value={current} onChange={(v) => navigate({ name: 'group', id, tab: v })} options={tabs} />

      {current === 'info' && <InfoTab g={g} isAdmin={isAdmin} active={!!active} onDialog={(d) => { setErr(null); setDialog(d) }} />}
      {current === 'members' && <MembersTab g={g} isAdmin={isAdmin} onDialog={(d) => { setErr(null); setDialog(d) }} />}
      {current === 'settlements' && <SettlementsTab g={g} isAdmin={isAdmin} />}
      {current === 'invitations' && isAdmin && <InvitationsTab g={g} />}

      {dialogType === 'archive' && (
        <Modal title="Archiwizować grupę?" onClose={close}>
          <p className="mb-4 text-sm text-ink2">Zarchiwizowana grupa nie będzie widoczna na liście grup. Jej wydatki nie zostaną usunięte i nadal będą uwzględniane w podsumowaniach.</p>
          <ErrorBox code={err} />
          <div className="mt-3 flex justify-end gap-2">
            <Btn variant="ghost" onClick={close}>Anuluj</Btn>
            <Btn variant="danger" onClick={async () => { const r = await groups.archive(g.id); r.ok ? done('Grupa została zarchiwizowana', true) : setErr(r.code) }}>Archiwizuj grupę</Btn>
          </div>
        </Modal>
      )}

      {dialogType === 'leave' && (
        <Modal title={isAdmin ? 'Nie możesz opuścić grupy' : 'Opuścić grupę?'} onClose={close}>
          {isAdmin ? (
            <>
              <p className="mb-4 text-sm text-ink2">Najpierw przekaż rolę administratora innemu aktywnemu członkowi grupy.</p>
              <div className="flex justify-end gap-2">
                <Btn variant="ghost" onClick={close}>Anuluj</Btn>
                <Btn onClick={() => setDialog('transfer')}>Przekaż rolę administratora</Btn>
              </div>
            </>
          ) : (
            <>
              <p className="mb-4 text-sm text-ink2">Po opuszczeniu grupy staniesz się byłym członkiem grupy. Stracisz dostęp do aktywności w grupie do momentu ponownego dołączenia z ważnym zaproszeniem.</p>
              <ErrorBox code={err} />
              <div className="mt-3 flex justify-end gap-2">
                <Btn variant="ghost" onClick={close}>Anuluj</Btn>
                <Btn variant="danger" onClick={async () => { const r = await groups.leave(g.id); r.ok ? done('Opuściłeś grupę', true) : setErr(r.code) }}>Opuść grupę</Btn>
              </div>
            </>
          )}
        </Modal>
      )}

      {dialogType === 'transfer' && (
        <Modal title="Przekaż rolę administratora" onClose={close}>
          {candidates.length === 0 ? (
            <p className="mb-4 text-sm text-ink2">W grupie nie ma innego aktywnego członka, któremu można przekazać rolę administratora.</p>
          ) : (
            <>
              <p className="mb-4 text-sm text-ink2">Wybierz aktywnego członka. Po przekazaniu zostaniesz zwykłym członkiem grupy. Nie możesz wybrać siebie ani byłego członka grupy.</p>
              <Field label="Nowy administrator">
                <select value={target} onChange={(e) => setTarget(e.target.value)} className={inputCls}>
                  <option value="">Wybierz członka…</option>
                  {candidates.map((m) => <option key={m.userId} value={m.userId}>{userLabel(m.userId)}</option>)}
                </select>
              </Field>
            </>
          )}
          <div className="mt-3"><ErrorBox code={err} /></div>
          <div className="mt-3 flex justify-end gap-2">
            <Btn variant="ghost" onClick={close}>Anuluj</Btn>
            <Btn disabled={!target} onClick={async () => { const r = await groups.transferAdmin(g.id, target); r.ok ? done(`${userLabel(target)} jest teraz administratorem`) : setErr(r.code) }}>Przekaż</Btn>
          </div>
        </Modal>
      )}

      {removeId && (
        <Modal title="Usunąć z grupy?" onClose={close}>
          <p className="mb-4 text-sm text-ink2">{userLabel(removeId)} zostanie usunięty(a) z grupy i stanie się byłym członkiem. To nie jest blokada - osoba może dołączyć ponownie z ważnym zaproszeniem.</p>
          <ErrorBox code={err} />
          <div className="mt-3 flex justify-end gap-2">
            <Btn variant="ghost" onClick={close}>Anuluj</Btn>
            <Btn variant="danger" onClick={async () => { const r = await groups.removeMember(g.id, removeId); r.ok ? done('Usunięto z grupy') : setErr(r.code) }}>Usuń z grupy</Btn>
          </div>
        </Modal>
      )}

      {memberId && g.members.find((m) => m.userId === memberId) && <MemberModal m={g.members.find((m) => m.userId === memberId)!} isAdmin={memberId === g.adminUserId} onClose={close} />}
    </div>
  )
}
