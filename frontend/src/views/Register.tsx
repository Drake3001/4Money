import { useState, type FormEvent } from 'react'
import { ArrowRight, Check, Eye, EyeOff, LoaderCircle } from 'lucide-react'
import { useApp } from '../context'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { USER_REASON, validateRegister, type Problems } from '../mocks/users'
import { Btn, cx } from '../ui'
import SplitAuthLayout from './SplitAuthLayout'
import GoogleSignIn from './GoogleSignIn'
import AuthMessage from './AuthMessage'
import ResendConfirmationModal from './ResendConfirmationModal'

const fieldClass = 'w-full min-w-0 rounded-xl border border-line bg-white px-4 py-2.5 text-sm text-ink transition placeholder:text-ink2/50 focus:border-ink focus:outline-none focus:ring-4 focus:ring-ink/10'

export default function Register() {
  const { navigate } = useApp()
  const user = useUser()
  const [f, setF] = useState({ login: '', name: '', surname: '', email: '', password: '', birthDate: '' })
  const [repeat, setRepeat] = useState('')
  const [visible, setVisible] = useState(false)
  const [problems, setProblems] = useState<Problems>({})
  const [error, setError] = useState<UserRes | null>(null)
  const [done, setDone] = useState(false)
  const [resend, setResend] = useState(false)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (busy) return
    setError(null)
    const p = validateRegister(f)
    if (repeat !== f.password) p.repeat = 'Hasła nie są takie same.'
    setProblems(p)
    if (Object.keys(p).length) return
    setBusy(true)
    try {
      const r = await user.register({ ...f, login: f.login.trim(), email: f.email.trim() })
      if (r.ok) setDone(true); else setError(r)
    } finally { setBusy(false) }
  }
  const field = (key: keyof typeof f, label: string, type = 'text', auto?: string) => <div className="min-w-0">
    <label htmlFor={`register-${key}`} className="mb-1.5 block text-sm font-semibold text-ink">{label}</label>
    <input id={`register-${key}`} name={key} type={type} value={f[key]} onChange={(e) => {
      setF((value) => ({ ...value, [key]: e.target.value }))
      setProblems((value) => { const next = { ...value }; delete next[key]; return next })
    }} autoComplete={auto} autoCapitalize={key === 'login' || key === 'email' ? 'none' : undefined} aria-invalid={!!problems[key]} aria-describedby={problems[key] ? `register-${key}-error` : undefined} className={cx(fieldClass, problems[key] && '!border-danger')} />
    <AuthMessage id={`register-${key}-error`}>{problems[key]}</AuthMessage>
  </div>
  return <SplitAuthLayout>
    <div className="register-panel w-full max-w-[400px]">
      <p className="mb-3 flex w-fit items-center rounded-full bg-ink px-3 py-1.5 text-xs font-bold uppercase tracking-[0.18em] text-mint">Zacznij z 4Money</p>
      <h2 className="font-display text-4xl font-bold tracking-tight text-ink">Rejestracja</h2>
      {done ? <div className="mt-6 space-y-5">
        <div role="status" className="flex items-start gap-3 rounded-xl bg-safe-soft p-4 text-sm text-safe"><Check size={20} className="shrink-0" /><p>Konto zostało utworzone. Wysłaliśmy link potwierdzający na adres <strong className="break-all">{f.email.trim()}</strong>. Potwierdź adres e-mail, aby się zalogować.</p></div>
        <p className="text-sm text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
        <button onClick={() => navigate({ name: 'login' })} className="min-h-11 w-full rounded-xl bg-ink px-4 py-3 text-sm font-semibold text-white hover:bg-[#12285a]">Przejdź do logowania</button>
        <Btn variant="ghost" className="w-full" onClick={() => setResend(true)}>Wyślij link ponownie</Btn>
      </div> : <>
        <p className="mt-3 text-sm leading-relaxed text-ink2">Załóż konto i przejmij kontrolę nad swoimi finansami.</p>
        <div className="login-google mt-6"><GoogleSignIn disabled={busy} mode="register" /></div>
        <div className="login-divider my-5 flex items-center gap-4" aria-hidden="true"><span className="h-px flex-1 bg-line" /><span className="text-xs text-ink2/65">lub użyj adresu e-mail</span><span className="h-px flex-1 bg-line" /></div>
        <form onSubmit={submit} noValidate className="register-form space-y-3">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{field('name', 'Imię', 'text', 'given-name')}{field('surname', 'Nazwisko', 'text', 'family-name')}</div>
          {field('email', 'E-mail', 'email', 'email')}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{field('login', 'Login', 'text', 'username')}{field('birthDate', 'Data urodzenia', 'date', 'bday')}</div>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div>
              <label htmlFor="register-password" className="mb-1.5 block text-sm font-semibold text-ink">Hasło</label>
              <div className="relative">
                <input id="register-password" name="password" type={visible ? 'text' : 'password'} value={f.password} onChange={(e) => setF((value) => ({ ...value, password: e.target.value }))} autoComplete="new-password" aria-invalid={!!problems.password} aria-describedby={problems.password ? 'register-password-hint register-password-error' : 'register-password-hint'} className={cx(fieldClass, 'pr-11', problems.password && '!border-danger')} />
                <button type="button" onClick={() => setVisible((value) => !value)} aria-label={visible ? 'Ukryj hasło' : 'Pokaż hasło'} aria-pressed={visible} className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-xl text-ink2/60 hover:text-ink">{visible ? <EyeOff size={18} /> : <Eye size={18} />}</button>
              </div>
              <AuthMessage id="register-password-error">{problems.password}</AuthMessage>
            </div>
            <div>
              <label htmlFor="register-repeat" className="mb-1.5 block text-sm font-semibold text-ink">Powtórz hasło</label>
              <input id="register-repeat" name="repeat" type={visible ? 'text' : 'password'} value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" aria-invalid={!!problems.repeat} aria-describedby={problems.repeat ? 'register-repeat-error' : undefined} className={cx(fieldClass, problems.repeat && '!border-danger')} />
              <AuthMessage id="register-repeat-error">{problems.repeat}</AuthMessage>
            </div>
          </div>
          <p id="register-password-hint" className="text-xs leading-relaxed text-ink2">Hasło: 8-128 znaków, wielka litera, cyfra i znak specjalny.</p>
          <AuthMessage banner>{error && !error.ok ? error.code === 503 ? 'Nie udało się wysłać wiadomości potwierdzającej. Konto nie zostało utworzone. Spróbuj ponownie.' : (error.reason && USER_REASON[error.reason]) || describeError(error.code, { 409: 'Ten login lub adres e-mail jest już zajęty.' }) : null}</AuthMessage>
          <button type="submit" disabled={busy} className="group flex min-h-12 w-full items-center justify-center gap-3 rounded-xl bg-ink px-5 py-3 text-sm font-semibold text-white shadow-[0_6px_18px_rgba(7,24,58,.12)] transition hover:bg-[#12285a] disabled:cursor-wait disabled:opacity-60">{busy ? <><LoaderCircle size={18} className="animate-spin" />Rejestrowanie…</> : <>Załóż konto<ArrowRight size={18} className="transition-transform group-hover:translate-x-1" /></>}</button>
        </form>
        <div className="login-actions mt-6 flex items-center justify-center gap-2 border-t border-line pt-6 text-sm"><span className="text-ink2">Masz już konto?</span><button onClick={() => navigate({ name: 'login' })} className="rounded-lg px-2 py-1 font-semibold text-ink hover:bg-mint-soft">Zaloguj się</button></div>
      </>}
    </div>
    {resend && <ResendConfirmationModal initialEmail={f.email.trim()} onClose={() => setResend(false)} />}
  </SplitAuthLayout>
}
