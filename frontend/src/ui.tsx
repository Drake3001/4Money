import { createContext, useContext, useEffect, useId, useState, type ComponentProps, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Eye, EyeOff, X } from 'lucide-react'

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
    <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
      <h3 className="font-display text-xl font-semibold">{children}</h3>
      {aside}
    </div>
  )
}

export function PageTitle({ title, sub, aside }: { title: string; sub?: string; aside?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div className="min-w-0">
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

export const inputCls = 'w-full rounded-xl border border-line bg-white px-3 py-2.5 text-sm text-ink placeholder:text-ink2/60 focus:border-ink focus:outline-none'
export function PasswordInput({ className, id, ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  const [visible, setVisible] = useState(false)
  const generatedId = useId()
  const inputId = id ?? generatedId
  return <span className="relative block">
    <input {...props} id={inputId} type={visible ? 'text' : 'password'} className={cx(inputCls, 'pr-12', className)} />
    <button type="button" disabled={props.disabled} onClick={() => setVisible((value) => !value)} aria-label={visible ? 'Ukryj hasło' : 'Pokaż hasło'} aria-pressed={visible} aria-controls={inputId} className="absolute inset-y-0 right-0 flex w-12 items-center justify-center rounded-r-xl text-ink2/60 transition hover:text-ink disabled:cursor-not-allowed disabled:opacity-50">
      {visible ? <EyeOff size={18} /> : <Eye size={18} />}
    </button>
  </span>
}
export function Field({ label, children, hint, error, reserveHint = false }: { label: string; children: ReactNode; hint?: string; error?: string; reserveHint?: boolean }) {
  const invalid = !!error || (reserveHint && !!hint)
  const message = error || hint
  return (
    <label className={cx('block', invalid && '[&_input]:!border-danger [&_select]:!border-danger [&_textarea]:!border-danger')}>
      <span className="mb-1 block text-xs font-semibold uppercase tracking-wide text-ink2">{label}</span>
      {children}
      {reserveHint ? <span role={invalid ? 'alert' : undefined} className={cx('mt-1 block h-7 overflow-y-auto text-xs leading-[1.25]', invalid ? 'text-danger' : 'text-ink2')}>{message}</span> : message && <span role={invalid ? 'alert' : undefined} className={cx('mt-1 block text-xs', invalid ? 'text-danger' : 'text-ink2')}>{message}</span>}
    </label>
  )
}

const ModalContext = createContext(false)

export function MessageSlot({ children, field = false }: { children?: ReactNode; field?: boolean }) {
  return <div className={field ? 'h-8 min-w-0 pt-1' : 'h-14 min-w-0'}>
    <div className="max-h-full overflow-y-auto [overflow-wrap:anywhere] [&>[role=alert]]:py-2">{children}</div>
  </div>
}

export function FormMessage({ id, children, field = false }: { id?: string; children?: ReactNode; field?: boolean }) {
  return <MessageSlot field={field}>
    {children && <p id={id} role="alert" className={field ? 'text-xs leading-[1.25] text-danger !py-0' : 'rounded-xl bg-danger-soft px-3 py-2 text-sm leading-5 text-danger'}>{children}</p>}
  </MessageSlot>
}

export function ModalErrorSlot({ children }: { children?: ReactNode }) {
  const inModal = useContext(ModalContext)
  return inModal ? <MessageSlot>{children}</MessageSlot> : <>{children}</>
}

export function Modal({ title, onClose, children, className }: { title: string; onClose: () => void; children: ReactNode; className?: string }) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose])
  // Keep the overlay outside scrolling/animated ancestors so fixed positioning uses the viewport.
  if (typeof document === 'undefined') return null
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink/40 p-4" onMouseDown={onClose}>
      <div role="dialog" aria-modal="true" aria-label={title} onMouseDown={(e) => e.stopPropagation()} className={cx('animate-rise soft-scroll max-h-[calc(100dvh-2rem)] w-full min-w-0 max-w-xl shrink-0 overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl [overflow-wrap:anywhere] [scrollbar-gutter:stable]', className)}>
        <div className="mb-5 flex shrink-0 items-start justify-between gap-3">
          <h2 className="min-w-0 font-display text-2xl font-semibold">{title}</h2>
          <button aria-label="Zamknij" onClick={onClose} className="shrink-0 rounded-lg p-1.5 text-ink2 hover:bg-wash"><X size={20} /></button>
        </div>
        <ModalContext.Provider value={true}>{children}</ModalContext.Provider>
      </div>
    </div>,
    document.body,
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

export function Pager({ page, onPage, itemSummary }: { page: { pageNumber: number; totalPages: number; totalCount: number; pageSize: number; hasPreviousPage: boolean; hasNextPage: boolean }; onPage: (n: number) => void; itemSummary?: string }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-line px-3 py-3 text-sm">
      <span className="text-ink2">Strona {page.pageNumber} z {page.totalPages} · {itemSummary ?? `wyników: ${page.totalCount}`} · {page.pageSize} na stronę</span>
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
