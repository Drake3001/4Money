// UI-side models and pure helpers for the Expense module (types, number helpers, form model, client-side validation). No data lives here.
import { ME_ID, mockName } from './groups'
import type { Tx } from './bank'

export { ME_ID, mockName }
export { userLabel, shortId } from './groups'

// ---- Categories (GET /api/categories). The React provider owns the list. Colours are a frontend concern.
export type Category = { id: string; name: string; description: string; icon: string; isSystemGenerated: boolean }
/** Id used for "no category" in forms and fallbacks. */
export const UNKNOWN_CATEGORY_ID = ''
export const DISCOUNT_CATEGORY_ID = '30000000-0000-0000-0000-000000000020'
const FALLBACK_CATEGORY: Category = { id: '', name: 'Nieznane', description: 'Kategoria nieznana', icon: '', isSystemGenerated: true }
export const category = (id: string | null | undefined, categories: readonly Category[]) => categories.find((c) => c.id === id) ?? FALLBACK_CATEGORY
// ---- Types
export type ExpenseItem = { id: string; categoryId: string; name: string; unit: string | null; unitPrice: number; quantity: number; discount: number; isSystemGenerated?: boolean }
export type Share = { userId: string; percentage: number }
export type ExpenseDetail = {
  id: string; version: string; groupId: string; groupName: string; payerId: string; name: string; merchant: string | null
  totalAmount: number; transactionTime: string; items: ExpenseItem[]; shares: Share[]
  bankTransactionId: string | null; receiptId: string | null; canEdit: boolean; canDelete: boolean
}

export type OcrItem = { name: string; unit?: string; unitPrice?: number; quantity?: number; discount?: number; categoryId?: string; needsReview?: boolean }
export type OcrAnalysis = {
  name?: string; merchant?: string; amount?: number; currency?: string; transactionDate?: string; transactionTime?: string
  items: OcrItem[]; warnings: string[]; needsReview: boolean
}
export type Receipt = { id: string; fileName: string; mimeType: string; size: number | null; previewUrl: string | null; createdAt: string; originalAnalysis: OcrAnalysis | null; latestAnalysis: OcrAnalysis | null; isLocal?: boolean }

export const RECEIPT_MIME_BY_EXTENSION: Readonly<Record<string, string>> = {
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  png: 'image/png',
  webp: 'image/webp',
  heic: 'image/heic',
  heif: 'image/heif',
  tif: 'image/tiff',
  tiff: 'image/tiff',
  bmp: 'image/bmp',
  pdf: 'application/pdf',
}
export const RECEIPT_EXT = Object.keys(RECEIPT_MIME_BY_EXTENSION)
export const MAX_RECEIPT_BYTES = 20 * 1024 * 1024
export const PAGE_SIZE = 20

// ---- Number helpers
export const r2 = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100
export const num = (s: string | number) => (typeof s === 'number' ? s : Number(String(s).trim().replace(',', '.')))
export const isNum = (s: string) => s.trim() !== '' && Number.isFinite(num(s))
export const itemTotal = (i: { unitPrice: number; quantity: number; discount: number }) => r2(i.unitPrice * i.quantity + i.discount)
export const fmtPct = (n: number) => `${String(r2(n)).replace('.', ',')}%`

export function equalShares(userIds: string[]): Share[] {
  if (!userIds.length) return []
  const cents = Math.floor(10000 / userIds.length)
  const rest = 10000 - cents * userIds.length
  return userIds.map((u, i) => ({ userId: u, percentage: (cents + (i === 0 ? rest : 0)) / 100 }))
}

export function shareAmounts(total: number, shares: { userId: string; percentage: number }[]) {
  const amounts = shares.map((s) => r2((total * s.percentage) / 100))
  const diff = r2(total - amounts.reduce((a, b) => a + b, 0))
  if (amounts.length) amounts[0] = r2(amounts[0] + diff)
  return shares.map((s, i) => ({ ...s, amount: amounts[i] }))
}

// ---- Form model (what the "Dodaj wydatek" form edits; mirrors the create/update request)
export type FormItem = { id: string; categoryId: string; name: string; unit: string; unitPrice: string; quantity: string; discount: string }
export type FormShare = { userId: string; percentage: string }
export type ExpenseForm = {
  groupId: string; payerId: string; name: string; merchant: string; amount: string
  date: string; time: string; items: FormItem[]; shares: FormShare[]
  bankTransactionId: string | null; receiptId: string | null
}

