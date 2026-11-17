import { useState, type FormEvent } from 'react'
import { LogOut, Pencil } from 'lucide-react'
import { useApp } from '../context'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { USER_REASON, validateEmail, validateProfile, type Problems } from '../mocks/users'
import { dayText } from '../mocks/bank'
import { Badge, Btn, Card, Field, H, KeyValue, Modal, PageTitle, PasswordInput, cx, inputCls } from '../ui'
import { ProfileIcon } from './ProfileIcon'
import { ChangePassword, ChangeProfileIcon } from './ProfileSettings'
import ProfileMessage from './ProfileMessage'

const PROFILE_TEXT: Record<number, string> = {
  400: 'Dane są nieprawidłowe. Sprawdź formularz i, przy zmianie e-maila, aktualne hasło.',
  409: 'Ten login lub adres e-mail jest już zajęty.',
  429: 'Zbyt wiele prób. Spróbuj ponownie za chwilę.',
}
const errText = (r: UserRes) => (r.ok ? null : (r.reason && USER_REASON[r.reason]) || describeError(r.code, PROFILE_TEXT))
const Err = ({ children }: { children?: string | null }) => <ProfileMessage field>{children}</ProfileMessage>

function EditProfile({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const { showToast } = useApp()
  const p = user.profile
  const [f, setF] = useState({ login: p.login, name: p.name, surname: p.surname, phone: p.phone ?? '', birthDate: p.birthDate })
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
      <form onSubmit={submit} noValidate className="space-y-1">
        {fld('login', 'Login')}
        <div className="grid gap-4 sm:grid-cols-2">{fld('name', 'Imię')}{fld('surname', 'Nazwisko')}</div>
        {fld('birthDate', 'Data urodzenia', 'date')}
        {fld('phone', 'Telefon (opcjonalnie)', 'tel')}
        <ProfileMessage>{err && !err.ok ? errText(err) : null}</ProfileMessage>
        <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={onClose}>Anuluj</Btn><Btn type="submit" disabled={busy}>Zapisz zmiany</Btn></div>
      </form>
    </Modal>
  )
}

function ChangeEmail({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const [sent, setSent] = useState(false)
  const [email, setEmail] = useState('')
  const [pw, setPw] = useState('')
  const [pr, setPr] = useState<Problems>({})
  const [err, setErr] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const close = () => { if (!busy) onClose() }
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr(null)
    if (busy) return
    const v: Problems = {}
    const ev = validateEmail(email); if (ev) v.email = ev
    if (!pw) v.pw = 'Podaj aktualne hasło.'
    setPr(v)
    if (Object.keys(v).length) return
    setBusy(true)
    try {
      const r = await user.requestEmailChange(email.trim(), pw)
      if (r.ok) { setSent(true); setPw('') } else setErr(r)
    } finally { setBusy(false) }
  }
  return (
    <Modal title="Zmień adres e-mail" onClose={close}>
      {sent ? <div className="space-y-4">
        <div role="status" className="space-y-3 rounded-xl bg-mint-soft p-4 text-sm">
          <p>Sprawdź skrzynkę:<strong className="mt-1 block break-all">{email.trim()}</strong></p>
          <p>Wysłaliśmy link potwierdzający na nowy adres e-mail. Adres e-mail konta zmieni się dopiero po jego potwierdzeniu.</p>
          <p className="text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
        </div>
        <div className="flex justify-end"><Btn variant="ghost" onClick={close}>Zamknij</Btn></div>
      </div> : <form onSubmit={submit} noValidate className="space-y-1">
        <div><Field label="Nowy adres e-mail"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={cx(inputCls, pr.email && '!border-danger')} /></Field><Err>{pr.email}</Err></div>
        <div><Field label="Aktualne hasło"><PasswordInput value={pw} onChange={(e) => setPw(e.target.value)} autoComplete="current-password" aria-invalid={!!pr.pw} className={cx(pr.pw && '!border-danger')} /></Field><Err>{pr.pw}</Err></div>
        <p className="text-xs text-ink2">Wyślemy link potwierdzający na NOWY adres e-mail. Adres e-mail konta zmieni się dopiero po jego potwierdzeniu.</p>
        <ProfileMessage>{err && !err.ok ? errText(err) : null}</ProfileMessage>
        <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={close} disabled={busy}>Anuluj</Btn><Btn type="submit" disabled={busy}>{busy ? 'Wysyłanie…' : 'Wyślij link'}</Btn></div>
      </form>}
    </Modal>
  )
}

