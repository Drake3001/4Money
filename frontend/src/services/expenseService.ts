import { useMemo } from 'react'
import { useExpenses, type ExpensesApi } from '../expensesContext'

// Expense service used by the UI: a thin wrapper over the real Expense API (see src/api/expenses.ts).
// Receipt operations belong to the Expense API; there is no /api/receipts.
export type ExpenseService = {
  /** GET /api/expenses */
  listExpenses: ExpensesApi['list']
  /** GET /api/expenses/{expenseId} */
  getExpense: ExpensesApi['get']
  /** POST /api/expenses/preview */
  previewExpense: ExpensesApi['preview']
  /** POST /api/expenses */
  createExpense: ExpensesApi['create']
  /** PUT /api/expenses/{expenseId} */
  updateExpense: ExpensesApi['update']
  /** DELETE /api/expenses/{expenseId}?version= */
  deleteExpense: ExpensesApi['remove']
  /** GET /api/expenses/available-bank-transactions */
  availableBankTransactions: ExpensesApi['availableBankTransactions']
  /** GET /api/banking/transactions/{transactionId} */
  getBankTransaction: ExpensesApi['bankTransaction']
  /** Keep the selected file locally until saving the expense. */
  uploadReceipt: ExpensesApi['uploadReceipt']
  /** GET /api/expenses/{expenseId}/receipt */
  getReceipt: ExpensesApi['fetchReceipt']
  /** POST /api/expenses/receipts/analyze with the selected file. */
  analyzeReceipt: ExpensesApi['analyzeReceipt']
  /** Release the local file; saved receipts are detached through expense update. */
  deleteReceipt: ExpensesApi['deleteReceipt']
}

export function useExpenseService(): ExpenseService {
  const ex = useExpenses()
  return useMemo<ExpenseService>(() => ({
    listExpenses: ex.list, getExpense: ex.get, previewExpense: ex.preview, createExpense: ex.create, updateExpense: ex.update, deleteExpense: ex.remove,
    availableBankTransactions: ex.availableBankTransactions, getBankTransaction: ex.bankTransaction, uploadReceipt: ex.uploadReceipt,
    getReceipt: ex.fetchReceipt, analyzeReceipt: ex.analyzeReceipt, deleteReceipt: ex.deleteReceipt,
  }), [ex])
}
