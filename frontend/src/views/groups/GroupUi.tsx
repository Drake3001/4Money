import { useEffect, useRef, useState } from 'react'
import { Upload, UsersRound, X } from 'lucide-react'
import { useApp } from '../../context'
import { useGroups } from '../../groupsContext'
import { describeError } from '../../api/errors'
import { ERROR_TEXT, ICON_TYPES, MAX_ICON_BYTES } from '../../mocks/groups'
import { Btn, cx } from '../../ui'

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
  if (!code || code === 501) return null // 501 is handled by the shared "Usługa tymczasowo niedostępna" dialog
  return (
    <div role="alert" className="flex flex-wrap items-center gap-3 rounded-xl bg-danger-soft p-3 text-sm text-danger">
      <p className="min-w-0 flex-1">{ERROR_TEXT[code] ?? describeError(code)}</p>
      {code === 401 && onRetryLogin !== false && <button onClick={() => navigate({ name: 'login' })} className="font-semibold underline">Przejdź do logowania</button>}
    </div>
  )
}

/** Value edited by the picker. `icon` (preset key), `file` and `removeIcon` are mutually exclusive, as required by the API. */
export type IconValue = { icon: string | null; file: File | null; previewUrl: string | null; removeIcon: boolean }

export function IconPicker({ value, onChange, existing }: { value: IconValue; onChange: (v: IconValue) => void; existing?: { id: string; icon: string | null; hasCustomIcon: boolean } }) {
  const { iconPresets } = useGroups()
  const file = useRef<HTMLInputElement>(null)
  const [err, setErr] = useState<number | null>(null)
  useEffect(() => () => { if (value.previewUrl) URL.revokeObjectURL(value.previewUrl) }, [value.previewUrl])

  const upload = (f?: File) => {
    if (!f) return
    setErr(null)
    const code = !ICON_TYPES.includes(f.type) ? 415 : f.size > MAX_ICON_BYTES ? 413 : null
    if (file.current) file.current.value = ''
    if (code) return setErr(code)
    onChange({ icon: null, file: f, previewUrl: URL.createObjectURL(f), removeIcon: false })
  }

  // What is shown right now: the new selection, or the stored icon unless it is being removed.
  const shown: IconSource = value.previewUrl || value.icon || value.removeIcon || !existing
    ? { icon: value.icon, previewUrl: value.previewUrl }
    : { id: existing.id, icon: existing.icon, hasCustomIcon: existing.hasCustomIcon }
  const has = !!value.icon || !!value.file || (!!existing && !value.removeIcon && (!!existing.icon || existing.hasCustomIcon))
  return (
    <div>
      <span className="mb-2 block text-xs font-semibold uppercase tracking-wide text-ink2">Ikona</span>
      <div className="flex flex-wrap items-center gap-4">
        <GroupIcon group={shown} size={64} />
        <div className="flex flex-wrap gap-2">
          <input ref={file} type="file" accept={ICON_TYPES.join(',')} className="hidden" onChange={(e) => upload(e.target.files?.[0])} aria-label="Wybierz plik ikony" />
          <Btn variant="ghost" onClick={() => file.current?.click()}><Upload size={16} />Wgraj własną ikonę</Btn>
          {has && <Btn variant="soft" onClick={() => { setErr(null); onChange({ icon: null, file: null, previewUrl: null, removeIcon: !!existing }) }}><X size={16} />Usuń ikonę</Btn>}
        </div>
      </div>
      <p className="mt-2 text-xs text-ink2">PNG, JPEG lub WebP, maksymalnie 5 MiB. Wybór gotowej ikony zastępuje własną i odwrotnie. Bez ikony pokażemy ikonę domyślną.</p>
      <div className="mt-3 grid grid-cols-5 gap-2 sm:grid-cols-10" role="radiogroup" aria-label="Gotowe ikony">
        {iconPresets.map((p) => {
          const on = value.icon === p.key
          return (
            <button key={p.key} type="button" role="radio" aria-checked={on} title={p.key} aria-label={`Ikona ${p.key}`} onClick={() => { setErr(null); onChange({ icon: p.key, file: null, previewUrl: null, removeIcon: false }) }}
              className={cx('flex aspect-square items-center justify-center overflow-hidden rounded-xl border p-1.5 transition', on ? 'border-personal bg-mint-soft' : 'border-line bg-white hover:border-ink/30')}>
              <img src={p.url} alt="" className="h-full w-full object-contain" />
            </button>
          )
        })}
      </div>
      {err && <div className="mt-3"><ErrorBox code={err} /></div>}
    </div>
  )
}

export const dateOnly = (iso: string) => new Date(iso).toLocaleDateString('pl-PL', { day: 'numeric', month: 'long', year: 'numeric' })