export default function Profile() {
  const { navigate } = useApp()
  const user = useUser()
  const p = user.profile
  const [edit, setEdit] = useState(false)
  const [mail, setMail] = useState(false)
  const [icon, setIcon] = useState(false)
  const [password, setPassword] = useState(false)
  const [ask, setAsk] = useState(false)
  const [deactivationSent, setDeactivationSent] = useState(false)
  const [err, setErr] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const closeDeactivation = () => { if (!busy) setAsk(false) }
  const deactivate = async () => {
    if (busy) return
    setBusy(true)
    try {
      const r = await user.requestDeactivation()
      if (r.ok) { setDeactivationSent(true); setErr(null) } else setErr(r)
    } finally { setBusy(false) }
  }
  return (
    <div className="mx-auto max-w-2xl space-y-6">
      <PageTitle title="Konto" sub="Twój profil i ustawienia konta" aside={<Btn variant="ghost" onClick={async () => { await user.logout(); navigate({ name: 'login' }) }}><LogOut size={16} />Wyloguj</Btn>} />

      <Card>
        <div className="mb-4 flex flex-wrap items-center gap-4">
          <button type="button" onClick={() => setIcon(true)} aria-label="Zmień ikonę profilu" title="Zmień ikonę profilu" className="rounded-full focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ink">
            <ProfileIcon name={p.name} surname={p.surname} url={user.profileImageUrl} size={64} />
          </button>
          <div className="min-w-0 flex-1"><h2 className="font-display text-xl font-bold">{p.name} {p.surname}</h2><p className="text-sm text-ink2">@{p.login}</p></div>
          <Btn variant="ghost" onClick={() => setEdit(true)}><Pencil size={16} />Edytuj profil</Btn>
        </div>
        <div className="mb-4"><Btn variant="soft" onClick={() => setIcon(true)}>Zmień ikonę profilu</Btn></div>
        <KeyValue rows={[
          ['Login', p.login], ['Imię', p.name], ['Nazwisko', p.surname], ['Data urodzenia', dayText(p.birthDate)], ['Telefon', p.phone?.trim() || '-'],
          ['Data utworzenia konta', dayText(p.createdAt)],
        ]} />
      </Card>

      <Card>
        <H aside={<Btn variant="ghost" disabled={p.isGoogleLinked} className={p.isGoogleLinked ? 'disabled:bg-wash disabled:text-ink2' : ''} onClick={() => setMail(true)}>Zmień adres e-mail</Btn>}>Adres e-mail</H>
        {p.isGoogleLinked && <p className="mb-3 text-sm text-ink2">Konto zarejestrowane przez Google. Brak możliwości zmiany adresu email.</p>}
        <KeyValue rows={[['E-mail', p.email], ['Status', <Badge key="s" tone={p.isEmailConfirmed ? 'ok' : 'warn'}>{p.isEmailConfirmed ? 'Potwierdzony' : 'Niepotwierdzony'}</Badge>]]} />
      </Card>

      <Card>
        <H>Hasło</H>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="max-w-sm text-sm text-ink2">{p.hasPassword ? 'Zmień hasło do konta, korzystając z linku wysłanego na Twój adres e-mail.' : p.isGoogleLinked ? 'Konto zarejestrowane przez Google. Ustaw hasło, aby logować się również w sposób tradycyjny.' : 'Ustaw hasło, aby logować się również loginem lub adresem e-mail.'}</p>
          <Btn variant="ghost" onClick={() => setPassword(true)}>{p.hasPassword ? 'Zmień hasło' : 'Ustaw hasło'}</Btn>
        </div>
      </Card>

      <Card>
        <H>Dezaktywacja konta</H>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <p className="max-w-sm text-sm text-ink2">Dezaktywacja wymaga potwierdzenia w wiadomości e-mail. Konto nie jest usuwane, a jego ponowna aktywacja nie jest dostępna.</p>
          <Btn variant="danger" onClick={() => { setErr(null); setDeactivationSent(false); setAsk(true) }}>Dezaktywuj</Btn>
        </div>
      </Card>

      {edit && <EditProfile onClose={() => setEdit(false)} />}
      {icon && <ChangeProfileIcon onClose={() => setIcon(false)} />}
      {password && <ChangePassword onClose={() => setPassword(false)} />}
      {mail && !p.isGoogleLinked && <ChangeEmail onClose={() => { setMail(false) }} />}
      {ask && (
        <Modal title={deactivationSent ? 'Dezaktywacja konta' : 'Dezaktywować konto?'} onClose={closeDeactivation}>
          {deactivationSent ? <div role="status" className="mb-4 space-y-3 rounded-xl bg-mint-soft p-4 text-sm">
            <p>Sprawdź skrzynkę:<strong className="mt-1 block break-all">{p.email}</strong></p>
            <p>Wysłaliśmy wiadomość z linkiem potwierdzającym. Konto pozostaje aktywne, dopóki nie potwierdzisz dezaktywacji.</p>
            <p className="text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
          </div> : <p className="mb-4 text-sm text-ink2">Wyślemy wiadomość z linkiem potwierdzającym. Konto stanie się nieaktywne dopiero po potwierdzeniu i nie zostanie usunięte.</p>}
          {p.isGoogleLinked && <p className="mb-4 rounded-xl bg-warn-soft p-3 text-sm text-warn">Rejestracja przez Google jest jednorazowa. Po dezaktywacji nie będzie można ponownie zarejestrować się w 4Money tym samym kontem Google.</p>}
          {!deactivationSent && <div className="mb-2"><ProfileMessage>{err && !err.ok ? errText(err) : null}</ProfileMessage></div>}
          <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={closeDeactivation} disabled={busy}>{deactivationSent ? 'Zamknij' : 'Anuluj'}</Btn>{!deactivationSent && <Btn variant="danger" onClick={deactivate} disabled={busy}>{busy ? 'Wysyłanie…' : 'Dezaktywuj'}</Btn>}</div>
        </Modal>
      )}
    </div>
  )
}
