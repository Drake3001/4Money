import { useState, type FormEvent } from 'react'
import { LogOut, Pencil } from 'lucide-react'
import { useApp } from '../context'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { PROFILE_ICONS, USER_REASON, validateEmail, validateProfile, type Problems } from '../mocks/users'
import { dateText, dayText } from '../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, Modal, PageTitle, cx, inputCls } from '../ui'
import { ICON_LABELS, ProfileIcon } from './ProfileIcon'

const PROFILE_TEXT: Record<number, string> = {
  400: 'Dane są nieprawidłowe. Sprawdź formularz i, przy zmianie e-maila, aktualne hasło.',
  409: 'Ten login lub adres e-mail jest już zajęty.',
  429: 'Zbyt wiele prób. Spróbuj ponownie za chwilę.',
}
const errText = (r: UserRes) => (r.ok ? null : (r.reason && USER_REASON[r.reason]) || describeError(r.code, PROFILE_TEXT))
const Err = ({ children }: { children?: string | null }) => (children ? <p className="mt-1 text-xs text-danger">{children}</p> : null)

function EditProfile({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const { showToast } = useApp()
  const p = user.profile
  const [f, setF] = useState({ login: p.login, name: p.name, surname: p.surname, phone: p.phone ?? '', birthDate: p.birthDate, icon: p.icon })
  const [pr, setPr] = useState<Problems>({})
  const [err, setErr] = useState<UserRes | null>(null)
  const set = (k: 'login' | 'name' | 'surname' | 'phone' | 'birthDate') => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr(null)
    const v = validateProfile(f); setPr(v)
    if (Object.keys(v).length) return
    setBusy(true)
    const r = await user.updateProfile(f)
    setBusy(false)
    if (r.ok) { showToast('Zapisano zmiany w profilu'); onClose() } else setErr(r)
  }
  const fld = (k: 'login' | 'name' | 'surname' | 'phone' | 'birthDate', label: string, type = 'text', hint?: string) => (
    <div><Field label={label} hint={hint}><input type={type} value={f[k]} onChange={set(k)} className={cx(inputCls, pr[k] && '!border-danger')} /></Field><Err>{pr[k]}</Err></div>
  )
  return (
    <Modal title="Edytuj profil" onClose={onClose}>
      <form onSubmit={submit} noValidate className="space-y-4">
        {fld('login', 'Login')}
        <div className="grid gap-4 sm:grid-cols-2">{fld('name', 'Imię')}{fld('surname', 'Nazwisko')}</div>
        {fld('phone', 'Telefon (opcjonalnie)', 'tel')}
        {fld('birthDate', 'Data urodzenia', 'date')}
        <div>
          <span className="mb-2 block text-xs font-semibold uppercase tracking-wide text-ink2">Ikona profilu (opcjonalnie)</span>
          <div className="grid grid-cols-6 gap-2" role="radiogroup" aria-label="Ikona profilu">
            {PROFILE_ICONS.map((i) => (
              <button key={i} type="button" role="radio" aria-checked={f.icon === i} title={ICON_LABELS[i]} aria-label={ICON_LABELS[i]} onClick={() => setF({ ...f, icon: f.icon === i ? null : i })}
                className={cx('flex items-center justify-center rounded-xl border p-2', f.icon === i ? 'border-personal bg-mint-soft' : 'border-line hover:border-ink/30')}><ProfileIcon icon={i} size={36} /></button>
            ))}
          </div>
        </div>
        <p className="text-xs text-ink2">Adres e-mail zmienia się osobną czynnością „Zmień adres e-mail”.</p>
        {err && !err.ok && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{errText(err)}</p>}
        <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={onClose}>Anuluj</Btn><Btn type="submit" disabled={busy}>Zapisz zmiany</Btn></div>
      </form>
    </Modal>
  )
}

function ChangeEmail({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const [email, setEmail] = useState('')
  const [pw, setPw] = useState('')
  const [pr, setPr] = useState<Problems>({})
  const [err, setErr] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr(null)
    const v: Problems = {}
    const ev = validateEmail(email); if (ev) v.email = ev
    if (!pw) v.pw = 'Podaj aktualne hasło.'
    setPr(v)
    if (Object.keys(v).length) return
    setBusy(true)
    const r = await user.requestEmailChange(email.trim(), pw)
    setBusy(false)
    if (r.ok) onClose(); else setErr(r)
  }
  return (
    <Modal title="Zmień adres e-mail" onClose={onClose}>
      <form onSubmit={submit} noValidate className="space-y-4">
        <div><Field label="Nowy adres e-mail"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={cx(inputCls, pr.email && '!border-danger')} /></Field><Err>{pr.email}</Err></div>
        <div><Field label="Aktualne hasło"><input type="password" value={pw} onChange={(e) => setPw(e.target.value)} autoComplete="current-password" className={cx(inputCls, pr.pw && '!border-danger')} /></Field><Err>{pr.pw}</Err></div>
        <p className="text-xs text-ink2">Wyślemy link potwierdzający na NOWY adres. Adres konta zmieni się dopiero po jego potwierdzeniu.</p>
        {err && !err.ok && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{errText(err)}</p>}
        <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={onClose}>Anuluj</Btn><Btn type="submit" disabled={busy}>Wyślij link</Btn></div>
      </form>
    </Modal>
  )
}