let seq = 0
export const uid = (p = 'id') => `${p}-${Date.now().toString(36)}${(seq++).toString(36)}`
export const emptyItem = (): FormItem => ({ id: uid('it'), categoryId: '', name: '', unit: '', unitPrice: '', quantity: '1', discount: '' })

export type PreviewItem = ExpenseItem & { total: number }
export type Preview = {
  totalAmount: number; items: PreviewItem[]; unknownAmount: number
  shares: { userId: string; percentage: number; amount: number }[]
  bankTransactionId: string | null; receiptId: string | null
}
export type Problems = { fields: Record<string, string>; items: Record<string, string[]>; itemFields: Record<string, Partial<Record<keyof FormItem, string>>>; list: string[] }

// Shared by live validation, POST /expenses/preview, create and update.
export function computeExpense(f: ExpenseForm, ctx: { activePayers: string[]; bankAmount?: number | null }): { problems: Problems; preview: Preview | null } {
  const fields: Record<string, string> = {}
  const items: Record<string, string[]> = {}
  const itemFields: Problems['itemFields'] = {}
  const list: string[] = []
  const bad = (k: string, m: string) => { fields[k] = m; list.push(m) }

  if (!f.groupId) bad('groupId', 'Wybierz grupę.')
  if (!f.payerId) bad('payerId', 'Wybierz płatnika.')
  else if (!ctx.activePayers.includes(f.payerId)) bad('payerId', 'Płatnik musi być aktualnym członkiem grupy.')
  if (!f.name.trim()) bad('name', 'Podaj nazwę wydatku.')
  if (!f.date) bad('date', 'Podaj datę transakcji.')

  const normal: PreviewItem[] = []
  f.items.forEach((it) => {
    const errs: string[] = []
    const fieldErrors: Partial<Record<keyof FormItem, string>> = {}
    const badItem = (key: keyof FormItem, message: string) => { fieldErrors[key] = message; errs.push(message) }
    const price = num(it.unitPrice), qty = num(it.quantity), disc = it.discount.trim() === '' ? 0 : num(it.discount)
    const isDiscount = it.categoryId === DISCOUNT_CATEGORY_ID
    if (!it.name.trim()) badItem('name', 'Podaj nazwę produktu.')
    if (!it.categoryId) badItem('categoryId', 'Wybierz kategorię produktu.')
    if (!isNum(it.unitPrice)) badItem('unitPrice', 'Uzupełnij cenę jednostkową')
    else if (isDiscount && (price >= 0 || r2(price) >= 0)) badItem('unitPrice', 'Dla kategorii „Zniżka” wprowadź ujemną cenę jednostkową')
    if (!isNum(it.quantity) || qty <= 0) badItem('quantity', 'Ilość musi być dodatnia.')
    else if (isDiscount && qty !== 1) badItem('quantity', 'Dla kategorii „Zniżka” ilość musi być równa 1.')
    if (!Number.isFinite(disc) || disc > 0) badItem('discount', 'Rabat musi być liczbą mniejszą lub równą zero.')
    else if (isDiscount && disc !== 0) badItem('discount', 'Dla kategorii „Zniżka” rabat musi być równy zero.')
    else if (disc !== 0 && isNum(it.unitPrice) && isNum(it.quantity) && qty > 0 && itemTotal({ unitPrice: price, quantity: qty, discount: disc }) < 0) badItem('discount', 'Wartość produktu po rabacie nie może być ujemna.')
    itemFields[it.id] = fieldErrors
    if (errs.length) { items[it.id] = errs; list.push(`Produkt „${it.name.trim() || 'bez nazwy'}”: ${errs[0]}`) }
    else normal.push({ id: it.id, categoryId: it.categoryId, name: it.name.trim(), unit: it.unit.trim() || null, unitPrice: r2(price), quantity: qty, discount: r2(disc), total: itemTotal({ unitPrice: price, quantity: qty, discount: disc }) })
  })
  const known = r2(normal.reduce((s, i) => s + i.total, 0))

  let total = 0
  if (f.bankTransactionId) total = ctx.bankAmount != null ? r2(Math.abs(ctx.bankAmount)) : known
  else if (f.amount.trim() !== '') {
    if (!isNum(f.amount) || num(f.amount) <= 0) bad('amount', 'Kwota musi być większa od zera.')
    else total = r2(num(f.amount))
  } else if (known > 0) total = known
  else if (!fields.amount) bad('amount', 'Podaj kwotę lub dodaj produkty z ceną.')
  if (total > 0 && known > total + 0.001) bad('amount', `Suma produktów (${known.toFixed(2).replace('.', ',')} zł) przekracza kwotę wydatku. Zwiększ kwotę lub popraw produkty.`)

  // Shares: percentages only; each > 0, unique, exactly 100 in total.
  const seen = new Set<string>()
  let sum = 0
  if (f.shares.length === 0) bad('shares', 'Dodaj co najmniej jednego uczestnika.')
  f.shares.forEach((s) => {
    const p = num(s.percentage)
    if (seen.has(s.userId)) bad('shares', 'Uczestnicy nie mogą się powtarzać.')
    seen.add(s.userId)
    if (!isNum(s.percentage) || p <= 0 || p > 100) bad('shares', `Udział uczestnika ${mockName(s.userId)} musi być większy od 0% i nie większy niż 100%.`)
    else sum = r2(sum + p)
  })
  if (f.shares.length > 0 && !fields.shares && Math.abs(sum - 100) > 0.001) bad('shares', `Udziały muszą dawać dokładnie 100% (obecnie ${fmtPct(sum)}).`)

  if (list.length) return { problems: { fields, items, itemFields, list }, preview: null }

  const unknownAmount = r2(total - known)
  const finalItems: PreviewItem[] = [...normal]
  if (unknownAmount > 0.001) finalItems.push({ id: 'system-unknown', categoryId: UNKNOWN_CATEGORY_ID, name: 'Nieznane', unit: null, unitPrice: unknownAmount, quantity: 1, discount: 0, isSystemGenerated: true, total: unknownAmount })
  return {
    problems: { fields, items, itemFields, list },
    preview: {
      totalAmount: total, items: finalItems, unknownAmount: unknownAmount > 0.001 ? unknownAmount : 0,
      shares: shareAmounts(total, f.shares.map((s) => ({ userId: s.userId, percentage: num(s.percentage) }))),
      bankTransactionId: f.bankTransactionId, receiptId: f.receiptId,
    },
  }
}

