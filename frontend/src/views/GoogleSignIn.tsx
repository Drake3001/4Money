import { useEffect, useRef, useState, type FormEvent } from 'react'
import { requestGoogleChallenge } from '../api/googleChallenge'
import { loadGoogleIdentity } from '../api/googleIdentity'
import { describeError } from '../api/errors'
import { useApp } from '../context'
import { useUser, type UserRes } from '../userContext'
import { validateProfile, type Problems } from '../mocks/users'
import { Btn, Field, FormMessage, Modal, cx, inputCls } from '../ui'

const GOOGLE_ERRORS: Record<string, string> = {
  invalid_google_credentials: 'Logowanie przez Google wygasło lub zostało przerwane. Spróbuj ponownie.',
  google_registration_required: 'Nie masz jeszcze konta 4Money powiązanego z tym kontem Google. Najpierw zarejestruj się przez Google.',
  account_inactive: 'Konto 4Money powiązane z tym kontem Google zostało zdezaktywowane. Rejestracja przez Google jest jednorazowa. Nie możesz ponownie zarejestrować się tym samym kontem Google.',
  email_confirmation_required: 'Najpierw potwierdź adres e-mail konta, korzystając z wiadomości od 4Money.',
  login_or_email_in_use: 'Ten login lub adres e-mail jest już zajęty. Istniejące konta nie są automatycznie łączone z Google.',
  google_account_already_exists: 'To konto Google zostało już użyte do rejestracji w 4Money. Nie możesz zarejestrować go ponownie. Zaloguj się do istniejącego konta.',
  email_delivery_failed: 'Konto zostało utworzone, ale nie udało się wysłać wiadomości. Skorzystaj z ponownej wysyłki potwierdzenia e-mail.',
}
const errorText = (r: UserRes<unknown>) => r.ok ? null : (r.apiCode && GOOGLE_ERRORS[r.apiCode]) || describeError(r.code, { 503: 'Logowanie przez Google jest obecnie niedostępne. Możesz użyć loginu i hasła.' })

function GoogleMark() {
  return <svg aria-hidden="true" width="19" height="19" viewBox="0 0 48 48"><path fill="#4285F4" d="M43.6 24.5c0-1.4-.1-2.8-.4-4.1H24v7.8h11a9.4 9.4 0 0 1-4.1 6.2v5.2h6.7c3.9-3.6 6-8.9 6-15.1Z" /><path fill="#34A853" d="M24 44c5.5 0 10.2-1.8 13.6-4.9l-6.7-5.2c-1.8 1.2-4.1 1.9-6.9 1.9-5.3 0-9.8-3.6-11.4-8.4H5.7v5.4A20.5 20.5 0 0 0 24 44Z" /><path fill="#FBBC05" d="M12.6 27.4a12 12 0 0 1 0-7.8v-5.4H5.7a20.5 20.5 0 0 0 0 18.6l6.9-5.4Z" /><path fill="#EA4335" d="M24 11.2c3 0 5.6 1 7.7 3l5.8-5.8A19.6 19.6 0 0 0 24 3 20.5 20.5 0 0 0 5.7 14.2l6.9 5.4c1.6-4.8 6.1-8.4 11.4-8.4Z" /></svg>
}

function GoogleRegistration({ idToken, onClose }: { idToken: string; onClose: () => void }) {
  const user = useUser()
  const { navigate } = useApp()
  const [f, setF] = useState({ login: '', name: '', surname: '', birthDate: '' })
  const [problems, setProblems] = useState<Problems>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [email, setEmail] = useState<string | null>(null)
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (busy) return
    const p = validateProfile({ ...f, phone: '' }); setProblems(p); setError(null)
    if (Object.keys(p).length) return
    setBusy(true)
    try {
      const r = await user.googleRegister({ ...f, login: f.login.trim(), name: f.name.trim(), surname: f.surname.trim(), idToken })
      if (!r.ok) setError(errorText(r))
      else if (r.data.requiresEmailConfirmation) setEmail(r.data.user.email)
      else { onClose(); navigate({ name: 'dashboard' }) }
    } finally { setBusy(false) }
  }
  return <Modal title={email ? 'Potwierdź adres e-mail' : 'Dokończ zakładanie konta'} onClose={() => { if (!busy) onClose() }}>
    {email ? <div className="space-y-4"><p className="text-sm text-ink2">Wysłaliśmy link potwierdzający na <strong className="break-all text-ink">{email}</strong>. Potwierdź adres przed zalogowaniem. Sprawdź również folder Spam lub Oferty.</p><Btn onClick={onClose}>Wróć do logowania</Btn></div> : <form onSubmit={submit} noValidate className="space-y-1">
      <p className="text-sm text-ink2">Uzupełnij dane swojego profilu 4Money. Adres e-mail zostanie pobrany z konta Google.</p>
      {([{ key: 'login', label: 'Login', auto: 'username' }, { key: 'name', label: 'Imię', auto: 'given-name' }, { key: 'surname', label: 'Nazwisko', auto: 'family-name' }, { key: 'birthDate', label: 'Data urodzenia', auto: 'bday' }] as const).map(({ key, label, auto }) => <div key={key}>
        <Field label={label}><input type={key === 'birthDate' ? 'date' : 'text'} value={f[key]} autoComplete={auto} disabled={busy} onChange={(e) => {
          setF((value) => ({ ...value, [key]: e.target.value }))
          setProblems((value) => { const next = { ...value }; delete next[key]; return next })
        }} className={cx(inputCls, problems[key] && '!border-danger')} /></Field>
        <FormMessage field>{problems[key]}</FormMessage>
      </div>)}
      <FormMessage>{error}</FormMessage>
      <div className="flex justify-end gap-2"><Btn variant="ghost" disabled={busy} onClick={onClose}>Anuluj</Btn><Btn type="submit" disabled={busy}>{busy ? 'Zakładanie konta…' : 'Załóż konto'}</Btn></div>
    </form>}
  </Modal>
}

