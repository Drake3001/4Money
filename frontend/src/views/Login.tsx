import { useState, type FormEvent } from 'react'
import { ArrowRight, Check, Eye, EyeOff, LoaderCircle } from 'lucide-react'
import { useApp } from '../context'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { USER_REASON, validateEmail, validateLogin, type Problems } from '../mocks/users'
import { cx } from '../ui'
import GoogleSignIn from './GoogleSignIn'
import SplitAuthLayout from './SplitAuthLayout'
import ForgotPasswordModal from './ForgotPasswordModal'
import AuthMessage from './AuthMessage'
import ResendConfirmationModal from './ResendConfirmationModal'

const fieldClass = 'w-full rounded-xl border border-line bg-white px-4 py-3.5 text-sm text-ink transition placeholder:text-ink2/50 focus:border-ink focus:outline-none focus:ring-4 focus:ring-ink/10'

export default function Login({ showResetPassword = false, showResendConfirmation = false }: { showResetPassword?: boolean; showResendConfirmation?: boolean }) {
  const { navigate, route } = useApp()
  const user = useUser()
  const [resetPassword, setResetPassword] = useState(showResetPassword)
  const [resendConfirmation, setResendConfirmation] = useState(showResendConfirmation)
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [visible, setVisible] = useState(false)
  const [problems, setProblems] = useState<Problems>({})
  const [error, setError] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError(null)
    if (busy) return
    const p: Problems = {}
    const invalid = login.includes('@') ? validateEmail(login) : validateLogin(login); if (invalid) p.login = invalid
    if (!password) p.password = 'Podaj hasło.'
    else if (password.length > 128) p.password = 'Hasło może mieć maksymalnie 128 znaków.'
    setProblems(p)
    if (Object.keys(p).length) return
    setBusy(true)
    try {
      const r = await user.login(login.trim(), password)
      if (r.ok) navigate({ name: 'dashboard' }); else setError(r)
    } finally { setBusy(false) }
  }
  const errorText = error && !error.ok
    ? (error.reason && USER_REASON[error.reason]) || describeError(error.code, { 401: 'Nieprawidłowy login lub hasło.', 403: 'Logowanie nie jest możliwe. Sprawdź, czy konto jest aktywne i adres e-mail został potwierdzony.' })
    : null

  return (
    <SplitAuthLayout>
        <div className="login-panel w-full max-w-[400px]">
          <p className="mb-3 flex w-fit items-center rounded-full bg-ink px-3 py-1.5 text-xs font-bold uppercase tracking-[0.18em] text-mint">Dobrze Cię widzieć</p>
          <h2 className="font-display text-4xl font-bold tracking-tight text-ink">Logowanie</h2>

          {route.tab === 'email-confirmed' && <div role="status" className="mt-6 flex gap-2 rounded-xl bg-safe-soft p-3 text-sm text-safe"><Check size={18} className="shrink-0" />Adres e-mail został potwierdzony. Możesz się zalogować.</div>}

          <div className="login-google mt-6"><GoogleSignIn disabled={busy} /></div>
          <div className="login-divider my-5 flex items-center gap-4" aria-hidden="true"><span className="h-px flex-1 bg-line" /><span className="text-xs text-ink2/65">lub użyj loginu lub e-maila</span><span className="h-px flex-1 bg-line" /></div>

          <form onSubmit={submit} noValidate className="login-form space-y-5">
            <div>
              <label htmlFor="login-identity" className="mb-2 block text-sm font-semibold text-ink">Login lub e-mail</label>
              <input id="login-identity" name="username" value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="username" autoCapitalize="none" spellCheck={false} placeholder="Twój login lub adres e-mail" aria-invalid={!!problems.login} aria-describedby={problems.login ? 'login-identity-error' : undefined} className={cx(fieldClass, problems.login && '!border-danger')} />
              <AuthMessage id="login-identity-error">{problems.login}</AuthMessage>
            </div>
            <div>
              <label htmlFor="login-password" className="mb-2 block text-sm font-semibold text-ink">Hasło</label>
              <div className="relative">
                <input id="login-password" name="password" type={visible ? 'text' : 'password'} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="current-password" placeholder="Wpisz hasło" aria-invalid={!!problems.password} aria-describedby={problems.password ? 'login-password-error' : undefined} className={cx(fieldClass, 'pr-12', problems.password && '!border-danger')} />
                <button type="button" onClick={() => setVisible((v) => !v)} aria-label={visible ? 'Ukryj hasło' : 'Pokaż hasło'} aria-pressed={visible} className="absolute inset-y-0 right-0 flex w-12 items-center justify-center rounded-r-xl text-ink2/60 transition hover:text-ink">{visible ? <EyeOff size={18} /> : <Eye size={18} />}</button>
              </div>
              <div className="min-h-[60px] pt-1 pb-2">
                {problems.password && <p id="login-password-error" role="alert" className="text-xs leading-4 text-danger">{problems.password}</p>}
                {errorText && <p role="alert" className="text-sm leading-5 text-danger">{errorText}</p>}
              </div>
            </div>
            <button type="submit" disabled={busy} className="group flex min-h-12 w-full items-center justify-center gap-3 rounded-xl bg-ink px-5 py-3.5 text-sm font-semibold text-white shadow-[0_6px_18px_rgba(7,24,58,.12)] transition hover:bg-[#12285a] disabled:cursor-wait disabled:opacity-60">
              {busy ? <><LoaderCircle size={18} className="animate-spin" />Logowanie…</> : <>Zaloguj się<ArrowRight size={18} className="transition-transform group-hover:translate-x-1" /></>}
            </button>
          </form>

          <div className="login-actions mt-6 grid grid-cols-2 items-center gap-3 border-t border-line pt-6">
            <button onClick={() => navigate({ name: 'register' })} className="flex min-h-11 items-center justify-center gap-2 rounded-xl bg-mint-soft px-3 py-3 text-sm font-semibold text-ink transition hover:bg-mint/25">Załóż konto<ArrowRight size={15} /></button>
            <button onClick={() => setResetPassword(true)} className="min-h-11 rounded-xl px-2 py-3 text-sm font-medium text-ink2 transition hover:bg-wash hover:text-ink">Nie pamiętam hasła</button>
          </div>
        </div>
      {resetPassword && <ForgotPasswordModal onClose={() => { setResetPassword(false); if (route.name === 'forgot') navigate({ name: 'login' }) }} />}
      {resendConfirmation && <ResendConfirmationModal onClose={() => { setResendConfirmation(false); if (route.name === 'resend') navigate({ name: 'login' }) }} />}
    </SplitAuthLayout>
  )
}
