import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { CircleCheck } from 'lucide-react'
import logo from '../assets/logo-4money.png'
import Login from './Login'
import ResendConfirmationModal from './ResendConfirmationModal'
import { useApp, type RouteName } from '../context'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { clearEmailLink, confirmEmailLink } from '../emailLinks'
import { USER_REASON, validateBirth, validateName, validatePassword, type Problems } from '../mocks/users'
import { Btn, Card, Field, PasswordInput, cx } from '../ui'

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
const errText = (r: UserRes) => (r.ok ? null : (r.reason && USER_REASON[r.reason]) || describeError(r.code, USER_TEXT, { includeDetails: false }))
function Err({ children }: { children?: ReactNode }) { return children ? <p className="mt-1 text-xs text-danger">{children}</p> : null }
function Banner({ children }: { children: ReactNode }) { return <div role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{children}</div> }
function Success({ children }: { children: ReactNode }) { return <div className="flex items-start gap-3 rounded-xl bg-safe-soft p-4 text-sm text-safe"><CircleCheck size={20} className="shrink-0" /><div>{children}</div></div> }

function Links({ items }: { items: [RouteName, string][] }) {
  const { navigate } = useApp()
  const [resend, setResend] = useState(false)
  return <>
    <div className="mt-5 flex flex-wrap items-center gap-3 text-sm">
      {items.map(([name, label]) => name === 'resend'
        ? <Btn key={name} variant="ghost" onClick={() => setResend(true)}>{label}</Btn>
        : <button key={name} onClick={() => navigate({ name })} className="font-semibold text-ink">{label}</button>)}
    </div>
    {resend && <ResendConfirmationModal onClose={() => setResend(false)} />}
  </>
}

function Shell({ title, intro, children }: { title: string; intro?: string; children: ReactNode }) {
  return <AuthLayout><Card className="!p-6"><h1 className="font-display text-2xl font-bold">{title}</h1>{intro && <p className="mt-1 text-ink2">{intro}</p>}<div className="mt-5 space-y-4">{children}</div></Card></AuthLayout>
}

export { default as Login } from './Login'

export { default as Register } from './Register'

function ConfirmScreen({ title, intro, success, run, links }: { title: string; intro: string; success: ReactNode; run: (token: string) => Promise<UserRes>; links: [RouteName, string][] }) {
  const { route } = useApp()
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
      }
      setRes(result)
    }).catch(() => { if (active) setRes({ ok: false, code: 500 }) })
    return () => { active = false }
  }, [route.name, token, attempt])
  return (
    <Shell title={title} intro={res?.ok ? undefined : intro}>
      {!token ? <Banner>Link jest niekompletny. Otwórz pełny link z wiadomości e-mail lub poproś o nową wiadomość.</Banner> : res?.ok ? <Success>{success}</Success> : res && !res.ok ? (
        <>
          <Banner>{errText(res)}</Banner>
          {(res.code === 0 || res.code >= 500 || res.code === 429) && <Btn onClick={() => setAttempt((value) => value + 1)}>Spróbuj ponownie</Btn>}
        </>
      ) : <p role="status" className="text-sm text-ink2">Potwierdzanie…</p>}
      <Links items={res?.ok ? links.filter(([name]) => name === 'login') : links} />
    </Shell>
  )
}

export const ConfirmEmail = () => {
  const user = useUser()
  return <ConfirmScreen title="Potwierdzenie adresu e-mail" intro="Potwierdź adres e-mail, aby móc się zalogować." success="Adres e-mail został potwierdzony. Możesz się zalogować." run={user.confirmEmail} links={[['login', 'Przejdź do logowania'], ['resend', 'Wyślij wiadomość ponownie']]} />
}
export const ConfirmEmailChange = () => {
  const user = useUser()
  return <ConfirmScreen title="Potwierdzenie zmiany adresu e-mail" intro="Potwierdzamy nowy adres e-mail Twojego konta." success="Adres e-mail konta został pomyślnie zmieniony. Zaloguj się ponownie." run={user.confirmEmailChange} links={[['login', 'Przejdź do logowania']]} />
}
export const ConfirmDeactivation = () => {
  const user = useUser()
  return <ConfirmScreen title="Dezaktywacja konta" intro="Potwierdzamy dezaktywację Twojego konta." success="Twoje konto zostało pomyślnie zdezaktywowane. Dane pozostają w systemie, ale logowanie nie jest już możliwe." run={user.confirmDeactivation} links={[['login', 'Przejdź do logowania']]} />
}

export const ResendConfirmation = () => <Login showResendConfirmation />

export function Forgot() {
  return <Login showResetPassword />
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
    <Shell title="Ustaw nowe hasło">
      {res?.ok ? <Success>Hasło zostało zmienione. Możesz się teraz zalogować.</Success> : (
        <form onSubmit={submit} noValidate className="space-y-4">
          {!token && <Banner>Link jest niekompletny. Otwórz link z wiadomości lub poproś o nowy.</Banner>}
          <div><Field label="Nowe hasło" hint="8-128 znaków, wielka litera, cyfra i znak specjalny."><PasswordInput value={pw} onChange={(e) => setPw(e.target.value)} autoComplete="new-password" aria-invalid={!!p.pw} className={cx(p.pw && '!border-danger')} /></Field><Err>{p.pw}</Err></div>
          <div><Field label="Powtórz nowe hasło"><PasswordInput value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" aria-invalid={!!p.repeat} className={cx(p.repeat && '!border-danger')} /></Field><Err>{p.repeat}</Err></div>
          {res && !res.ok && <Banner>{errText(res)}</Banner>}
          <Btn type="submit" disabled={busy || !token} className="w-full">{busy ? 'Zapisywanie…' : 'Zapisz hasło'}</Btn>
        </form>
      )}
      <Links items={[['login', 'Przejdź do logowania']]} />
    </Shell>
  )
}
void validateName; void validateBirth
