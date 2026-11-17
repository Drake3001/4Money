import { useEffect, useState, type ReactNode } from 'react'
import { Modal, cx } from '../ui'

// Keep the space even when empty, so validation never moves the form.
export default function AuthMessage({ id, children, banner = false }: { id?: string; children?: ReactNode; banner?: boolean }) {
  const [open, setOpen] = useState(false)
  useEffect(() => { setOpen(!!children) }, [children])
  if (banner) return open && children ? <Modal title="Nie udało się wykonać operacji" onClose={() => setOpen(false)}><p role="alert" className="text-sm leading-relaxed text-danger">{children}</p></Modal> : null
  return <div className="auth-field-slot">
    <p id={id} role={children ? 'alert' : undefined} className={cx('text-xs leading-4 text-danger', !children && 'invisible')}>
      {children || '\u00a0'}
    </p>
  </div>
}
