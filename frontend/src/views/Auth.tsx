import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { CircleCheck } from 'lucide-react'
import logo from '../assets/logo-4money.png'
import { useApp, type RouteName } from '../context'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { clearEmailLink, confirmEmailLink } from '../emailLinks'
import { USER_REASON, validateBirth, validateEmail, validateLogin, validateName, validatePassword, validateRegister, type Problems } from '../mocks/users'
import { Btn, Card, Field, cx, inputCls } from '../ui'

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-full flex-col items-center bg-wash px-4 py-10">
      <div className="mb-6 flex items-center gap-3">
        <img src={logo} alt="4Money" className="block aspect-square h-11 w-11 shrink-0 object-contain" />
        <span className="text-xl font-extrabold tracking-tight">4Money</span>
      </div>
      <div className="w-full max-w-md">{children}</div>
    </div>
  )
}

const USER_TEXT: Record<number, string> = {
  401: 'Nieprawidłowy login lub hasło.',
  403: 'Logowanie nie jest możliwe: konto jest nieaktywne.',
  409: 'Ten login lub adres e-mail jest już zajęty.',
  400: 'Dane są nieprawidłowe lub link wygasł. Sprawdź je i spróbuj ponownie.',
}
const errText = (r: UserRes) => (r.ok ? null : (r.reason && USER_REASON[r.reason]) || describeError(r.code, USER_TEXT))
function Err({ children }: { children?: ReactNode }) { return children ? <p className="mt-1 text-xs text-danger">{children}</p> : null }
function Banner({ children }: { children: ReactNode }) { return <div role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{children}</div> }
function Success({ children }: { children: ReactNode }) { return <div className="flex items-start gap-3 rounded-xl bg-safe-soft p-4 text-sm text-safe"><CircleCheck size={20} className="shrink-0" /><div>{children}</div></div> }
function SpamHint() { return <p className="text-xs leading-relaxed text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p> }

function Links({ items }: { items: [RouteName, string][] }) {
  const { navigate } = useApp()
  return (
    <p className="mt-5 flex flex-wrap gap-x-4 gap-y-1 text-sm">
      {items.map(([name, label]) => <button key={name} onClick={() => navigate({ name })} className="font-semibold text-personal">{label}</button>)}
    </p>
  )
}
function Shell({ title, intro, children }: { title: string; intro?: string; children: ReactNode }) {
  return <AuthLayout><Card className="!p-6"><h1 className="font-display text-2xl font-bold">{title}</h1>{intro && <p className="mt-1 text-ink2">{intro}</p>}<div className="mt-5 space-y-4">{children}</div></Card></AuthLayout>
}

export function Login() {
  const { navigate, route } = useApp()
  const user = useUser()
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [p, setP] = useState<Problems>({})
  const [err, setErr] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr(null)
    const pr: Problems = {}
    const l = validateLogin(login); if (l) pr.login = l
    if (!password) pr.password = 'Podaj hasło.'; else if (password.length > 128) pr.password = 'Hasło może mieć maksymalnie 128 znaków.'
    setP(pr)
    if (Object.keys(pr).length) return
    setBusy(true)
    const r = await user.login(login, password)
    setBusy(false)
    if (r.ok) navigate({ name: 'dashboard' }); else setErr(r)
  }
  return (
    <Shell title="Logowanie">
      {route.tab === 'email-confirmed' && <Success>Adres e-mail został potwierdzony. Możesz się zalogować.</Success>}
      <form onSubmit={submit} noValidate className="space-y-4">
        <div><Field label="Login"><input value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="username" className={cx(inputCls, p.login && '!border-danger')} /></Field><Err>{p.login}</Err></div>
        <div><Field label="Hasło"><input type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" className={cx(inputCls, p.password && '!border-danger')} /></Field><Err>{p.password}</Err></div>
        {err && !err.ok && <Banner>{errText(err)}</Banner>}
        <Btn type="submit" disabled={busy} className="w-full">{busy ? 'Logowanie…' : 'Zaloguj'}</Btn>
      </form>
      <Links items={[['forgot', 'Nie pamiętam hasła?'], ['register', 'Załóż konto'], ['resend', 'Wyślij potwierdzenie ponownie']]} />
    </Shell>
  )
}