// ---- Local date/time (UI) <-> ISO UTC (stored). Raw UTC is never shown to the user.
export const toLocalParts = (iso: string) => {
  if (/^\d{4}-\d{2}-\d{2}$/.test(iso)) return { date: iso, time: '' }
  const d = new Date(iso)
  const p = (n: number) => String(n).padStart(2, '0')
  return { date: `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`, time: `${p(d.getHours())}:${p(d.getMinutes())}` }
}
export const fromLocalParts = (date: string, time: string) => new Date(`${date}T${time}:00`).toISOString()
export const fmtDateTime = (iso: string) => new Date(iso).toLocaleString('pl-PL', { dateStyle: 'medium', timeStyle: 'short' })
export const fmtDay = (iso: string) => new Date(iso).toLocaleDateString('pl-PL', { dateStyle: 'medium' })


export const toFormItems = (items: ExpenseItem[]): FormItem[] =>
  items.filter((i) => !i.isSystemGenerated).map((i) => ({ id: i.id, categoryId: i.categoryId === UNKNOWN_CATEGORY_ID ? '' : i.categoryId, name: i.name, unit: i.unit ?? '', unitPrice: String(i.unitPrice), quantity: String(i.quantity), discount: i.discount ? String(i.discount) : '' }))

export function formFromExpense(e: ExpenseDetail): ExpenseForm {
  const t = toLocalParts(e.transactionTime)
  return {
    groupId: e.groupId, payerId: e.payerId, name: e.name, merchant: e.merchant ?? '', amount: String(e.totalAmount),
    date: t.date, time: t.time, items: toFormItems(e.items), shares: e.shares.map((s) => ({ userId: s.userId, percentage: String(s.percentage) })),
    bankTransactionId: e.bankTransactionId, receiptId: e.receiptId,
  }
}


export function bankFormValues(tx: Tx): Pick<ExpenseForm, 'bankTransactionId' | 'name' | 'date' | 'amount'> {
  return { bankTransactionId: tx.id, name: tx.description ?? '', date: tx.date, amount: String(Math.abs(Number(tx.amount))) }
}

export type { Tx }
