import { api, allPages, type Page } from './client'
import type { AccountQuery, TxQuery } from '../mocks/bank'
import type {
  BankAccountResponse, BankConnectionAttemptResponse, BankConnectionResponse, BankConsentResponse, BankRefreshResponse,
  BankTransactionResponse, StartBankConnectionResponse, StartBankRefreshResponse,
} from './types'

const dec = (s: string) => s.trim().replace(',', '.')

export const banking = {
  startConnection: () => api.post<StartBankConnectionResponse>('/api/banking/connection-attempts'),
  attempt: (id: string) => api.get<BankConnectionAttemptResponse>(`/api/banking/connection-attempts/${id}`),
  connections: () => api.get<BankConnectionResponse[]>('/api/banking/connections'),
  connection: (id: string) => api.get<BankConnectionResponse>(`/api/banking/connections/${id}`),
  consents: () => api.get<BankConsentResponse[]>('/api/banking/consents'),
  startRefresh: () => api.post<StartBankRefreshResponse>('/api/banking/refreshes'),
  refresh: (id: string) => api.get<BankRefreshResponse>(`/api/banking/refreshes/${id}`),
  accounts: (q: AccountQuery) => api.get<Page<BankAccountResponse>>('/api/banking/accounts', {
    pageNumber: q.pageNumber, pageSize: q.pageSize, name: q.name.trim(), currencyCode: q.currency, type: q.type.trim(),
    isClosed: q.isClosed === '' ? undefined : q.isClosed === 'yes', sortBy: q.sortBy, descending: q.descending,
  }),
  allAccounts: () => allPages<BankAccountResponse>('/api/banking/accounts'),
  account: (id: string) => api.get<BankAccountResponse>(`/api/banking/accounts/${id}`),
  transactions: (q: TxQuery) => api.get<Page<BankTransactionResponse>>('/api/banking/transactions', {
    pageNumber: q.pageNumber, pageSize: q.pageSize, bankAccountId: q.bankAccountId, dateFrom: q.dateFrom, dateTo: q.dateTo,
    minAmount: q.minAmount.trim() ? dec(q.minAmount) : undefined, maxAmount: q.maxAmount.trim() ? dec(q.maxAmount) : undefined,
    currencyCode: q.currency, description: q.description.trim(), status: q.status.trim(),
    sortBy: q.sortBy === 'currency' ? 'currencyCode' : q.sortBy, descending: q.descending,
  }),
  transaction: (id: string) => api.get<BankTransactionResponse>(`/api/banking/transactions/${id}`),
}
