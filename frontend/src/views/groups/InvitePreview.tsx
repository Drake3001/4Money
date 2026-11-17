import { useEffect, useState } from 'react'
import { ArrowLeft, Check } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import type { InvitationPreview } from '../../api/types'
import { Badge, Btn, Card, Field, KeyValue, PageTitle, inputCls } from '../../ui'
import { ErrorBox, GroupIcon } from './GroupUi'

export default function InvitePreview({ token: initial }: { token?: string }) {
  const { navigate, showToast } = useApp()
  const groups = useGroups()
  const [token, setToken] = useState(initial ?? '')
  const [preview, setPreview] = useState<InvitationPreview | null>(null)
  const [err, setErr] = useState<number | null>(null)

  const run = async (t: string) => {
    setErr(null); setPreview(null)
    if (!t.trim()) return setErr(400)
    const r = await groups.previewInvitation(t)
    if (r.ok) setPreview(r.data); else setErr(r.code)
  }
  // Arriving with a token from an invitation link previews it right away.
  useEffect(() => { if (initial) run(initial) }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const accept = async () => {
    const r = await groups.acceptInvitation(token)
    if (!r.ok) return setErr(r.code)
    showToast('Dołączono do grupy')
    navigate({ name: 'group', id: r.data.groupId })
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div>
        <button onClick={() => navigate({ name: 'groups' })} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />Grupy</button>
        <PageTitle title="Zaproszenie do grupy" sub="Wklej kod i dołącz do grupy" />
      </div>
      <Card>
        <form onSubmit={(e) => { e.preventDefault(); run(token) }} className="space-y-4" noValidate>
          <Field label="Kod zaproszenia" hint="Kod otrzymasz od administratora grupy.">
            <input value={token} onChange={(e) => setToken(e.target.value)} className={inputCls} placeholder="Wklej kod" />
          </Field>
          <Btn type="submit" variant={preview ? 'ghost' : 'primary'}>Dołącz do grupy</Btn>
        </form>
      </Card>
      <ErrorBox code={err} />
      {preview && (
        <Card>
          <div className="mb-4 flex items-center gap-4">
            <GroupIcon group={{ icon: preview.icon, previewUrl: preview.image?.url }} size={56} />
            <div className="min-w-0">
              <h2 className="font-display text-xl font-bold">{preview.name}</h2>
              <p className="text-sm text-ink2">{preview.description ?? 'Brak opisu'}</p>
            </div>
          </div>
          <KeyValue rows={[
            ['Waluta', <Badge key="c">{preview.currencyCode}</Badge>],
            ['Ważne do', new Date(preview.expiresAt).toLocaleString('pl-PL', { dateStyle: 'medium', timeStyle: 'short' })],
            ['Pozostałe użycia', preview.remainingUses],
          ]} />
          <div className="mt-5 flex justify-end"><Btn onClick={accept}><Check size={16} />Dołącz do grupy</Btn></div>
        </Card>
      )}
    </div>
  )
}
