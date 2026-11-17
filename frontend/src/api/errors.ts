import { lastProblem } from './client'

// User-facing Polish messages per HTTP status. 501 is intentionally empty: the shared unavailable-service dialog is shown instead.
const BASE: Record<number, string> = {
  0: 'Brak połączenia z serwerem. Sprawdź połączenie i spróbuj ponownie.',
  400: 'Dane są nieprawidłowe. Sprawdź formularz i spróbuj ponownie.',
  401: 'Twoja sesja wygasła lub dane logowania są nieprawidłowe.',
  403: 'Nie masz uprawnień do tej czynności.',
  404: 'Nie znaleziono zasobu lub jest niedostępny.',
  409: 'Operacja koliduje z aktualnym stanem danych. Odśwież widok i spróbuj ponownie.',
  413: 'Plik jest za duży.',
  415: 'Nieobsługiwany format pliku.',
  422: 'Nie udało się przetworzyć przesłanej zawartości.',
  429: 'Zbyt wiele żądań. Odczekaj chwilę i spróbuj ponownie.',
  500: 'Wystąpił błąd serwera. Spróbuj ponownie później.',
  502: 'Usługa zewnętrzna zwróciła błąd. Spróbuj ponownie później.',
  503: 'Usługa jest chwilowo niedostępna. Spróbuj ponownie za chwilę.',
  504: 'Usługa nie odpowiedziała na czas. Spróbuj ponownie później.',
}

/** Message for a failed request, or null when nothing should be shown (HTTP 501 is handled by the dialog). */
export function describeError(code: number, overrides: Record<number, string> = {}, options: { includeDetails?: boolean } = {}): string | null {
  if (code === 501) return null
  const base = overrides[code] ?? BASE[code] ?? BASE[500]
  const extra = options.includeDetails !== false && lastProblem && lastProblem.code === code && (code === 400 || code === 409 || code === 422) ? lastProblem.message : undefined
  return extra && !base.includes(extra) ? `${base} (${extra})` : base
}