export default function GoogleSignIn({ disabled = false, mode = 'login' }: { disabled?: boolean; mode?: 'login' | 'register' }) {
  const user = useUser()
  const { navigate } = useApp()
  const container = useRef<HTMLDivElement>(null)
  const [attempt, setAttempt] = useState(0)
  const [ready, setReady] = useState(false)
  const [loading, setLoading] = useState(true)
  const [working, setWorking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [errorCode, setErrorCode] = useState<string | null>(null)
  const [registrationToken, setRegistrationToken] = useState<string | null>(null)
  const locked = useRef(false)
  locked.current = disabled || working || !!registrationToken
  const callback = useRef(async (_credential: string) => {})
  callback.current = async (credential) => {
    if (locked.current || !credential) return
    locked.current = true; setWorking(true); setError(null); setErrorCode(null)
    try {
      const r = await user.googleLogin(credential)
      if (r.ok) navigate({ name: 'dashboard' })
      else if (r.apiCode === 'google_registration_required' && mode === 'register') setRegistrationToken(credential)
      else { setError(errorText(r)); setErrorCode(r.apiCode ?? null); setAttempt((n) => n + 1) }
    } finally { setWorking(false) }
  }

  useEffect(() => {
    let active = true
    setReady(false); setLoading(true)
    const init = async () => {
      const challenge = await requestGoogleChallenge()
      if (!active) return
      if (!challenge.ok) {
        if (attempt > 0) setError(errorText(challenge))
        setLoading(false); return
      }
      try {
        const identity = await loadGoogleIdentity()
        if (!active || !container.current) return
        identity.initialize({ client_id: challenge.data.clientId, nonce: challenge.data.nonce, auto_select: false, ux_mode: 'popup', callback: (response) => { if (active) void callback.current(response.credential) } })
        container.current.replaceChildren()
        identity.renderButton(container.current, { type: 'standard', theme: 'outline', size: 'large', text: mode === 'register' ? 'signup_with' : 'signin_with', shape: 'pill', width: Math.min(400, Math.max(200, container.current.clientWidth)), locale: 'pl' })
        setReady(true)
      } catch { setError('Nie udało się wczytać logowania przez Google. Spróbuj ponownie lub użyj loginu i hasła.') }
      finally { if (active) setLoading(false) }
    }
    void init()
    return () => { active = false }
  }, [attempt, mode])

  return <div>
    <div className={cx('relative h-12', (disabled || working) && 'pointer-events-none opacity-50')} aria-busy={loading || working}>
      <div ref={container} className={cx('min-h-11 w-full', !ready && 'invisible absolute inset-0')} />
      {!ready && <button type="button" disabled={disabled || loading || working} onClick={() => { setError(null); setAttempt((n) => n + 1) }} className="flex min-h-11 w-full items-center justify-center gap-3 rounded-full border border-line bg-white px-4 py-3 text-sm font-medium text-ink transition hover:bg-wash disabled:cursor-wait"><GoogleMark />{mode === 'register' ? 'Zarejestruj się przez Google' : 'Zaloguj się przez Google'}</button>}
    </div>
    {working && <p role="status" className="sr-only">Logowanie przez Google…</p>}
    {error && <Modal title={mode === 'register' ? 'Rejestracja przez Google' : 'Logowanie przez Google'} onClose={() => setError(null)}>
      <p role="alert" className="text-sm leading-relaxed text-danger">{error}</p>
      {errorCode === 'google_registration_required' && mode === 'login' && <div className="mt-4 flex justify-end"><Btn onClick={() => { setError(null); navigate({ name: 'register' }) }}>Przejdź do rejestracji</Btn></div>}
    </Modal>}
    {registrationToken && <GoogleRegistration idToken={registrationToken} onClose={() => { setRegistrationToken(null); setAttempt((n) => n + 1) }} />}
  </div>
}
