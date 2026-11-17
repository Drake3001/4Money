import { useState, type FormEvent } from 'react'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { validateEmail } from '../mocks/users'
import { Btn, Field, FormMessage, Modal, cx, inputCls } from '../ui'

export default function ResendConfirmationModal({ initialEmail = '', onClose }: { initialEmail?: string; onClose: () => void }) {
  const user = useUser()
  const [email, setEmail] = useState(initialEmail)
  const [problem, setProblem] = useState<string | null>(null)
  const [result, setResult] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const close = () => { if (!busy) onClose() }
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (busy) return
    const invalid = validateEmail(email)
    setProblem(invalid); setResult(null)
    if (invalid) return
    setBusy(true)
    try { setResult(await user.resendConfirmation(email.trim())) }
    finally { setBusy(false) }
  }
  return <Modal title="Wyślij link ponownie" onClose={close}>
    {result?.ok ? <div className="space-y-4">
      <div role="status" className="space-y-3 rounded-xl bg-mint-soft p-4 text-sm">
        <p>Sprawdź skrzynkę:<strong className="mt-1 block break-all">{email.trim()}</strong></p>
        <p>Jeśli konto wymaga potwierdzenia, wysłaliśmy nową wiadomość z linkiem.</p>
        <p className="text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
      </div>
      <div className="flex justify-end"><Btn variant="ghost" onClick={close}>Zamknij</Btn></div>
    </div> : <form onSubmit={submit} noValidate className="space-y-2">
      <p className="text-sm leading-relaxed text-ink2">Podaj adres e-mail użyty przy rejestracji. Wyślemy ponownie link potwierdzający.</p>
      <div>
        <Field label="E-mail"><input type="email" name="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" autoFocus aria-invalid={!!problem} aria-describedby={problem ? 'resend-email-error' : undefined} className={cx(inputCls, problem && '!border-danger')} /></Field>
        <FormMessage field id="resend-email-error">{problem}</FormMessage>
      </div>
      <FormMessage>{result && !result.ok ? describeError(result.code, { 429: 'Zbyt wiele prób. Spróbuj ponownie za chwilę.' }, { includeDetails: false }) : null}</FormMessage>
      <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={close} disabled={busy}>Anuluj</Btn><Btn type="submit" disabled={busy}>{busy ? 'Wysyłanie…' : 'Wyślij link'}</Btn></div>
    </form>}
  </Modal>
}