export function Register() {
  const { navigate } = useApp()
  const user = useUser()
  const [f, setF] = useState({ login: '', name: '', surname: '', email: '', password: '', birthDate: '' })
  const [repeat, setRepeat] = useState('') // frontend-only usability field, never part of the request
  const [p, setP] = useState<Problems>({})
  const [err, setErr] = useState<UserRes | null>(null)
  const [done, setDone] = useState(false)
  const set = (k: keyof typeof f) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value })
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setErr(null)
    const pr = validateRegister(f)
    if (repeat !== f.password) pr.repeat = 'Hasła nie są takie same.'
    setP(pr)
    if (Object.keys(pr).length) return
    setBusy(true)
    const r = await user.register({ ...f, login: f.login.trim(), email: f.email.trim() })
    setBusy(false)
    if (r.ok) setDone(true); else setErr(r)
  }
  const fld = (k: keyof typeof f, label: string, type = 'text', auto?: string) => (
    <div><Field label={label}><input type={type} value={f[k]} onChange={set(k)} autoComplete={auto} className={cx(inputCls, p[k] && '!border-danger')} /></Field><Err>{p[k]}</Err></div>
  )
  if (done) return (
    <Shell title="Rejestracja">
      <Success>Konto zostało utworzone. Wysłaliśmy wiadomość z linkiem potwierdzającym na adres {f.email.trim()}. Przed zalogowaniem musisz potwierdzić adres e-mail.</Success>
      <SpamHint />
      <div className="flex flex-wrap gap-2"><Btn variant="ghost" onClick={() => navigate({ name: 'resend' })}>Nie dostałem wiadomości</Btn><Btn onClick={() => navigate({ name: 'login' })}>Przejdź do logowania</Btn></div>
    </Shell>
  )
  return (
    <Shell title="Rejestracja">
      <form onSubmit={submit} noValidate className="space-y-4">
        {fld('login', 'Login', 'text', 'username')}
        <div className="grid gap-4 sm:grid-cols-2">{fld('name', 'Imię', 'text', 'given-name')}{fld('surname', 'Nazwisko', 'text', 'family-name')}</div>
        {fld('email', 'E-mail', 'email', 'email')}
        <div><Field label="Hasło" hint="8–128 znaków, wielka litera, cyfra i znak specjalny."><input type="password" value={f.password} onChange={set('password')} autoComplete="new-password" className={cx(inputCls, p.password && '!border-danger')} /></Field><Err>{p.password}</Err></div>
        <div><Field label="Powtórz hasło"><input type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" className={cx(inputCls, p.repeat && '!border-danger')} /></Field><Err>{p.repeat}</Err></div>
        {fld('birthDate', 'Data urodzenia', 'date', 'bday')}
        {err && !err.ok && <Banner>{err.code === 503 ? 'Nie udało się wysłać wiadomości potwierdzającej. Konto nie zostało utworzone. Spróbuj zarejestrować się ponownie.' : errText(err)}</Banner>}
        <Btn type="submit" disabled={busy} className="w-full">{busy ? 'Rejestrowanie…' : 'Zarejestruj'}</Btn>
      </form>
      <Links items={[['login', 'Mam już konto']]} />
    </Shell>
  )
}

