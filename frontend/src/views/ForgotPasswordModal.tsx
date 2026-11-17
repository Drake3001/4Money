import { useState, type FormEvent } from 'react'
import { Check } from 'lucide-react'
import { useUser, type UserRes } from '../userContext'
import { describeError } from '../api/errors'
import { USER_REASON, validateEmail } from '../mocks/users'
import { Btn, Field, FormMessage, Modal, cx, inputCls } from '../ui'

export default function ForgotPasswordModal({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const [email, setEmail] = useState('')
  const [problem, setProblem] = useState<string | null>(null)
  const [result, setResult] = useState<UserRes | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (busy) return
    const invalid = validateEmail(email)
    setProblem(invalid); setResult(null)
    if (invalid) return
    setBusy(true)
    try { setResult(await user.forgotPassword(email.trim())) }
    finally { setBusy(false) }
  }
  return <Modal title="Nie pamiętasz hasła?" onClose={onClose}>
    {result?.ok ? <div className="space-y-5">
      <div role="status" className="flex gap-3 rounded-xl bg-safe-soft p-4 text-sm text-safe"><Check size={20} className="shrink-0" /><p>Jeśli konto o tym adresie istnieje, wysłaliśmy link do ustawienia nowego hasła.</p></div>
      <p className="text-sm text-ink2">Nie widzisz wiadomości? Sprawdź również folder Spam lub Oferty.</p>
      <Btn className="w-full" onClick={onClose}>Wróć do logowania</Btn>
    </div> : <form onSubmit={submit} noValidate className="space-y-2">
      <p className="text-sm leading-relaxed text-ink2">Podaj adres e-mail swojego konta. Wyślemy Ci link do ustawienia nowego hasła.</p>
      <div>
        <Field label="E-mail"><input type="email" name="email" autoComplete="email" autoFocus value={email} onChange={(e) => setEmail(e.target.value)} aria-invalid={!!problem} aria-describedby={problem ? 'reset-email-error' : undefined} placeholder="Twój adres e-mail" className={cx(inputCls, problem && '!border-danger')} /></Field>
        <FormMessage field id="reset-email-error">{problem}</FormMessage>
      </div>
      <FormMessage>{result && !result.ok ? (result.reason && USER_REASON[result.reason]) || describeError(result.code) : null}</FormMessage>
      <div className="flex justify-end gap-3"><Btn variant="ghost" onClick={onClose}>Anuluj</Btn><Btn type="submit" disabled={busy}>{busy ? 'Wysyłanie…' : 'Wyślij link'}</Btn></div>
    </form>}
  </Modal>
}
