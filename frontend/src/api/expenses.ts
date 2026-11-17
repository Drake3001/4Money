// Expense API. Receipt files are analyzed without saving, then uploaded with the expense.
import { api, type Page } from './client'
import { registerName } from './names'
import type {
  AvailableBankTransactionResponse, BankTransactionResponse, CategoryResponse, ExpenseListItemResponse, ExpensePreviewResponse, ExpenseResponse,
  ReceiptAnalysisResponse, ReceiptOcrResponse, ReceiptResponse,
} from './types'
import type { Category, ExpenseDetail, ExpenseForm, OcrAnalysis, OcrItem, Preview, Receipt } from '../mocks/expenses'
import { isNum, num, RECEIPT_MIME_BY_EXTENSION } from '../mocks/expenses'
import type { Tx } from '../mocks/bank'
import type { BankFilter, BankPageFilter, ExpenseListItem, ExpenseQuery } from '../expensesContext'

const n = (s: string | null | undefined) => (s === null || s === undefined || s === '' ? 0 : Number(s))
const opt = (s: string | null | undefined) => (s === null || s === undefined || s === '' || !Number.isFinite(Number(s)) ? undefined : Number(s))
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export const toCategory = (c: CategoryResponse): Category => ({ id: c.id, name: c.name, description: c.description, icon: c.icon, isSystemGenerated: c.isSystemGenerated })

export function toExpenseDetail(e: ExpenseResponse): ExpenseDetail {
  registerName(e.payerId, e.payerDisplayName)
  e.shares.forEach((s) => registerName(s.userId, s.userDisplayName))
  return {
    id: e.id, version: e.version, groupId: e.groupId, groupName: e.groupName, payerId: e.payerId, name: e.name, merchant: e.merchant,
    totalAmount: n(e.amount), transactionTime: e.transactionTime, bankTransactionId: e.bankTransactionId, receiptId: e.receiptId,
    canEdit: e.canEdit, canDelete: e.canDelete,
    items: e.items.map((i) => ({ id: i.id, categoryId: i.categoryId, name: i.name, unit: i.unit, unitPrice: n(i.unitPrice), quantity: n(i.quantity), discount: n(i.discount), isSystemGenerated: i.isSystemGenerated })),
    shares: e.shares.map((s) => ({ userId: s.userId, percentage: n(s.percentage) })),
  }
}

export function toPreview(p: ExpensePreviewResponse): Preview {
  p.shares.forEach((s) => registerName(s.userId, s.userDisplayName))
  const items = p.items.map((i, idx) => ({
    id: `preview-${idx}`, categoryId: i.categoryId, name: i.name, unit: i.unit, unitPrice: n(i.unitPrice), quantity: n(i.quantity), discount: n(i.discount),
    isSystemGenerated: i.isSystemGenerated, total: n(i.total),
  }))
  return {
    totalAmount: n(p.amount), items, unknownAmount: items.filter((i) => i.isSystemGenerated).reduce((s, i) => s + i.total, 0),
    shares: p.shares.map((s) => ({ userId: s.userId, percentage: n(s.percentage), amount: n(s.amount) })),
    bankTransactionId: p.bankTransactionId, receiptId: p.receiptId,
  }
}

export function toListItem(e: ExpenseListItemResponse): ExpenseListItem {
  registerName(e.payerId, e.payerDisplayName)
  return {
    id: e.id, name: e.name, merchant: e.merchant, totalAmount: n(e.amount), transactionTime: e.transactionTime, groupId: e.groupId, groupName: e.groupName, payerId: e.payerId,
    myShareAmount: n(e.myShareAmount), myShareRate: n(e.mySharePercentage), categoryIds: e.categories.map((c) => c.id), hasReceipt: e.hasReceipt, hasBankTransaction: e.hasBankTransaction,
  }
}

export function toAnalysis(a: ReceiptAnalysisResponse): OcrAnalysis {
  const p = a.proposal
  const items: OcrItem[] = p.items.map((i) => ({
    name: i.name ?? '', unit: i.unit ?? undefined, unitPrice: opt(i.unitPrice), quantity: opt(i.quantity), discount: opt(i.discount),
    categoryId: i.categoryId ?? undefined, needsReview: i.needsReview,
  }))
  return {
    name: p.name ?? undefined, merchant: p.merchant ?? undefined, amount: opt(p.amount), currency: p.currencyCode ?? undefined,
    transactionDate: p.transactionTime ?? undefined,
    items, warnings: a.warnings.map((w) => w.message), needsReview: a.needsReview,
  }
}

/** `file` is the local file that was just uploaded: the API returns no file name or MIME type. */
export function toReceipt(r: ReceiptResponse, file?: File): Receipt {
  return {
    id: r.id, fileName: file?.name ?? 'Paragon', mimeType: file?.type ?? '', size: file?.size ?? null, previewUrl: r.fileUrl, createdAt: '',
    originalAnalysis: r.analysis ? toAnalysis(r.analysis) : null, latestAnalysis: r.analysis ? toAnalysis(r.analysis) : null,
  }
}

export const toTx = (t: BankTransactionResponse | AvailableBankTransactionResponse): Tx => ({ ...t })

