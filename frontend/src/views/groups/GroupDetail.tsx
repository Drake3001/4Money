import { useState } from 'react'
import { ArrowLeft, Copy, LogOut, Pencil, UserMinus } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import { useRemote } from '../../api/hooks'
import { ME_ID, invitationState, userLabel, type GroupData, type InvitationState, type Member, type MembershipFilter } from '../../mocks/groups'
import { money } from '../../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, Modal, PageTitle, Segmented, Table, Td, inputCls } from '../../ui'
import { ErrorBox, GroupIcon, dateOnly } from './GroupUi'
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
          ['Twoja rola', isAdmin ? <Badge key="r" tone="info">Administrator</Badge> : 'Członek'],
          ['Twoje członkostwo', active ? 'Aktywne' : 'Byłe'],
        ]} />
      </Card>
      {isAdmin && (
        <Card>
          <H>Zarządzanie grupą</H>
          <div className="flex flex-wrap gap-2">
            <Btn variant="ghost" onClick={() => navigate({ name: 'group-edit', id: g.id })}><Pencil size={16} />Edytuj grupę</Btn>
            <Btn variant="ghost" onClick={() => onDialog('transfer')}>Przekaż administratora</Btn>
            <Btn variant="danger" onClick={() => onDialog('archive')}>Archiwizuj grupę</Btn>
          </div>
          <p className="mt-3 text-xs text-ink2">Waluty grupy nie można zmienić po jej utworzeniu.</p>
        </Card>
      )}
      {active && (
        <Card>
          <H>Członkostwo</H>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="max-w-md text-sm text-ink2">{isAdmin ? 'Jako administrator nie możesz od razu opuścić grupy – najpierw przekaż administratora innemu aktywnemu członkowi.' : 'Po opuszczeniu grupy staniesz się jej byłym członkiem.'}</p>
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
                <Td><p className="num font-semibold">{userLabel(m.userId)}</p></Td>
                <Td>{m.userId === g.adminUserId ? <Badge tone="info">Administrator</Badge> : 'Członek'}</Td>
                <Td><Badge tone={m.status === 'active' ? 'ok' : 'neutral'}>{m.status === 'active' ? 'Aktywny' : 'Były'}</Badge></Td>
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
      <p className="text-xs text-ink2">Członkowie są identyfikowani przez ID użytkownika.</p>
    </div>
  )
}

const STATE_LABEL: Record<InvitationState, { label: string; tone: 'ok' | 'neutral' | 'warn' }> = {
  usable: { label: 'Można użyć', tone: 'ok' }, revoked: { label: 'Odwołane', tone: 'neutral' },
  expired: { label: 'Wygasło', tone: 'warn' }, exhausted: { label: 'Wykorzystane', tone: 'warn' },
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
  const copy = async () => {
    try { await navigator.clipboard.writeText(link); showToast('Skopiowano link zaproszenia') } catch { showToast('Nie udało się skopiować – zaznacz link ręcznie') }
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
            <p className="text-xs text-ink2">Minimum 1 użycie; data ważności musi być w przyszłości. Zaproszenia nie są wysyłane e-mailem – link przekazujesz samodzielnie.</p>
            <ErrorBox code={err} />
            <Btn type="submit">Utwórz zaproszenie</Btn>
          </div>
        </form>
      </Card>

      {created && (
        <Card className="!border-warn-bar/60 !bg-warn-soft/40">
          <H>Zaproszenie utworzone</H>
          <p className="mb-3 text-sm font-semibold text-warn">Token jest widoczny tylko teraz. Po zamknięciu tego okna nie zobaczysz go ponownie.</p>
          <p className="text-xs font-semibold uppercase tracking-wide text-ink2">Token</p>
          <p className="num mb-3 break-all rounded-lg bg-white px-3 py-2 text-sm">{created.token}</p>
          <p className="text-xs font-semibold uppercase tracking-wide text-ink2">Link zaproszenia</p>
          <p className="mb-4 break-all rounded-lg bg-white px-3 py-2 text-sm">{link}</p>
          <div className="flex flex-wrap gap-2">
            <Btn onClick={copy}><Copy size={16} />Kopiuj link zaproszenia</Btn>
            <Btn variant="ghost" onClick={() => setCreated(null)}>Zamknij</Btn>
          </div>
        </Card>
      )}

      <Card className="!p-2">
        <div className="px-3 pt-3"><H>Historia zaproszeń</H></div>
        {g.invitations.length === 0 ? <p className="p-8 text-center text-ink2">Brak zaproszeń.</p> : (
          <Table head={['Użycia', 'Ważne do', 'Status', 'Dostępność', '']}>
            {g.invitations.map((i) => {
              const st = invitationState(i)
              return (
                <tr key={i.id}>
                  <Td className="num">{i.useCount} / {i.maxUses}</Td>
                  <Td className="text-ink2">{when(i.expiresAt)}</Td>
                  <Td><Badge tone={i.isActive ? 'ok' : 'neutral'}>{i.isActive ? 'Aktywne' : 'Odwołane'}</Badge></Td>
                  <Td><Badge tone={STATE_LABEL[st].tone}>{STATE_LABEL[st].label}</Badge></Td>
                  <Td className="text-right">{i.isActive && <Btn variant="danger" className="!min-h-8 !px-3 !py-1" onClick={() => revoke(i.id)}>Odwołaj zaproszenie</Btn>}</Td>
                </tr>
              )
            })}
          </Table>
        )}
      </Card>
      <p className="text-xs text-ink2">Zaproszenie jest aktywne dla nowego członka tylko wtedy, gdy nie zostało odwołane, nie wygasło i nie wyczerpano limitu użyć. Tokenów nie można ponownie wyświetlić.</p>
    </div>
  )
}

