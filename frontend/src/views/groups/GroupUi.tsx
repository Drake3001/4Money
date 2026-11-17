import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Upload, UsersRound } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import { describeError } from '../../api/errors'
import { ERROR_TEXT } from '../../mocks/groups'
import { ICON_IMAGE_ACCEPT, prepareIconImage } from '../../shared/iconImage'
import { Badge, Btn, ModalErrorSlot, cx } from '../../ui'

export function GroupRoleBadge({ isAdmin }: { isAdmin: boolean }) {
  return isAdmin ? <Badge tone="info">Administrator</Badge> : <span className="inline-flex items-center rounded-full bg-shared-soft/50 px-2.5 py-0.5 text-xs font-semibold text-shared">Członek</span>
}

export function GroupMembershipBadge({ active, children }: { active: boolean; children?: ReactNode }) {
  return <Badge tone={active ? 'ok' : 'danger'}>{children ?? (active ? 'Aktywne' : 'Byłe')}</Badge>
}

/** What the icon looks like for a group: preset key (GroupResponse.icon), custom image flag, and an optional local preview. */
export type IconSource = { id?: string; icon: string | null; hasCustomIcon?: boolean; previewUrl?: string | null }

// Preview (just chosen file) -> preset image (GET /api/groups/icon-presets) -> custom image (GET /api/groups/{id}/icon) -> fallback.
// A failing image falls back and never hides the group.
export function GroupIcon({ group, size = 44 }: { group: IconSource; size?: number }) {
  const { iconPresets, fetchIconUrl } = useGroups()
  const [broken, setBroken] = useState<string | null>(null)
  const [custom, setCustom] = useState<string | null>(null)
  const preset = group.icon ? iconPresets.find((p) => p.key === group.icon) : undefined
  const wantCustom = !group.previewUrl && !preset && !!group.hasCustomIcon && !!group.id
  useEffect(() => {
    setCustom(null)
    if (wantCustom) void fetchIconUrl(group.id!).then(setCustom)
  }, [wantCustom, group.id]) // eslint-disable-line react-hooks/exhaustive-deps
  const src = group.previewUrl ?? preset?.url ?? custom
  const showImg = !!src && broken !== src
  return (
    <span className="flex shrink-0 items-center justify-center overflow-hidden rounded-xl bg-shared-soft text-shared-deep" style={{ width: size, height: size }}>
      {showImg ? <img src={src!} alt="" onError={() => setBroken(src)} className="h-full w-full object-cover" /> : <UsersRound size={size * 0.5} />}
    </span>
  )
}

export function ErrorBox({ code, onRetryLogin }: { code: number | null; onRetryLogin?: boolean }) {
  const { navigate } = useApp()
  return <ModalErrorSlot>{code && code !== 501 ? (
    <div role="alert" className="flex flex-wrap items-center gap-3 rounded-xl bg-danger-soft p-3 text-sm text-danger">
      <p className="min-w-0 flex-1">{ERROR_TEXT[code] ?? describeError(code)}</p>
      {code === 401 && onRetryLogin !== false && <button onClick={() => navigate({ name: 'login' })} className="font-semibold underline">Przejdź do logowania</button>}
    </div>
  ) : null}</ModalErrorSlot>
}

/** Value edited by the picker. `icon` (preset key), `file` and `removeIcon` are mutually exclusive, as required by the API. */
export type IconValue = { icon: string | null; file: File | null; previewUrl: string | null; removeIcon: boolean }

export function IconPicker({ value, onChange, onPreparingChange, existing }: { value: IconValue; onChange: (v: IconValue) => void; onPreparingChange: (preparing: boolean) => void; existing?: { id: string; icon: string | null; hasCustomIcon: boolean } }) {
  const { iconPresets } = useGroups()
  const file = useRef<HTMLInputElement>(null)
  const [err, setErr] = useState<string | null>(null)
  const [preparing, setPreparing] = useState(false)
  const mounted = useRef(true)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => () => { if (value.previewUrl) URL.revokeObjectURL(value.previewUrl) }, [value.previewUrl])

  const upload = async (f?: File) => {
    if (file.current) file.current.value = ''
    if (!f || preparing) return
    setErr(null); setPreparing(true); onPreparingChange(true)
    try {
      const prepared = await prepareIconImage(f)
      if (mounted.current) onChange({ icon: null, file: prepared, previewUrl: URL.createObjectURL(prepared), removeIcon: false })
    } catch (error) {
      if (mounted.current) setErr(error instanceof Error ? error.message : 'Nie udało się przygotować zdjęcia.')
    } finally {
      if (mounted.current) { setPreparing(false); onPreparingChange(false) }
    }
  }

  // What is shown right now: the new selection, or the stored icon unless it is being removed.
  const shown: IconSource = value.previewUrl || value.icon || value.removeIcon || !existing
    ? { icon: value.icon, previewUrl: value.previewUrl }
    : { id: existing.id, icon: existing.icon, hasCustomIcon: existing.hasCustomIcon }
  return (
    <fieldset disabled={preparing} aria-label="Ikona grupy" aria-busy={preparing} className="space-y-5">
      <span className="mb-2 block text-xs font-semibold uppercase tracking-wide text-ink2">Ikona</span>
      <div className="flex justify-center py-3">
        <GroupIcon group={shown} size={88} />
      </div>
      <div className="flex flex-wrap justify-center gap-2">
        <input ref={file} type="file" accept={ICON_IMAGE_ACCEPT} className="hidden" onChange={(e) => { void upload(e.target.files?.[0]) }} aria-label="Wybierz plik ikony" />
        <Btn variant="ghost" disabled={preparing} onClick={() => file.current?.click()}><Upload size={16} />Dodaj grafikę</Btn>
        <Btn variant="soft" disabled={preparing} onClick={() => { setErr(null); onChange({ icon: null, file: null, previewUrl: null, removeIcon: !!existing }) }}>Domyślna ikona</Btn>
      </div>
      {preparing && <p className="text-center text-xs text-ink2" role="status">Przygotowywanie zdjęcia…</p>}
      <fieldset>
        <legend className="mb-2 text-sm font-semibold">Ikony 4Money</legend>
        {!iconPresets.length ? <p className="rounded-xl bg-wash p-3 text-sm text-ink2">Ikony 4Money będą dostępne wkrótce. Teraz możesz dodać własną grafikę lub wybrać domyślną ikonę.</p> : (
          <div className="grid grid-cols-5 gap-2 sm:grid-cols-10" role="radiogroup" aria-label="Ikony 4Money">
            {iconPresets.map((p) => {
              const on = value.icon === p.key
              return (
                <button key={p.key} type="button" role="radio" aria-checked={on} title={p.key} aria-label={`Ikona ${p.key}`} onClick={() => { setErr(null); onChange({ icon: p.key, file: null, previewUrl: null, removeIcon: false }) }}
                  className={cx('flex aspect-square items-center justify-center overflow-hidden rounded-xl border p-1.5 transition', on ? 'border-ink bg-mint-soft' : 'border-line bg-white hover:border-ink/30')}>
                  <img src={p.url} alt="" className="h-full w-full object-contain" />
                </button>
              )
            })}
          </div>
        )}
      </fieldset>
      {err && <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">{err}</p>}
    </fieldset>
  )
}

export const dateOnly = (iso: string) => new Date(iso).toLocaleDateString('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' })
