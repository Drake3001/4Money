import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Upload } from 'lucide-react'
import { usersApi } from '../api/users'
import { useRemote } from '../api/hooks'
import { describeError } from '../api/errors'
import { useApp } from '../context'
import { useUser } from '../userContext'
import { Btn, Modal, cx } from '../ui'
import { ProfileIcon } from './ProfileIcon'
import { ICON_IMAGE_ACCEPT, ICON_TOO_LARGE, prepareIconImage } from '../shared/iconImage'
import ProfileMessage from './ProfileMessage'

export function ChangeProfileIcon({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const { showToast } = useApp()
  const p = user.profile
  const presets = useRemote(usersApi.presets, [])
  const input = useRef<HTMLInputElement>(null)
  const [selection, setSelection] = useState<{ kind: 'keep' } | { kind: 'remove' } | { kind: 'preset'; key: string; url: string } | { kind: 'file'; file: File }>({ kind: 'keep' })
  const [preview, setPreview] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [preparing, setPreparing] = useState(false)
  const locked = busy || preparing
  const file = selection.kind === 'file' ? selection.file : null
  useEffect(() => {
    if (!file) { setPreview(null); return }
    const url = URL.createObjectURL(file)
    setPreview(url)
    return () => URL.revokeObjectURL(url)
  }, [file])
  const url = selection.kind === 'remove' ? null : selection.kind === 'preset' ? selection.url : selection.kind === 'file' ? preview : user.profileImageUrl
  const close = () => { if (!locked) onClose() }
  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (locked || selection.kind === 'keep') return
    setBusy(true); setError(null)
    const r = await user.updateProfile({
      login: p.login, name: p.name, surname: p.surname, phone: p.phone ?? '', birthDate: p.birthDate,
      ...(selection.kind === 'remove' ? { removeIcon: true } : selection.kind === 'file' ? { iconFile: selection.file } : { icon: selection.key }),
    })
    setBusy(false)
    if (r.ok) { showToast('Zapisano ikonę profilu'); onClose() }
    else setError(describeError(r.code, { 400: 'Nie można zapisać tej ikony. Sprawdź plik lub wybierz inny preset.', 413: ICON_TOO_LARGE, 415: 'Wybierz obsługiwany format pliku: JPEG, PNG, WebP, HEIC/HEIF.' }))
  }
  return (
    <Modal title="Zmień ikonę profilu" onClose={close}>
      <form onSubmit={submit} className="space-y-3">
        <div className="flex justify-center py-3">
          <ProfileIcon name={p.name} surname={p.surname} url={url} size={88} />
        </div>
        <fieldset disabled={locked} aria-label="Zmiana ikony profilu" className="space-y-3">
          <input ref={input} type="file" accept={ICON_IMAGE_ACCEPT} aria-label="Wybierz grafikę profilu" className="sr-only" onChange={async (e) => {
            const selected = e.target.files?.[0]
            e.target.value = ''
            if (!selected) return
            setError(null); setPreparing(true)
            try { setSelection({ kind: 'file', file: await prepareIconImage(selected) }) }
            catch (error) { setError(error instanceof Error ? error.message : 'Nie udało się przygotować zdjęcia.') }
            finally { setPreparing(false) }
          }} />
          <div className="flex flex-wrap justify-center gap-2">
            <Btn variant="ghost" onClick={() => input.current?.click()} disabled={locked}><Upload size={16} />Dodaj grafikę</Btn>
            <Btn variant="soft" disabled={locked} onClick={() => { setSelection({ kind: 'remove' }); setError(null) }}>Domyślna ikona</Btn>
          </div>
          <div className="h-5">{preparing && <p className="text-center text-xs text-ink2" role="status">Przygotowywanie zdjęcia…</p>}</div>
        </fieldset>
        <fieldset disabled={locked} className="h-32 min-w-0 overflow-y-auto">
          <legend className="mb-2 text-sm font-semibold">Ikony 4Money</legend>
          {presets.loading ? <p className="text-sm text-ink2" role="status">Wczytywanie ikon…</p>
            : presets.error !== null ? <div role="alert" className="space-y-2"><p className="text-sm text-danger">Nie udało się wczytać ikon 4Money.</p><Btn variant="ghost" onClick={presets.reload} disabled={busy}>Spróbuj ponownie</Btn></div>
            : !presets.data?.length ? <p className="rounded-xl bg-wash p-3 text-sm text-ink2">Ikony 4Money będą dostępne wkrótce. Teraz możesz dodać własną grafikę lub wybrać domyślną ikonę.</p>
            : <div className="grid grid-cols-4 gap-2 sm:grid-cols-6" role="radiogroup" aria-label="Ikony 4Money profilu">
              {presets.data.map((preset, index) => (
                <button key={preset.key} type="button" role="radio" aria-checked={selection.kind === 'preset' ? selection.key === preset.key : selection.kind === 'keep' && p.icon === preset.key} aria-label={`Ikona ${index + 1}`} onClick={() => { setSelection({ kind: 'preset', key: preset.key, url: preset.url }); setError(null) }}
                  className={cx('aspect-square overflow-hidden rounded-xl border-2 p-1', (selection.kind === 'preset' ? selection.key === preset.key : selection.kind === 'keep' && p.icon === preset.key) ? 'border-ink bg-mint-soft' : 'border-line hover:border-ink')}>
                  <img src={preset.url} alt="" className="h-full w-full rounded-lg object-cover" />
                </button>
              ))}
            </div>}
        </fieldset>
        <ProfileMessage>{error}</ProfileMessage>
        <div className="flex justify-end gap-2"><Btn variant="ghost" onClick={close} disabled={locked}>Anuluj</Btn><Btn type="submit" disabled={locked || selection.kind === 'keep'}>{busy ? 'Zapisywanie…' : 'Zapisz ikonę'}</Btn></div>
      </form>
    </Modal>
  )
}

