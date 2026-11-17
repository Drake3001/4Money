import { useState } from 'react'
import { cx } from '../ui'

export function ProfileIcon({ url, name, surname, size = 36, className }: { url?: string | null; name: string; surname: string; size?: number; className?: string }) {
  const [failedUrl, setFailedUrl] = useState<string | null>(null)
  const initials = `${Array.from(name.trim())[0] ?? ''}${Array.from(surname.trim())[0] ?? ''}`.toLocaleUpperCase('pl')
  return (
    <span className={cx('inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-mint font-semibold text-ink', className)} style={{ width: `${size / 16}rem`, height: `${size / 16}rem`, containerType: 'inline-size' }}>
      {url && url !== failedUrl
        ? <img src={url} alt={`${name} ${surname}`} className="h-full w-full object-cover" onError={() => setFailedUrl(url)} />
        : <span aria-label={`Inicjały: ${initials}`} className="text-[36cqw] leading-none">{initials || '?'}</span>}
    </span>
  )
}
