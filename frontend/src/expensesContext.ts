import { createContext, useContext } from 'react'
import type { Res } from './api/client'
import type { Category, ExpenseDetail, ExpenseForm, OcrAnalysis, Preview, Receipt, Tx } from './mocks/expenses'

export type { Res }

export type ExpenseQuery = {
  pageNumber: number; pageSize: number; sortBy: 'transactionTime' | 'amount' | 'name'; descending: boolean
  groupId: string; participantId: string; payerId: string; dateFrom: string; dateTo: string
  minAmount: string; maxAmount: string; categoryId: string; search: string
  hasReceipt: boolean | null; hasBankTransaction: boolean | null
}
export const DEFAULT_QUERY: ExpenseQuery = {
  pageNumber: 1, pageSize: 20, sortBy: 'transactionTime', descending: true, groupId: '', participantId: '', payerId: '', dateFrom: '', dateTo: '',
  minAmount: '', maxAmount: '', categoryId: '', search: '', hasReceipt: null, hasBankTransaction: null,
}

export type ExpenseListItem = {
  id: string; name: string; merchant: string | null; totalAmount: number; transactionTime: string; groupId: string; groupName: string; payerId: string
  myShareAmount: number; myShareRate: number; categoryIds: string[]; hasReceipt: boolean; hasBankTransaction: boolean
}
export type Page<T> = { items: T[]; pageNumber: number; pageSize: number; totalCount: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }

export type BankFilter = { accountId: string; dateFrom: string; dateTo: string; minAmount: string; maxAmount: string; description: string }
export type BankPageFilter = BankFilter & { pageNumber: number; pageSize: number; sortBy: 'date' | 'amount'; descending: boolean }

// Every operation goes to the real Expense API. A failed call resolves to { ok: false, code } (HTTP status; 0 = network failure).
// HTTP 501 has already opened the shared "Usługa tymczasowo niedostępna" dialog by the time the Res is returned.
export type ExpensesApi = {
  categories: Category[]
  categoriesLoading: boolean
  categoriesError: number | null
  reloadCategories: () => void
  list: (q: ExpenseQuery) => Promise<Res<Page<ExpenseListItem>>>
  get: (id: string) => Promise<Res<ExpenseDetail>>
  preview: (f: ExpenseForm, expenseId?: string, version?: string) => Promise<Res<Preview>>
  create: (f: ExpenseForm) => Promise<Res<ExpenseDetail>>
  update: (id: string, f: ExpenseForm, version: string) => Promise<Res<ExpenseDetail>>
  remove: (id: string, version: string) => Promise<Res>
  availableBankTransactions: (f: BankFilter) => Promise<Res<Tx[]>>
  availableBankTransactionsPage: (f: BankPageFilter) => Promise<Res<Page<Tx>>>
  bankTransaction: (id: string) => Promise<Res<Tx>>
  uploadReceipt: (file: File) => Promise<Res<Receipt>>
  /** Receipts seen in this session (upload / GET / analyze), for synchronous rendering. */
  getReceipt: (id: string) => Receipt | undefined
  fetchReceipt: (expenseId: string) => Promise<Res<Receipt>>
  analyzeReceipt: (id: string) => Promise<Res<OcrAnalysis>>
  deleteReceipt: (id: string) => Promise<Res>
}
export const ExpensesCtx = createContext<ExpensesApi>(null as never)
export const useExpenses = () => useContext(ExpensesCtx)