export function ChangePassword({ onClose }: { onClose: () => void }) {
  const user = useUser()
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const close = () => { if (!busy) onClose() }
  const send = async () => {
    if (busy) return
    setBusy(true); setError(null)
    const r = await user.forgotPassword(user.profile.email)
    setBusy(false)
    if (r.ok) setSent(true)
    else setError(describeError(r.code, { 429: 'Zbyt wiele prób. Spróbuj ponownie za chwilę.' }))
  }
  return (
    <Modal title={user.profile.hasPassword ? 'Zmień hasło' : 'Ustaw hasło'} onClose={close}>
      <div className="space-y-2">
        {sent ? <div role="status" className="space-y-3 rounded-xl bg-mint-soft p-4 text-sm">
          <p>Sprawdź skrzynkę:<strong className="mt-1 block break-all">{user.profile.email}</strong></p>
          <p>Jeśli konto spełnia warunki, otrzymasz link do ustawienia nowego hasła.</p>
          <p className="text-ink2">Sprawdź również folder Spam lub Oferty. Link jest ważny przez ograniczony czas.</p>
        </div> : <>
          <p className="text-sm text-ink2">Wyślemy link do ustawienia nowego hasła na adres:<strong className="mt-1 block break-all text-ink">{user.profile.email}</strong></p>
          {!user.profile.isEmailConfirmed && <p className="rounded-xl bg-warn-soft p-3 text-sm">Najpierw potwierdź adres e-mail konta, aby otrzymać link.</p>}
          <p className="text-xs text-ink2">Po zapisaniu nowego hasła trzeba będzie zalogować się ponownie.{user.profile.isGoogleLinked && ' Hasło do Twojego prywatnego konta Google pozostanie bez zmian.'}</p>
        </>}
        {!sent && <ProfileMessage>{error}</ProfileMessage>}
        <div className="flex justify-end gap-2">
          <Btn variant="ghost" onClick={close} disabled={busy}>{sent ? 'Zamknij' : 'Anuluj'}</Btn>
          {!sent && <Btn onClick={send} disabled={busy || !user.profile.isEmailConfirmed}>{busy ? 'Wysyłanie…' : 'Wyślij link'}</Btn>}
        </div>
      </div>
    </Modal>
  )
}
