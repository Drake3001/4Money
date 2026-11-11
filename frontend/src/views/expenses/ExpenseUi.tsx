import { useState } from 'react'
import { Car, CircleHelp, Clapperboard, CupSoda, FileText, HeartPulse, House, Plane, Receipt, ShoppingBasket, Tag, Utensils, type LucideIcon } from 'lucide-react'
import { useApp } from '../../context'
import { categoryColor, category, mockName, type Receipt as ReceiptT } from '../../mocks/expenses'
import { cx } from '../../ui'
import { describeError } from '../../api/errors'

const ICONS: Record<string, LucideIcon> = {
  basket: ShoppingBasket, cup: CupSoda, utensils: Utensils, car: Car, receipt: Receipt, house: House, heart: HeartPulse,
  clapper: Clapperboard, plane: Plane, tag: Tag, help: CircleHelp,
}

export function CatBadge({ id, compact }: { id?: string | null; compact?: boolean }) {
  const c = category(id)
  const I = ICONS[c.icon] ?? Tag
  const color = categoryColor(c)
  return (
    <span className={cx('inline-flex items-center gap-1.5 rounded-full text-xs font-medium text-ink', compact ? 'px-2 py-0.5' : 'px-2.5 py-1')} style={{ background: color + '22' }} title={c.description}>
      <I size={13} style={{ color }} />{c.name}
    </span>
  )
}

export const EXP_ERRORS: Record<number, string> = {
  0: 'Brak połączenia z serwerem. Sprawdź połączenie i spróbuj ponownie.',
  400: 'Dane formularza lub filtrów są nieprawidłowe. Sprawdź je i spróbuj ponownie.',
  401: 'Twoja sesja wygasła. Zaloguj się ponownie.',
  403: 'Możesz przeglądać ten wydatek, ale nie możesz go zmieniać ani usuwać.',
  404: 'Nie znaleziono wydatku, paragonu lub innego zasobu. Mógł zostać usunięty.',
  409: 'Wydatek został zmieniony w innym miejscu albo wybrane źródło jest już użyte. Odśwież dane i spróbuj ponownie.',
  413: 'Plik jest za duży. Maksymalny rozmiar paragonu to 20 MiB.',
  415: 'Nieobsługiwany format pliku. Dodaj JPEG, PNG, WebP, HEIC/HEIF, TIFF, BMP lub PDF.',
  422: 'Nie udało się odczytać z tego dokumentu przydatnych danych. Spróbuj z wyraźniejszym zdjęciem paragonu albo uzupełnij dane ręcznie.',
  502: 'Usługa analizy paragonów zwróciła nieprawidłową odpowiedź. Spróbuj ponownie później.',
  503: 'Usługa jest chwilowo niedostępna. Spróbuj ponownie za chwilę.',
  504: 'Analiza paragonu trwa zbyt długo. Spróbuj ponownie.',
  500: 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie później.',
}

export function ExpErrorBox({ code, children }: { code: number | null; children?: React.ReactNode }) {
  const { navigate } = useApp()
  if (!code || code === 501) return null // 501 is handled by the shared "Usługa tymczasowo niedostępna" dialog
  return (
    <div role="alert" className="space-y-2 rounded-xl bg-danger-soft p-3 text-sm text-danger">
      <p>{EXP_ERRORS[code] ?? describeError(code)}</p>
      {code === 401 && <button onClick={() => navigate({ name: 'login' })} className="font-semibold underline">Przejdź do logowania</button>}
      {children}
    </div>
  )
}

export function ReceiptThumb({ r, size = 'md' }: { r: Pick<ReceiptT, 'previewUrl' | 'mimeType' | 'fileName'>; size?: 'md' | 'lg' }) {
  const [broken, setBroken] = useState(false)
  const box = size === 'lg' ? 'h-80 w-full' : 'h-28 w-24'
  if (!r.previewUrl || broken) {
    return <span className={cx('flex flex-col items-center justify-center gap-2 rounded-xl border border-line bg-wash p-2 text-center text-xs text-ink2', box)}><FileText size={size === 'lg' ? 40 : 24} />{r.mimeType === 'application/pdf' ? 'Dokument PDF' : 'Podgląd niedostępny'}</span>
  }
  return <img src={r.previewUrl} alt={`Paragon: ${r.fileName}`} onError={() => setBroken(true)} className={cx('rounded-xl border border-line bg-white object-contain', box)} />
}

// Read-only expense views show display names, which come from the Expense responses (payer and participant display names).
export function useNames() {
  return { user: (id: string) => mockName(id) }
}
