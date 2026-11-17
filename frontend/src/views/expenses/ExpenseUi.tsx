import { useEffect, useState } from 'react'
import {
  BadgePercent, Bus, Car, CircleHelp, Clapperboard, CupSoda, Dumbbell, FileText, GraduationCap,
  HeartPulse, House, Package, PawPrint, Plane, Receipt, Recycle, Shirt, ShoppingBasket,
  ShoppingCart, Smartphone, Sparkles, SprayCan, Tag, Ticket, Utensils, Wine, Wrench, type LucideIcon,
} from 'lucide-react'
import { useApp } from '../../context'
import { useExpenses } from '../../expensesContext'
import { category, mockName, type Receipt as ReceiptT } from '../../mocks/expenses'
import { ModalErrorSlot, cx } from '../../ui'
import { describeError } from '../../api/errors'
export { TransactionModal } from '../BankTransactionDetails'
import { fileTooLarge } from '../../shared/fileSize'
import { MAX_RECEIPT_BYTES } from '../../mocks/expenses'
import { receiptPreview } from '../../shared/receiptPreview'

const ICONS: Record<string, LucideIcon> = {
  'badge-percent': BadgePercent, bus: Bus, car: Car, 'circle-help': CircleHelp, dumbbell: Dumbbell,
  'graduation-cap': GraduationCap, 'heart-pulse': HeartPulse, home: House, package: Package,
  'paw-print': PawPrint, plane: Plane, receipt: Receipt, recycle: Recycle, shirt: Shirt,
  'shopping-cart': ShoppingCart, smartphone: Smartphone, sparkles: Sparkles, 'spray-can': SprayCan,
  ticket: Ticket, utensils: Utensils, wine: Wine, wrench: Wrench,
  basket: ShoppingBasket, cup: CupSoda, house: House, heart: HeartPulse, clapper: Clapperboard, tag: Tag, help: CircleHelp,
}

export function CatBadge({ id, compact }: { id?: string | null; compact?: boolean }) {
  const { categories, categoriesLoading, categoriesError } = useExpenses()
  const c = category(id, categories)
  const missing = !!id && !categories.some((entry) => entry.id === id)
  const I = ICONS[c.icon] ?? Tag
  return (
    <span className={cx('inline-flex items-center gap-1.5 rounded-full text-xs font-medium', c.isSystemGenerated ? 'bg-wash text-ink2' : 'bg-mint-soft text-ink', compact ? 'px-2 py-0.5' : 'px-2.5 py-1')} title={c.description}>
      <I size={13} className={c.isSystemGenerated ? undefined : 'text-personal'} />{missing ? (categoriesLoading ? 'Ładowanie kategorii…' : categoriesError !== null ? 'Kategoria niedostępna' : 'Nieznane') : c.name}
    </span>
  )
}

export function CategoryLoadError() {
  const { categoriesError, reloadCategories } = useExpenses()
  if (categoriesError === null) return null
  return <p role="alert" className="rounded-xl bg-danger-soft p-3 text-sm text-danger">Nie udało się pobrać kategorii. <button type="button" className="font-semibold underline" onClick={reloadCategories}>Spróbuj ponownie</button></p>
}

export const EXP_ERRORS: Record<number, string> = {
  0: 'Brak połączenia z serwerem. Sprawdź połączenie i spróbuj ponownie.',
  400: 'Dane formularza lub filtrów są nieprawidłowe. Sprawdź je i spróbuj ponownie.',
  401: 'Twoja sesja wygasła. Zaloguj się ponownie.',
  403: 'Możesz przeglądać ten wydatek, ale nie możesz go zmieniać ani usuwać.',
  404: 'Nie znaleziono wydatku, paragonu lub innego zasobu. Mógł zostać usunięty.',
  409: 'Wydatek został zmieniony w innym miejscu albo wybrane źródło jest już użyte. Odśwież dane i spróbuj ponownie.',
  413: fileTooLarge(MAX_RECEIPT_BYTES, 'paragonu'),
  415: 'Wybierz obsługiwany format pliku: JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP lub PDF.',
  422: 'Nie udało się odczytać z tego dokumentu przydatnych danych. Spróbuj z wyraźniejszym zdjęciem paragonu albo uzupełnij dane ręcznie.',
  502: 'Usługa analizy paragonów zwróciła nieprawidłową odpowiedź. Spróbuj ponownie później.',
  503: 'Usługa jest chwilowo niedostępna. Spróbuj ponownie za chwilę.',
  504: 'Analiza paragonu trwa zbyt długo. Spróbuj ponownie.',
  500: 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie później.',
}

export function ExpErrorBox({ code, children, message }: { code: number | null; children?: React.ReactNode; message?: string }) {
  const { navigate } = useApp()
  return <ModalErrorSlot>{code && code !== 501 ? (
    <div role="alert" className="space-y-2 rounded-xl bg-danger-soft p-3 text-sm text-danger">
      <p>{message ?? EXP_ERRORS[code] ?? describeError(code)}</p>
      {code === 401 && <button onClick={() => navigate({ name: 'login' })} className="font-semibold underline">Przejdź do logowania</button>}
      {children}
    </div>
  ) : null}</ModalErrorSlot>
}

export function ReceiptThumb({ r, size = 'md' }: { r: Pick<ReceiptT, 'previewUrl' | 'mimeType' | 'fileName'>; size?: 'md' | 'lg' }) {
  return <ReceiptImage key={r.previewUrl} r={r} size={size} />
}

function ReceiptImage({ r, size }: { r: Pick<ReceiptT, 'previewUrl' | 'mimeType' | 'fileName'>; size: 'md' | 'lg' }) {
  const [broken, setBroken] = useState(false)
  const [convert, setConvert] = useState(false)
  const [convertedUrl, setConvertedUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!convert || !r.previewUrl) return
    const controller = new AbortController()
    let objectUrl: string | null = null
    void receiptPreview(r.previewUrl, controller.signal).then((blob) => {
      if (controller.signal.aborted) return
      objectUrl = URL.createObjectURL(blob)
      setConvertedUrl(objectUrl)
    }).catch(() => { if (!controller.signal.aborted) setBroken(true) })
    return () => {
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [convert, r.previewUrl])
  const box = size === 'lg' ? 'max-h-[75dvh] min-h-80 w-full' : 'h-28 w-24'
  if (!r.previewUrl || broken) {
    return <span className={cx('flex flex-col items-center justify-center gap-2 rounded-xl border border-line bg-wash p-2 text-center text-xs text-ink2', box)}><FileText size={size === 'lg' ? 40 : 24} />{r.mimeType === 'application/pdf' ? 'Dokument PDF' : 'Podgląd niedostępny'}</span>
  }
  if (convert && !convertedUrl) return <span role="status" className={cx('flex items-center justify-center rounded-xl border border-line bg-wash p-2 text-center text-xs text-ink2', box)}>Przygotowywanie podglądu…</span>
  return <img src={convertedUrl ?? r.previewUrl} alt={`Paragon: ${r.fileName}`} onError={() => convertedUrl || r.mimeType === 'application/pdf' ? setBroken(true) : setConvert(true)} className={cx('rounded-xl border border-line bg-white object-contain', box)} />
}

// Read-only expense views show display names, which come from the Expense responses (payer and participant display names).
export function useNames() {
  return { user: (id: string) => mockName(id) }
}