export default function Profile() {
  const { navigate, showToast } = useApp()
  const user = useUser()
  const p = user.profile
  const [edit, setEdit] = useState(false)
  const [mail, setMail] = useState(false)
  const [ask, setAsk] = useState(false)
  const [err, setErr] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const deactivate = async () => {
    setBusy(true)
    const r = await user.requestDeactivation()
    setBusy(false)
    if (r.ok) { setAsk(false); setErr(null); showToast('Wysłaliśmy link potwierdzający dezaktywację') } else setErr(r)
  }
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <PageTitle title="Konto" sub="Twój profil i ustawienia konta" aside={<Btn variant="ghost" onClick={async () => { await user.logout(); navigate({ name: 'login' }) }}><LogOut size={16} />Wyloguj</Btn>} />

      <Card>
        <div className="mb-4 flex flex-wrap items-center gap-4">
          <ProfileIcon icon={p.icon} size={64} />
          <div className="min-w-0 flex-1"><h2 className="font-display text-xl font-bold">{p.name} {p.surname}</h2><p className="text-sm text-ink2">@{p.login}</p></div>
          <Btn variant="ghost" onClick={() => setEdit(true)}><Pencil size={16} />Edytuj profil</Btn>
        </div>
        <KeyValue rows={[
          ['Login', p.login], ['Imię', p.name], ['Nazwisko', p.surname], ['Telefon', p.phone ?? '—'], ['Data urodzenia', dayText(p.birthDate)],
          ['Konto utworzone', dateText(p.createdAt)], ['Ostatnie logowanie', dateText(p.lastLoginTime)],
        ]} />
      </Card>

      <Card>
        <H aside={<Btn variant="ghost" onClick={() => setMail(true)}>Zmień adres e-mail</Btn>}>Adres e-mail</H>
        <KeyValue rows={[['E-mail', p.email], ['Status', <Badge key="s" tone={p.isEmailConfirmed ? 'ok' : 'warn'}>{p.isEmailConfirmed ? 'Potwierdzony' : 'Niepotwierdzony'}</Badge>]]} />
        {user.pendingEmail && (
          <div className="mt-3 space-y-2 rounded-xl bg-wash p-3 text-sm">
            <p className="text-ink2">Wysłaliśmy link potwierdzający na nowy adres <b className="text-ink">{user.pendingEmail}</b>. Obecny adres konta nie zmieni się, dopóki go nie potwierdzisz.</p>
            <p className="text-xs text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
          </div>
        )}
      </Card>

      <Card>
        <H>Dezaktywacja konta</H>
        {user.deactivationRequested ? (
          <div className="space-y-3 text-sm">
            <p className="text-ink2">Wysłaliśmy wiadomość z linkiem potwierdzającym. Konto pozostaje aktywne, dopóki nie potwierdzisz dezaktywacji.</p>
            <p className="text-xs text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
          </div>
        ) : (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="max-w-sm text-sm text-ink2">Dezaktywacja wymaga potwierdzenia w wiadomości e-mail. Konto nie jest usuwane, a jego ponowna aktywacja nie jest dostępna.</p>
            <Btn variant="danger" onClick={() => { setErr(null); setAsk(true) }}>Dezaktywuj konto</Btn>
          </div>
        )}
      </Card>

      {edit && <EditProfile onClose={() => setEdit(false)} />}
      {mail && <ChangeEmail onClose={() => { setMail(false) }} />}
      {ask && (
        <Modal title="Dezaktywować konto?" onClose={() => setAsk(false)}>
          <p className="mb-4 text-sm text-ink2">Wyślemy wiadomość z linkiem potwierdzającym. Konto stanie się nieaktywne dopiero po potwierdzeniu i nie zostanie usunięte.</p>
          {err && !err.ok && <p role="alert" className="mb-4 rounded-xl bg-danger-soft p-3 text-sm text-danger">{errText(err)}</p>}
          <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={() => setAsk(false)}>Anuluj</Btn><Btn variant="danger" onClick={deactivate} disabled={busy}>Tak, dezaktywuj</Btn></div>
        </Modal>
      )}
    </div>
  )
}