function MemberModal({ m, onClose }: { m: Member; onClose: () => void }) {
  return (
    <Modal title="Szczegóły członka" onClose={onClose}>
      <KeyValue rows={[
        ['Identyfikator użytkownika', <span key="u" className="num break-all">{m.userId}</span>], ['Status', m.status === 'active' ? 'Aktywny' : 'Były'], ['Dołączył(a)', when(m.joinedAt)],
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
              <Badge tone={active ? 'ok' : 'neutral'}>{active ? 'Aktywny członek' : 'Były członek'}</Badge>
              {isAdmin && <Badge tone="info">Administrator</Badge>}
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
          <p className="mb-4 text-sm text-ink2">Zarchiwizowana grupa znika z normalnych widoków grup. To nie jest trwałe usunięcie, ale w tej wersji nie ma możliwości przywrócenia grupy.</p>
          <ErrorBox code={err} />
          <div className="mt-5 flex justify-end gap-2">
            <Btn variant="ghost" onClick={close}>Anuluj</Btn>
            <Btn variant="danger" onClick={async () => { const r = await groups.archive(g.id); r.ok ? done('Grupa została zarchiwizowana', true) : setErr(r.code) }}>Archiwizuj grupę</Btn>
          </div>
        </Modal>
      )}

      {dialogType === 'leave' && (
        <Modal title={isAdmin ? 'Nie możesz jeszcze opuścić grupy' : 'Opuścić grupę?'} onClose={close}>
          {isAdmin ? (
            <>
              <p className="mb-4 text-sm text-ink2">Jesteś administratorem tej grupy. Zanim ją opuścisz, przekaż administratora innemu aktywnemu członkowi.</p>
              <div className="flex justify-end gap-2">
                <Btn variant="ghost" onClick={close}>Zamknij</Btn>
                <Btn onClick={() => setDialog('transfer')}>Przekaż administratora</Btn>
              </div>
            </>
          ) : (
            <>
              <p className="mb-4 text-sm text-ink2">Staniesz się byłym członkiem grupy i stracisz dostęp do jej bieżących widoków aktywnych grup. Wrócisz do niej tylko z ważnym zaproszeniem.</p>
              <ErrorBox code={err} />
              <div className="mt-5 flex justify-end gap-2">
                <Btn variant="ghost" onClick={close}>Anuluj</Btn>
                <Btn variant="danger" onClick={async () => { const r = await groups.leave(g.id); r.ok ? done('Opuściłeś grupę', true) : setErr(r.code) }}>Opuść grupę</Btn>
              </div>
            </>
          )}
        </Modal>
      )}

      {dialogType === 'transfer' && (
        <Modal title="Przekaż administratora" onClose={close}>
          {candidates.length === 0 ? (
            <p className="mb-4 text-sm text-ink2">W grupie nie ma innego aktywnego członka, któremu można przekazać administratora.</p>
          ) : (
            <>
              <p className="mb-4 text-sm text-ink2">Wybierz aktywnego członka. Po przekazaniu zostaniesz zwykłym członkiem grupy. Nie można wybrać siebie ani byłego członka.</p>
              <Field label="Nowy administrator">
                <select value={target} onChange={(e) => setTarget(e.target.value)} className={inputCls}>
                  <option value="">Wybierz członka…</option>
                  {candidates.map((m) => <option key={m.userId} value={m.userId}>{userLabel(m.userId)}</option>)}
                </select>
              </Field>
            </>
          )}
          <div className="mt-3"><ErrorBox code={err} /></div>
          <div className="mt-5 flex justify-end gap-2">
            <Btn variant="ghost" onClick={close}>Anuluj</Btn>
            <Btn disabled={!target} onClick={async () => { const r = await groups.transferAdmin(g.id, target); r.ok ? done(`${userLabel(target)} jest teraz administratorem`) : setErr(r.code) }}>Przekaż administratora</Btn>
          </div>
        </Modal>
      )}

      {removeId && (
        <Modal title="Usunąć z grupy?" onClose={close}>
          <p className="mb-4 text-sm text-ink2">{userLabel(removeId)} zostanie usunięty(a) z grupy i stanie się byłym członkiem. To nie jest blokada – osoba może dołączyć ponownie z ważnym zaproszeniem.</p>
          <ErrorBox code={err} />
          <div className="mt-5 flex justify-end gap-2">
            <Btn variant="ghost" onClick={close}>Anuluj</Btn>
            <Btn variant="danger" onClick={async () => { const r = await groups.removeMember(g.id, removeId); r.ok ? done('Usunięto z grupy') : setErr(r.code) }}>Usuń z grupy</Btn>
          </div>
        </Modal>
      )}

      {memberId && g.members.find((m) => m.userId === memberId) && <MemberModal m={g.members.find((m) => m.userId === memberId)!} onClose={close} />}
    </div>
  )
}
