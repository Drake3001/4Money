import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'

let vite, expensesApi, defaults, query
const originalFetch = globalThis.fetch
const originalStorage = globalThis.localStorage

before(async () => {
  globalThis.localStorage = { getItem: () => null }
  globalThis.fetch = async (url) => {
    query = new URL(url, 'https://example.test').searchParams
    return Response.json({ items: [], totalCount: 0 })
  }
  vite = await createServer({ logLevel: 'error' })
  ;({ expensesApi } = await vite.ssrLoadModule('/src/api/expenses.ts'))
  ;({ DEFAULT_QUERY: defaults } = await vite.ssrLoadModule('/src/expensesContext.ts'))
})

after(async () => {
  await vite?.close()
  globalThis.fetch = originalFetch
  if (originalStorage === undefined) delete globalThis.localStorage
  else globalThis.localStorage = originalStorage
})

test('expense dates match the DateOnly API contract and include the selected end day', async () => {
  for (const [selected, exclusiveEnd] of [
    ['2026-11-13', '2026-11-14'],
    ['2026-03-29', '2026-03-30'],
    ['2026-10-25', '2026-10-26'],
    ['2028-02-29', '2028-03-01'],
    ['2026-12-31', '2027-01-01'],
  ]) {
    await expensesApi.list({ ...defaults, dateFrom: selected, dateTo: selected })
    assert.equal(query.get('transactionTimeFrom'), selected)
    assert.equal(query.get('transactionTimeTo'), exclusiveEnd)
  }
})

test('expense date filters support either boundary alone and omit empty dates', async () => {
  await expensesApi.list({ ...defaults, dateFrom: '2026-11-13' })
  assert.equal(query.get('transactionTimeFrom'), '2026-11-13')
  assert.equal(query.has('transactionTimeTo'), false)
  await expensesApi.list({ ...defaults, dateTo: '2026-11-13' })
  assert.equal(query.has('transactionTimeFrom'), false)
  assert.equal(query.get('transactionTimeTo'), '2026-11-14')
  await expensesApi.list(defaults)
  assert.equal(query.has('transactionTimeFrom'), false)
  assert.equal(query.has('transactionTimeTo'), false)
})

test('bank picker preserves server pagination and translates debit amount bounds', async () => {
  const previousFetch = globalThis.fetch
  const transaction = { id: 'synthetic-transaction', bankAccountId: 'synthetic-account', amount: '-45.00', currencyCode: 'PLN', date: '2026-11-14', description: 'Test zakupów', status: 'Booked' }
  const page = { items: [transaction], pageNumber: 2, pageSize: 20, totalCount: 45, totalPages: 3, hasPreviousPage: true, hasNextPage: true }
  globalThis.fetch = async (url) => {
    const request = new URL(url, 'https://example.test')
    assert.equal(request.pathname, '/api/expenses/available-bank-transactions')
    query = request.searchParams
    return Response.json(page)
  }
  try {
    const result = await expensesApi.availableBankTransactionsPage({
      accountId: 'synthetic-account', dateFrom: '2026-11-01', dateTo: '2026-11-14', minAmount: '20,50', maxAmount: '100', description: '',
      pageNumber: 2, pageSize: 20, sortBy: 'amount', descending: false,
    })
    assert.deepEqual(result, page)
    assert.equal(query.get('pageNumber'), '2')
    assert.equal(query.get('pageSize'), '20')
    assert.equal(query.get('sortBy'), 'amount')
    assert.equal(query.get('descending'), 'false')
    assert.equal(query.get('minAmount'), '-100')
    assert.equal(query.get('maxAmount'), '-20.5')
    assert.equal(query.get('dateTo'), '2026-11-14')
    assert.equal(query.has('description'), false)
  } finally {
    globalThis.fetch = previousFetch
  }
})

test('attaching a bank transaction replaces source fields and keeps other expense data', async () => {
  const { bankFormValues, computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  const draft = {
    groupId: 'synthetic-group', payerId: 'synthetic-user', name: 'Poprzednia nazwa', merchant: 'Testowy sklep', amount: '999', date: '2026-10-01', time: '',
    items: [], shares: [{ userId: 'synthetic-user', percentage: '100' }], bankTransactionId: null, receiptId: 'synthetic-receipt',
  }
  const transaction = { id: 'synthetic-transaction', amount: '-45.90', date: '2026-11-14', description: 'Test zakupów' }
  const filled = { ...draft, ...bankFormValues(transaction) }
  assert.equal(filled.name, 'Test zakupów')
  assert.equal(filled.date, '2026-11-14')
  assert.equal(filled.amount, '45.9')
  assert.equal(filled.bankTransactionId, transaction.id)
  assert.equal(filled.merchant, draft.merchant)
  assert.equal(filled.receiptId, draft.receiptId)
  assert.equal(filled.shares, draft.shares)
  const edited = { ...filled, name: 'Własna nazwa', date: '2026-11-13', amount: '999' }
  const result = computeExpense(edited, { activePayers: ['synthetic-user'], bankAmount: Number(transaction.amount) })
  assert.equal(result.preview.totalAmount, 45.9)
  assert.equal(edited.name, 'Własna nazwa')
  assert.equal(edited.date, '2026-11-13')
})