function ConfirmScreen({ title, intro, success, run, links, toLogin = false }: { title: string; intro: string; success: ReactNode; run: (token: string) => Promise<UserRes>; links: [RouteName, string][]; toLogin?: boolean }) {
  const { route, navigate } = useApp()
  const token = route.id ?? ''
  const [res, setRes] = useState<UserRes | null>(null)
  const [attempt, setAttempt] = useState(0)
  const latestRun = useRef(run)
  latestRun.current = run
  useEffect(() => {
    if (!token) return
    let active = true
    setRes(null)
    void confirmEmailLink(route.name, token, (value) => latestRun.current(value), attempt > 0).then((result) => {
      if (!active) return
      if (result.ok) {
        clearEmailLink()
        if (toLogin) { navigate({ name: 'login', tab: 'email-confirmed' }); return }
      }
      setRes(result)
    }).catch(() => { if (active) setRes({ ok: false, code: 500 }) })
    return () => { active = false }
  }, [route.name, token, attempt, toLogin, navigate])
  return (
    <Shell title={title} intro={res?.ok ? undefined : intro}>
      {!token ? <Banner>Link jest niekompletny. Otwórz pełny link z wiadomości e-mail lub poproś o nową wiadomość.</Banner> : res?.ok ? <Success>{success}</Success> : res && !res.ok ? (
        <>
          <Banner>{errText(res)}</Banner>
          {(res.code === 0 || res.code >= 500 || res.code === 429) && <Btn onClick={() => setAttempt((value) => value + 1)}>Spróbuj ponownie</Btn>}
        </>
      ) : <p role="status" className="text-sm text-ink2">Potwierdzanie…</p>}
      <Links items={links} />
    </Shell>
  )
}

export const ConfirmEmail = () => {
  const user = useUser()
  return <ConfirmScreen title="Potwierdzenie adresu e-mail" intro="Potwierdź adres e-mail, aby móc się zalogować." success="Adres e-mail został potwierdzony. Możesz się zalogować." run={user.confirmEmail} toLogin links={[['login', 'Przejdź do logowania'], ['resend', 'Wyślij wiadomość ponownie']]} />
}
export const ConfirmEmailChange = () => {
  const user = useUser()
  return <ConfirmScreen title="Potwierdzenie zmiany adresu e-mail" intro="Potwierdzamy nowy adres e-mail Twojego konta." success="Adres e-mail konta został pomyślnie zmieniony. Zaloguj się ponownie." run={user.confirmEmailChange} links={[['login', 'Przejdź do logowania']]} />
}
export const ConfirmDeactivation = () => {
  const user = useUser()
  return <ConfirmScreen title="Dezaktywacja konta" intro="Potwierdzamy dezaktywację Twojego konta." success="Twoje konto zostało pomyślnie zdezaktywowane. Dane pozostają w systemie, ale logowanie nie jest już możliwe." run={user.confirmDeactivation} links={[['login', 'Przejdź do logowania']]} />
}

function EmailOnly({ title, intro, button, success, run, links }: { title: string; intro: string; button: string; success: ReactNode; run: (email: string) => Promise<UserRes>; links: [RouteName, string][] }) {
  const [email, setEmail] = useState('')
  const [e1, setE1] = useState<string | null>(null)
  const [res, setRes] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setRes(null)
    const v = validateEmail(email); setE1(v)
    if (v) return
    setBusy(true)
    setRes(await run(email.trim()))
    setBusy(false)
  }
  return (
    <Shell title={title} intro={res?.ok ? undefined : intro}>
      {res?.ok ? <><Success>{success}</Success><SpamHint /></> : (
        <form onSubmit={submit} noValidate className="space-y-4">
          <div><Field label="E-mail"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={cx(inputCls, e1 && '!border-danger')} /></Field><Err>{e1}</Err></div>
          {res && !res.ok && <Banner>{errText(res)}</Banner>}
          <Btn type="submit" disabled={busy} className="w-full">{button}</Btn>
        </form>
      )}
      <Links items={links} />
    </Shell>
  )
}
export const ResendConfirmation = () => {
  const user = useUser()
  return <EmailOnly title="Ponowne wysłanie potwierdzenia" intro="Podaj adres e-mail użyty przy rejestracji." button="Wyślij wiadomość ponownie" success="Jeśli konto wymaga potwierdzenia, wysłaliśmy nową wiadomość z linkiem." run={user.resendConfirmation} links={[['login', 'Wróć do logowania']]} />
}

