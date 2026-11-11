import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToString } from 'react-dom/server'

let vite, expensesApi, toAnalysis, calls, reply
const originalFetch = globalThis.fetch
const originalStorage = globalThis.localStorage
const analysis = {
  proposal: { name: 'Zakupy', merchant: 'Synthetic shop', amount: '12.50', currencyCode: 'PLN', transactionTime: '2026-11-11', items: [] },
  warnings: [{ code: 'review', message: 'Sprawdź dane', field: null }], needsReview: true,
}
const expense = {
  id: 'expense-id', version: 'version', groupId: 'group', groupName: 'Group', payerId: 'payer', payerDisplayName: 'Test User',
  name: 'Zakupy', merchant: null, amount: '12.50', transactionTime: '2026-11-11', bankTransactionId: null, receiptId: 'saved-receipt',
  items: [], shares: [], canEdit: true, canDelete: true,
}
const form = {
  groupId: 'group', payerId: 'payer', name: 'Zakupy', merchant: '', amount: '12.50', date: '2026-11-11', time: '',
  items: [], shares: [], bankTransactionId: null, receiptId: 'local-draft',
}

before(async () => {
  globalThis.localStorage = { getItem: () => JSON.stringify({ accessToken: 'synthetic-test-token' }) }
  globalThis.fetch = async (url, options) => {
    calls.push({ url, ...options })
    return Response.json(reply)
  }
  vite = await createServer({ logLevel: 'error' })
  ;({ expensesApi, toAnalysis } = await vite.ssrLoadModule('/src/api/expenses.ts'))
})
after(async () => {
  await vite?.close()
  globalThis.fetch = originalFetch
  if (originalStorage === undefined) delete globalThis.localStorage
  else globalThis.localStorage = originalStorage
})

test('OCR sends the file as multipart to the analysis endpoint and maps date/warnings', async () => {
  calls = []; reply = { ...analysis, ocrProof: 'signed-proof' }
  const file = new File(['synthetic image'], 'receipt.png', { type: 'image/png' })
  const result = await expensesApi.analyzeReceipt(file)
  assert.equal(calls.length, 1)
  assert.equal(calls[0].url, '/api/expenses/receipts/analyze')
  assert.equal(calls[0].method, 'POST')
  assert.equal(calls[0].headers.Authorization, 'Bearer synthetic-test-token')
  assert.equal(calls[0].headers['Content-Type'], undefined)
  assert.equal(calls[0].body.get('file').name, file.name)
  assert.equal(await calls[0].body.get('file').text(), await file.text())
  assert.equal(result.ocrProof, 'signed-proof')
  assert.equal(toAnalysis(result).transactionDate, '2026-11-11')
  assert.equal(toAnalysis(result).transactionTime, undefined)
  assert.deepEqual(toAnalysis(result).warnings, ['Sprawdź dane'])
})

test('create and replace upload the expense, same file and OCR proof together', async () => {
  calls = []; reply = expense
  const file = new File(['synthetic image'], 'receipt.png', { type: 'image/png' })
  await expensesApi.create(form, { file, ocrProof: 'signed-proof' })
  await expensesApi.update('expense-id', form, 'version', { file, ocrProof: 'signed-proof' })
  assert.deepEqual(calls.map((c) => [c.method, c.url]), [['POST', '/api/expenses'], ['PUT', '/api/expenses/expense-id']])
  for (const call of calls) {
    assert.equal(call.body.get('file').name, file.name)
    assert.equal(call.body.get('ocrProof'), 'signed-proof')
    const body = JSON.parse(call.body.get('expense'))
    assert.equal(body.transactionTime, '2026-11-11')
    assert.equal(body.receiptId, undefined)
    if (call.method === 'PUT') { assert.equal(body.version, 'version'); assert.equal(body.removeReceipt, false) }
  }
})