// ---- Request bodies
const dec = (v: number, d: number) => v.toFixed(d)
function formBody(f: ExpenseForm) {
  return {
    payerId: f.payerId, name: f.name.trim(), merchant: f.merchant.trim() || null,
    amount: f.amount.trim() !== '' && isNum(f.amount) ? dec(num(f.amount), 2) : null,
    transactionTime: f.date,
    items: f.items.map((i) => ({
      ...(UUID.test(i.id) ? { id: i.id } : {}), categoryId: i.categoryId, name: i.name.trim(), unitPrice: dec(num(i.unitPrice), 2), quantity: dec(num(i.quantity), 3),
      discount: i.discount.trim() === '' ? '0.00' : dec(num(i.discount), 2), unit: i.unit.trim() || null,
    })),
    shares: f.shares.map((s) => ({ userId: s.userId, percentage: dec(num(s.percentage), 2) })),
    bankTransactionId: f.bankTransactionId,
  }
}

const nextDay = (d: string) => {
  const date = new Date(`${d}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + 1)
  return date.toISOString().slice(0, 10)
}
const dot = (s: string) => (s.trim() ? String(num(s)) : undefined)

export type ReceiptAttachment = { file: File; ocrProof?: string }

function receiptUploadFile(file: File): File {
  if (file.type && file.type !== 'application/octet-stream') return file
  const extension = /\.([a-z0-9]+)$/i.exec(file.name)?.[1]?.toLowerCase()
  if (!extension || !Object.hasOwn(RECEIPT_MIME_BY_EXTENSION, extension)) return file

  return new File([file], file.name, { type: RECEIPT_MIME_BY_EXTENSION[extension], lastModified: file.lastModified })
}

function saveExpense(method: 'POST' | 'PUT', path: string, body: unknown, attachment?: ReceiptAttachment) {
  if (!attachment) return method === 'POST' ? api.post<ExpenseResponse>(path, body) : api.put<ExpenseResponse>(path, body)
  const form = new FormData()
  form.set('expense', JSON.stringify(body))
  form.set('file', receiptUploadFile(attachment.file))
  if (attachment.ocrProof) form.set('ocrProof', attachment.ocrProof)
  return api.upload<ExpenseResponse>(method, path, form)
}

async function availableBankTransactionsPage(f: BankPageFilter): Promise<Page<Tx>> {
  // Expense amount filters use debit magnitudes; the Banking API uses signed amounts.
  const lo = f.minAmount.trim() ? Math.abs(num(f.minAmount)) : null, hi = f.maxAmount.trim() ? Math.abs(num(f.maxAmount)) : null
  const page = await api.get<Page<AvailableBankTransactionResponse>>('/api/expenses/available-bank-transactions', {
    pageNumber: f.pageNumber, pageSize: f.pageSize, bankAccountId: f.accountId, dateFrom: f.dateFrom, dateTo: f.dateTo,
    minAmount: hi !== null ? String(-hi) : undefined, maxAmount: lo !== null ? String(-lo) : undefined,
    description: f.description.trim(), sortBy: f.sortBy, descending: f.descending,
  })
  return { ...page, items: page.items.map(toTx) }
}

export const expensesApi = {
  categories: async () => (await api.get<CategoryResponse[]>('/api/categories')).map(toCategory),
  list: async (q: ExpenseQuery): Promise<Page<ExpenseListItem>> => {
    const p = await api.get<Page<ExpenseListItemResponse>>('/api/expenses', {
      pageNumber: q.pageNumber, pageSize: q.pageSize, groupId: q.groupId, participantId: q.participantId, payerId: q.payerId,
      // The API accepts DateOnly; its exclusive end is the day after the selected "to" date.
      transactionTimeFrom: q.dateFrom || undefined, transactionTimeTo: q.dateTo ? nextDay(q.dateTo) : undefined,
      minAmount: dot(q.minAmount), maxAmount: dot(q.maxAmount), categoryId: q.categoryId, search: q.search.trim(),
      hasReceipt: q.hasReceipt, hasBankTransaction: q.hasBankTransaction, sortBy: q.sortBy, descending: q.descending,
    })
    return { ...p, items: p.items.map(toListItem) }
  },
  get: async (id: string) => toExpenseDetail(await api.get<ExpenseResponse>(`/api/expenses/${id}`)),
  preview: async (f: ExpenseForm, expenseId?: string, version?: string) => toPreview(await api.post<ExpensePreviewResponse>('/api/expenses/preview', { groupId: f.groupId, ...formBody(f), ...(expenseId ? { expenseId, version } : {}) })),
  create: async (f: ExpenseForm, attachment?: ReceiptAttachment) => toExpenseDetail(await saveExpense('POST', '/api/expenses', { groupId: f.groupId, ...formBody(f) }, attachment)),
  // UpdateExpenseRequest: creation fields without groupId, plus the opaque version token.
  update: async (id: string, f: ExpenseForm, version: string, attachment?: ReceiptAttachment) => toExpenseDetail(await saveExpense('PUT', `/api/expenses/${id}`, { ...formBody(f), version, removeReceipt: !f.receiptId }, attachment)),
  remove: (id: string, version: string) => api.del(`/api/expenses/${id}`, { version }),
  availableBankTransactions: async (f: BankFilter): Promise<Tx[]> => {
    return (await availableBankTransactionsPage({ ...f, pageNumber: 1, pageSize: 100, sortBy: 'date', descending: true })).items
  },
  availableBankTransactionsPage,
  bankTransaction: async (id: string) => toTx(await api.get<BankTransactionResponse>(`/api/banking/transactions/${id}`)),
  receipt: async (expenseId: string) => toReceipt(await api.get<ReceiptResponse>(`/api/expenses/${expenseId}/receipt`)),
  analyzeReceipt: async (file: File) => {
    const form = new FormData()
    form.set('file', receiptUploadFile(file))
    return api.upload<ReceiptOcrResponse>('POST', '/api/expenses/receipts/analyze', form)
  },
}
