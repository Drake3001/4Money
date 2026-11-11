import { useEffect, type ReactNode } from 'react'
import { X } from 'lucide-react'

export const cx = (...a: (string | false | null | undefined)[]) => a.filter(Boolean).join(' ')

type Tone = 'ok' | 'warn' | 'danger' | 'info' | 'neutral'
const TONE: Record<Tone, string> = {
  ok: 'bg-safe-soft text-safe',
  warn: 'bg-warn-soft text-warn',
  danger: 'bg-danger-soft text-danger',
  info: 'bg-shared-soft text-shared-deep',
  neutral: 'bg-wash text-ink2',
}
export function Badge({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return <span className={cx('inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold', TONE[tone])}>{children}</span>
}
export const statusTone = (s: string): Tone =>
  /zakończone|aktywn|zaksięgowana/i.test(s) ? 'ok' : /trakcie|oczekując/i.test(s) ? 'info' : /uwagi|wygasa/i.test(s) ? 'warn' : 'neutral'

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={cx('rounded-2xl border border-line bg-white p-5', className)}>{children}</div>
}
export function H({ children, aside }: { children: ReactNode; aside?: ReactNode }) {
  return (
    <div className="mb-4 flex items-center justify-between gap-3">
      <h3 className="font-display text-xl font-semibold">{children}</h3>
      {aside}
    </div>
  )
}

export function PageTitle({ title, sub, aside }: { title: string; sub?: string; aside?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="font-display text-2xl font-bold">{title}</h1>
        {sub && <p className="mt-1 text-ink2">{sub}</p>}
      </div>
      {aside}
    </div>
  )
}

export function Segmented<T extends string>({ value, onChange, options, className }: { value: T; onChange: (v: T) => void; options: { v: T; label: string; disabled?: boolean }[]; className?: string }) {
  return (
    <div role="tablist" className={cx('soft-scroll inline-flex max-w-full overflow-x-auto rounded-xl bg-wash p-1', className)}>
      {options.map((o) => (
        <button
          key={o.v}
          role="tab"
          aria-selected={value === o.v}
          disabled={o.disabled}
          onClick={() => onChange(o.v)}
          className={cx('min-h-9 shrink-0 whitespace-nowrap rounded-lg px-3.5 py-1.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-40', value === o.v ? 'bg-white text-ink shadow-sm' : 'text-ink2 hover:text-ink')}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

export function Btn({ children, onClick, variant = 'primary', disabled, className = '', type = 'button' }: { children: ReactNode; onClick?: () => void; variant?: 'primary' | 'ghost' | 'soft' | 'danger'; disabled?: boolean; className?: string; type?: 'button' | 'submit' }) {
  const v = {
    primary: 'bg-ink text-white hover:bg-[#12285a]',
    ghost: 'border border-line bg-white text-ink hover:bg-wash',
    soft: 'bg-wash text-ink hover:bg-[#e8eef3]',
    danger: 'border border-line bg-white text-danger hover:bg-danger-soft',
  }[variant]
  return (
    <button type={type} onClick={onClick} disabled={disabled} className={cx('inline-flex min-h-10 items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-45', v, className)}>
      {children}
    </button>
  )
}

export const inputCls = 'w-full rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink placeholder:text-ink2/60 focus:border-personal focus:outline-none'
export function Field({ label, children, hint }: { label: string; children: ReactNode; hint?: string }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-xs text-ink2">{hint}</span>}
    </label>
  )
}

export function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: ReactNode }) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose])
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-ink/40 sm:items-center sm:p-4" onMouseDown={onClose}>
      <div role="dialog" aria-label={title} onMouseDown={(e) => e.stopPropagation()} className="animate-rise soft-scroll max-h-[92vh] w-full max-w-lg overflow-y-auto rounded-t-3xl bg-white p-6 shadow-2xl sm:rounded-2xl">
        <div className="mb-5 flex items-start justify-between">
          <h2 className="font-display text-2xl font-semibold">{title}</h2>
          <button aria-label="Zamknij" onClick={onClose} className="rounded-lg p-1.5 text-ink2 hover:bg-wash"><X size={20} /></button>
        </div>
        {children}
      </div>
    </div>
  )
}

export function KeyValue({ rows }: { rows: [string, ReactNode][] }) {
  return (
    <dl className="divide-y divide-line">
      {rows.map(([k, v]) => (
        <div key={k} className="flex flex-wrap items-center justify-between gap-2 py-3">
          <dt className="text-sm text-ink2">{k}</dt>
          <dd className="text-sm font-semibold">{v}</dd>
        </div>
      ))}
    </dl>
  )
}

export function Table({ head, children }: { head: string[]; children: ReactNode }) {
  return (
    <div className="soft-scroll overflow-x-auto">
      <table className="w-full min-w-[560px] text-left text-sm">
        <thead>
          <tr className="border-b border-line text-xs uppercase tracking-wide text-ink2">
            {head.map((h) => <th key={h} className="px-3 py-2.5 font-semibold">{h}</th>)}
          </tr>
        </thead>
        <tbody className="divide-y divide-line">{children}</tbody>
      </table>
    </div>
  )
}
export const Td = ({ children, className = '' }: { children?: ReactNode; className?: string }) => <td className={cx('px-3 py-3 align-middle', className)}>{children}</td>

export function Pager({ page, onPage }: { page: { pageNumber: number; totalPages: number; totalCount: number; pageSize: number; hasPreviousPage: boolean; hasNextPage: boolean }; onPage: (n: number) => void }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-3 py-3 text-sm">
      <span className="text-ink2">Strona {page.pageNumber} z {page.totalPages} · wyników: {page.totalCount} · {page.pageSize} na stronę</span>
      <div className="flex gap-2">
        <Btn variant="ghost" disabled={!page.hasPreviousPage} onClick={() => onPage(page.pageNumber - 1)} className="!min-h-9 !px-3">Poprzednia</Btn>
        <Btn variant="ghost" disabled={!page.hasNextPage} onClick={() => onPage(page.pageNumber + 1)} className="!min-h-9 !px-3">Następna</Btn>
      </div>
    </div>
  )
}

// One shared dialog for every action backed by an endpoint that is not implemented yet (501).
export function UnavailableDialog({ onClose }: { onClose: () => void }) {
  return (
    <Modal title="Usługa tymczasowo niedostępna" onClose={onClose}>
      <p className="text-ink2">Ta funkcjonalność nie jest jeszcze dostępna. Spróbuj ponownie później.</p>
      <div className="mt-6 flex justify-end"><Btn onClick={onClose}>Zamknij</Btn></div>
    </Modal>
  )
}
