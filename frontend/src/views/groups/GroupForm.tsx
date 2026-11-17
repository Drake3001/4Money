import { useState, type FormEvent } from 'react'
import { useRemote } from '../../api/hooks'
import { ArrowLeft } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import { MAX_DESCRIPTION, ME_ID, charCount } from '../../mocks/groups'
import { Btn, Card, Field, PageTitle, cx, inputCls } from '../../ui'
import { ErrorBox, IconPicker, type IconValue } from './GroupUi'

export default function GroupForm({ id }: { id?: string }) {
  const { navigate, showToast } = useApp()
  const groups = useGroups()
  const q = useRemote(() => groups.fetchGroup(id!), [id], !!id)
  const existing = q.data ?? undefined
  const [edited, setEdited] = useState<{ name?: string; description?: string; icon?: IconValue }>({})
  const name = edited.name ?? existing?.name ?? ''
  const description = edited.description ?? existing?.description ?? ''
  const icon: IconValue = edited.icon ?? { icon: null, file: null, previewUrl: null, removeIcon: false }
  const setName = (v: string) => setEdited((e) => ({ ...e, name: v }))
  const setDescription = (v: string) => setEdited((e) => ({ ...e, description: v }))
  const setIcon = (v: IconValue) => setEdited((e) => ({ ...e, icon: v }))
  const [err, setErr] = useState<number | null>(null)
  const [touched, setTouched] = useState(false)
  const [preparingIcon, setPreparingIcon] = useState(false)

  const len = charCount(description)
  const nameBad = !name.trim()
  const descBad = len > MAX_DESCRIPTION
  const back = () => navigate(id ? { name: 'group', id } : { name: 'groups' })

  if (id && !existing) return <div className="mx-auto max-w-2xl space-y-4"><PageTitle title="Edycja grupy" />{q.loading ? <p className="text-ink2">Wczytywanie grupy…</p> : <ErrorBox code={q.error ?? 404} />}</div>
  if (id && existing && existing.adminUserId !== ME_ID) return <div className="mx-auto max-w-2xl space-y-4"><PageTitle title="Edycja grupy" /><ErrorBox code={403} /><Btn variant="ghost" onClick={back}>Wróć do grupy</Btn></div>

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (preparingIcon) return
    setTouched(true); setErr(null)
    if (nameBad || descBad) return
    const input = { name: name.trim(), description, icon: icon.icon, iconFile: icon.file, removeIcon: icon.removeIcon }
    if (id) {
      const r = await groups.update(id, input)
      if (!r.ok) return setErr(r.code)
      showToast('Zapisano zmiany w grupie')
      navigate({ name: 'group', id })
    } else {
      const r = await groups.create(input)
      if (!r.ok) return setErr(r.code)
      showToast('Grupa została utworzona')
      navigate({ name: 'group', id: r.data.id })
    }
  }

  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <div>
        <button onClick={back} className="mb-2 inline-flex items-center gap-1.5 text-sm font-semibold text-ink2 hover:text-ink"><ArrowLeft size={16} />{id ? 'Grupa' : 'Grupy'}</button>
        <PageTitle title={id ? 'Edytuj grupę' : 'Nowa grupa'} />
      </div>
      <Card>
        <form onSubmit={submit} noValidate className="space-y-3">
          <Field label="Nazwa" error={touched && nameBad ? 'Nazwa nie może być pusta.' : undefined} reserveHint>
            <input value={name} onChange={(e) => setName(e.target.value)} aria-invalid={touched && nameBad} className={cx(inputCls, touched && nameBad && '!border-danger')} placeholder="np. Mieszkanie" />
          </Field>

          <div>
            <Field label="Opis (opcjonalnie)">
              <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} aria-invalid={descBad} className={cx(inputCls, descBad && '!border-danger')} />
            </Field>
            <div className="mt-1 flex h-7 items-start justify-between gap-3 text-xs">
              <span role={descBad ? 'alert' : undefined} className="text-danger">{descBad ? `Opis może mieć maksymalnie ${MAX_DESCRIPTION} znaków.` : ''}</span>
              <span className={cx('shrink-0', descBad ? 'text-danger' : 'text-ink2')}>{len} / {MAX_DESCRIPTION} znaków</span>
            </div>
          </div>

          <div>
            <p className="text-sm font-semibold">WALUTA: PLN</p>
            <p className="text-xs text-ink2">4Money obecnie obsługuje wyłącznie PLN.</p>
          </div>

          <IconPicker value={icon} onChange={setIcon} onPreparingChange={setPreparingIcon} existing={existing ? { id: existing.id, icon: existing.icon, hasCustomIcon: existing.hasCustomIcon } : undefined} />
          <ErrorBox code={err} />
          <div className="flex flex-wrap justify-end gap-2">
            <Btn variant="ghost" onClick={back}>Anuluj</Btn>
            <Btn type="submit" disabled={preparingIcon}>{id ? 'Zapisz zmiany' : 'Utwórz grupę'}</Btn>
          </div>
        </form>
      </Card>
    </div>
  )
}
