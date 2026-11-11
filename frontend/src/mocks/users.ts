// Local mock of the User module. Shapes follow the real DTOs; nothing is sent anywhere.
import { ME_ID } from './groups'

export type UserProfile = {
  id: string; login: string; name: string; surname: string; email: string; phone: string | null
  birthDate: string; icon: string | null; isEmailConfirmed: boolean; createdAt: string; lastLoginTime: string | null
}

export const INITIAL_PROFILE: UserProfile = {
  id: ME_ID, login: 'mnowak', name: 'Michał', surname: 'Nowak', email: 'michal.nowak@example.com', phone: '+48 600 100 200',
  birthDate: '1992-04-17', icon: 'user', isEmailConfirmed: true, createdAt: '2026-09-01T12:00:00Z', lastLoginTime: '2026-10-16T07:30:00Z',
}

// Logins that already exist (used to demonstrate 409 conflicts).
export const TAKEN_LOGINS = ['admin', 'anna.k', 'kasia']
export const TAKEN_EMAILS = ['admin@example.com', 'anna@example.com']

// ---- Validation (mirrors the request contracts)
export type Problems = Record<string, string>
export const PUNCT = /[!-/:-@[-`{-~]/
export const isFutureDate = (d: string) => new Date(`${d}T00:00:00`).getTime() > Date.now()
export const isEmail = (s: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s)

export const validatePassword = (pw: string): string | null =>
  pw.length < 8 || pw.length > 128 ? 'Hasło musi mieć od 8 do 128 znaków.'
  : !/[A-ZĄĆĘŁŃÓŚŹŻ]/.test(pw) ? 'Hasło musi zawierać wielką literę.'
  : !/\d/.test(pw) ? 'Hasło musi zawierać cyfrę.'
  : !PUNCT.test(pw) ? 'Hasło musi zawierać znak interpunkcyjny lub specjalny.' : null

export function validateLogin(login: string): string | null {
  const l = login.trim()
  return !l ? 'Podaj login.' : l.length < 3 || l.length > 100 ? 'Login musi mieć od 3 do 100 znaków.' : null
}
export const validateName = (v: string, what: string) => (!v.trim() ? `Podaj ${what}.` : v.trim().length > 100 ? `${what[0].toUpperCase()}${what.slice(1)} może mieć maksymalnie 100 znaków.` : null)
export const validateBirth = (d: string) => (!d ? 'Podaj datę urodzenia.' : isFutureDate(d) ? 'Data urodzenia nie może być z przyszłości.' : null)
export const validateEmail = (e: string) => (!e.trim() ? 'Podaj adres e-mail.' : e.trim().length > 254 || !isEmail(e.trim()) ? 'Podaj poprawny adres e-mail (maks. 254 znaki).' : null)

export type RegisterInput = { login: string; name: string; surname: string; email: string; password: string; birthDate: string }
export function validateRegister(i: RegisterInput): Problems {
  const p: Problems = {}
  const set = (k: string, m: string | null) => { if (m) p[k] = m }
  set('login', validateLogin(i.login)); set('name', validateName(i.name, 'imię')); set('surname', validateName(i.surname, 'nazwisko'))
  set('email', validateEmail(i.email)); set('password', validatePassword(i.password)); set('birthDate', validateBirth(i.birthDate))
  return p
}
export type ProfileInput = { login: string; name: string; surname: string; phone: string; birthDate: string; icon: string | null }
export function validateProfile(i: ProfileInput): Problems {
  const p: Problems = {}
  const set = (k: string, m: string | null) => { if (m) p[k] = m }
  set('login', validateLogin(i.login)); set('name', validateName(i.name, 'imię')); set('surname', validateName(i.surname, 'nazwisko'))
  set('birthDate', validateBirth(i.birthDate))
  if (i.phone.trim().length > 30) p.phone = 'Telefon może mieć maksymalnie 30 znaków.'
  return p
}

export const USER_ERRORS: Record<number, string> = {
  400: 'Dane są nieprawidłowe lub link wygasł. Sprawdź je i spróbuj ponownie.',
  401: 'Nieprawidłowy login lub hasło.',
  403: 'Nie masz dostępu do tej czynności.',
  409: 'Ten login lub adres e-mail jest już zajęty.',
  500: 'Wystąpił nieoczekiwany błąd. Spróbuj ponownie później.',
  503: 'Usługa jest chwilowo niedostępna. Spróbuj ponownie za chwilę.',
}
export const USER_REASON: Record<string, string> = {
  unconfirmed: 'Adres e-mail nie został jeszcze potwierdzony. Potwierdź go linkiem z wiadomości.',
  inactive: 'To konto jest nieaktywne.',
  password: 'Aktualne hasło jest nieprawidłowe.',
  token: 'Link jest nieprawidłowy lub wygasł.',
}
export const PROFILE_ICONS = ['user', 'smile', 'heart', 'star', 'rocket', 'coffee']
