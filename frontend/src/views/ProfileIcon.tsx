import { Coffee, Heart, Rocket, Smile, Star, UserRound, type LucideIcon } from 'lucide-react'
import { cx } from '../ui'

// The backend stores the icon as an opaque string; these ids are what the frontend writes and understands.
const MAP: Record<string, LucideIcon> = { user: UserRound, smile: Smile, heart: Heart, star: Star, rocket: Rocket, coffee: Coffee }
export const ICON_LABELS: Record<string, string> = { user: 'Osoba', smile: 'Uśmiech', heart: 'Serce', star: 'Gwiazdka', rocket: 'Rakieta', coffee: 'Kawa' }

export function ProfileIcon({ icon, size = 36, className }: { icon: string | null; size?: number; className?: string }) {
  const I = (icon && MAP[icon]) || UserRound // unknown/empty icon falls back to the default
  return <span className={cx('inline-flex shrink-0 items-center justify-center rounded-full bg-personal text-white', className)} style={{ width: size, height: size }}><I size={size * 0.5} /></span>
}