export function Forgot() {
  const user = useUser()
  const [email, setEmail] = useState('')
  const [e1, setE1] = useState<string | null>(null)
  const [res, setRes] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setRes(null)
    const v = validateEmail(email); setE1(v)
    if (v) return
    setBusy(true)
    setRes(await user.forgotPassword(email.trim()))
    setBusy(false)
  }
  return (
    <Shell title="Nie pamiętam hasła" intro={res?.ok ? undefined : 'Podaj adres e-mail konta, a wyślemy link do ustawienia nowego hasła.'}>
      {res?.ok ? (
        <>
          <Success>Jeśli konto o takim adresie istnieje, wysłaliśmy na nie wiadomość z instrukcją resetowania hasła.</Success>
          <SpamHint />
        </>
      ) : (
        <form onSubmit={submit} noValidate className="space-y-4">
          <div><Field label="E-mail"><input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" className={cx(inputCls, e1 && '!border-danger')} /></Field><Err>{e1}</Err></div>
          {res && !res.ok && <Banner>{errText(res)}</Banner>}
          <Btn type="submit" disabled={busy} className="w-full">Wyślij link do resetowania hasła</Btn>
        </form>
      )}
      <Links items={[['login', 'Wróć do logowania']]} />
    </Shell>
  )
}

export function ResetPassword() {
  const user = useUser()
  const [pw, setPw] = useState('')
  const [repeat, setRepeat] = useState('')
  const { route } = useApp()
  const [token, setToken] = useState(route.id ?? '')
  const [busy, setBusy] = useState(false)
  const [p, setP] = useState<Problems>({})
  const [res, setRes] = useState<UserRes | null>(null)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setRes(null)
    const pr: Problems = {}
    if (!token) pr.token = 'Otwórz pełny link z wiadomości e-mail lub poproś o nowy link.'
    const v = validatePassword(pw); if (v) pr.pw = v
    if (repeat !== pw) pr.repeat = 'Hasła nie są takie same.'
    setP(pr)
    if (Object.keys(pr).length) return
    setBusy(true)
    const result = await user.resetPassword(token, pw)
    if (result.ok) { clearEmailLink(); setPw(''); setRepeat(''); setToken('') }
    setRes(result)
    setBusy(false)
  }
  return (
    <Shell title="Ustaw nowe hasło" intro={res?.ok ? undefined : 'Link z wiadomości e-mail pozwala ustawić nowe hasło.'}>
      {res?.ok ? <Success>Hasło zostało zmienione. Możesz się teraz zalogować.</Success> : (
        <form onSubmit={submit} noValidate className="space-y-4">
          {!token && <Banner>Link jest niekompletny. Otwórz link z wiadomości lub poproś o nowy.</Banner>}
          <div><Field label="Nowe hasło" hint="8–128 znaków, wielka litera, cyfra i znak specjalny."><input type="password" value={pw} onChange={(e) => setPw(e.target.value)} autoComplete="new-password" className={cx(inputCls, p.pw && '!border-danger')} /></Field><Err>{p.pw}</Err></div>
          <div><Field label="Powtórz nowe hasło"><input type="password" value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" className={cx(inputCls, p.repeat && '!border-danger')} /></Field><Err>{p.repeat}</Err></div>
          {res && !res.ok && <Banner>{errText(res)}</Banner>}
          <Btn type="submit" disabled={busy || !token} className="w-full">{busy ? 'Zapisywanie…' : 'Zapisz hasło'}</Btn>
        </form>
      )}
      <Links items={[['login', res?.ok ? 'Przejdź do logowania' : 'Wróć do logowania'], ['forgot', 'Wyślij nowy link']]} />
    </Shell>
  )
}
void validateName; void validateBirth