test('image-only save omits OCR proof; JSON update can keep or detach an existing receipt', async () => {
  calls = []; reply = expense
  await expensesApi.create(form, { file: new File(['image'], 'receipt.png') })
  assert.equal(calls[0].body.has('ocrProof'), false)
  await expensesApi.update('expense-id', { ...form, receiptId: 'saved-receipt' }, 'version')
  await expensesApi.update('expense-id', { ...form, receiptId: null }, 'version')
  assert.equal(JSON.parse(calls[1].body).removeReceipt, false)
  assert.equal(JSON.parse(calls[2].body).removeReceipt, true)
})

test('read uses expense ID and maps the single saved analysis', async () => {
  calls = []; reply = { id: 'saved-receipt', expenseId: 'expense-id', fileUrl: 'https://storage.example.test/image', analysis }
  const receipt = await expensesApi.receipt('expense-id')
  assert.equal(calls[0].url, '/api/expenses/expense-id/receipt')
  assert.equal(receipt.originalAnalysis.transactionDate, '2026-11-11')
  assert.deepEqual(receipt.latestAnalysis.warnings, ['Sprawdź dane'])
  reply = { ...reply, analysis: null }
  assert.equal((await expensesApi.receipt('expense-id')).latestAnalysis, null)
})

test('preview of an edit sends expenseId without draft receipt IDs or files', async () => {
  calls = []; reply = { amount: '12.50', items: [], shares: [], bankTransactionId: null, receiptId: null }
  await expensesApi.preview(form, 'expense-id', 'version')
  const body = JSON.parse(calls[0].body)
  assert.equal(body.expenseId, 'expense-id')
  assert.equal(body.version, 'version')
  assert.equal(body.receiptId, undefined)
  assert.equal(body.transactionTime, '2026-11-11')
})

test('local receipt selection, OCR, retry and release retain the file and proof until a successful save', async () => {
  const { ExpensesProvider } = await vite.ssrLoadModule('/src/expensesStore.tsx')
  const { useExpenses } = await vite.ssrLoadModule('/src/expensesContext.ts')
  const { UserCtx } = await vite.ssrLoadModule('/src/userContext.ts')
  let store
  function Capture() { store = useExpenses(); return null }
  renderToString(createElement(UserCtx.Provider, { value: { isSignedIn: true } },
    createElement(ExpensesProvider, null, createElement(Capture))))
  calls = []
  const file = new File(['synthetic image'], 'receipt.png', { type: 'image/png' })
  const picked = await store.uploadReceipt(file)
  assert.equal(picked.ok, true)
  assert.equal(calls.length, 0, 'selecting a file must not call an upload endpoint')
  const id = picked.data.id
  assert.equal(store.getReceipt(id).isLocal, true)
  reply = { ...analysis, ocrProof: 'signed-proof' }
  assert.equal((await store.analyzeReceipt(id)).ok, true)
  assert.deepEqual(store.getReceipt(id).latestAnalysis.warnings, ['Sprawdź dane'])

  // A failed save must leave the local file and signed proof available for retry.
  const normalFetch = globalThis.fetch
  globalThis.fetch = async () => Response.json({ title: 'Synthetic failure' }, { status: 503 })
  assert.equal((await store.create({ ...form, receiptId: id })).ok, false)
  assert.equal(store.getReceipt(id).isLocal, true)
  globalThis.fetch = normalFetch
  reply = expense
  assert.equal((await store.create({ ...form, receiptId: id })).ok, true)
  assert.equal(calls.at(-1).body.get('ocrProof'), 'signed-proof')
  assert.equal(await calls.at(-1).body.get('file').text(), await file.text())
  assert.equal(store.getReceipt(id), undefined)

  const discarded = await store.uploadReceipt(file)
  const previousCalls = calls.length
  await store.deleteReceipt(discarded.data.id)
  assert.equal(store.getReceipt(discarded.data.id), undefined)
  assert.equal(calls.length, previousCalls, 'discarding a local file must not call DELETE')
})

test('expense validation accepts the OCR date without a transaction time', async () => {
  const { computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  const result = computeExpense({ ...form, shares: [{ userId: 'payer', percentage: '100' }] }, { activePayers: ['payer'], bankAmount: null })
  assert.equal(result.problems.fields.time, undefined)
})
