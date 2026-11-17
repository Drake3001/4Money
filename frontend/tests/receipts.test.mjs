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
  assert.equal(calls[0].body.get('file').type, 'image/png')
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

test('receipt analysis and saving send the supported MIME type when the browser provides no specific type', async () => {
  const formats = {
    jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', webp: 'image/webp',
    heic: 'image/heic', heif: 'image/heif', tif: 'image/tiff', tiff: 'image/tiff',
    bmp: 'image/bmp', pdf: 'application/pdf',
  }
  const bytes = Uint8Array.of(0, 1, 128, 255)
  for (const [extension, expectedType] of Object.entries(formats)) {
    for (const type of ['', 'application/octet-stream']) {
      calls = []
      const file = new File([bytes], `receipt.${extension.toUpperCase()}`, { type })
      reply = { ...analysis, ocrProof: 'signed-proof' }
      const { ocrProof } = await expensesApi.analyzeReceipt(file)
      reply = expense
      await expensesApi.create(form, { file, ocrProof })
      await expensesApi.update('expense-id', form, 'version', { file, ocrProof })

      assert.equal(calls.length, 3)
      for (const call of calls) {
        const part = call.body.get('file')
        assert.equal(part.name, file.name)
        assert.equal(part.type, expectedType)
        assert.deepEqual(new Uint8Array(await part.arrayBuffer()), bytes)
      }
    }
  }
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

test('an existing image-only receipt can be analyzed and saved with its proof after a failed update', async () => {
  const { ExpensesProvider } = await vite.ssrLoadModule('/src/expensesStore.tsx')
  const { useExpenses } = await vite.ssrLoadModule('/src/expensesContext.ts')
  const { UserCtx } = await vite.ssrLoadModule('/src/userContext.ts')
  let store
  function Capture() { store = useExpenses(); return null }
  renderToString(createElement(UserCtx.Provider, { value: { isSignedIn: true } },
    createElement(ExpensesProvider, null, createElement(Capture))))
  calls = []
  reply = { id: 'saved-receipt', fileUrl: 'https://storage.example.test/receipt', analysis: null }
  await store.fetchReceipt('expense-id')
  const normalFetch = globalThis.fetch
  try {
    globalThis.fetch = async (url, options) => url === reply.fileUrl
      ? new Response('synthetic saved image', { headers: { 'Content-Type': 'image/png' } })
      : Response.json({ ...analysis, ocrProof: 'saved-image-proof' })
    assert.equal((await store.analyzeReceipt('saved-receipt')).ok, true)
    assert.equal(store.getReceipt('saved-receipt').latestAnalysis.amount, 12.5)
    globalThis.fetch = async () => Response.json({}, { status: 503 })
    assert.equal((await store.update('expense-id', { ...form, receiptId: 'saved-receipt' }, 'version')).ok, false)
    globalThis.fetch = normalFetch
    reply = expense
    assert.equal((await store.update('expense-id', { ...form, receiptId: 'saved-receipt' }, 'version')).ok, true)
    assert.equal(calls.at(-1).body.get('file').name, 'paragon.png')
    assert.equal(await calls.at(-1).body.get('file').text(), 'synthetic saved image')
    assert.equal(calls.at(-1).body.get('ocrProof'), 'saved-image-proof')
  } finally { globalThis.fetch = normalFetch }
})

test('a product-free expense assigns the full manual or bank amount to Nieznane', async () => {
  const { computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  for (const bankTransactionId of [null, 'bank-transaction']) {
    const result = computeExpense({ ...form, bankTransactionId, items: [], shares: [{ userId: 'payer', percentage: '100' }] },
      { activePayers: ['payer'], bankAmount: bankTransactionId ? -12.5 : null })
    assert.deepEqual(result.problems.list, [])
    assert.equal(result.preview.totalAmount, 12.5)
    assert.equal(result.preview.items.length, 1)
    assert.equal(result.preview.items[0].name, 'Nieznane')
    assert.equal(result.preview.items[0].total, 12.5)
    assert.equal(result.preview.shares[0].amount, 12.5)
  }
})

test('invalid discounts prevent an expense preview even when other product fields are incomplete', async () => {
  const { computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  for (const discount of ['1', '0,01', '-10.01']) {
    for (const categoryId of ['food', '']) {
      const result = computeExpense({ ...form, items: [{ id: 'item', categoryId, name: 'Produkt', unit: '', unitPrice: '5', quantity: '2', discount }], shares: [{ userId: 'payer', percentage: '100' }] }, { activePayers: ['payer'] })
      assert.equal(result.preview, null)
      assert.match(result.problems.itemFields.item.discount, discount.startsWith('-') ? /nie może być ujemna/ : /nie może być dodatni/)
    }
  }
})

test('discounts can reduce a product total to zero but cannot make it negative', async () => {
  const { computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  for (const discount of ['', '0', '-9.99', '-10']) {
    const result = computeExpense({ ...form, items: [{ id: 'item', categoryId: 'food', name: 'Produkt', unit: '', unitPrice: '5', quantity: '2', discount }], shares: [{ userId: 'payer', percentage: '100' }] }, { activePayers: ['payer'] })
    assert.deepEqual(result.problems.list, [])
    assert.ok(result.preview)
  }
})

test('a standalone discount allows a negative unit price only with a zero discount field', async () => {
  const { computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  for (const discount of ['', '0', '0,00', '-0', '-1', '1']) {
    const result = computeExpense({ ...form, items: [
      { id: 'product', categoryId: 'food', name: 'Produkt', unit: '', unitPrice: '10', quantity: '1', discount: '' },
      { id: 'reduction', categoryId: 'discount', name: 'Zniżka', unit: '', unitPrice: '-2', quantity: '1', discount },
    ], shares: [{ userId: 'payer', percentage: '100' }] }, { activePayers: ['payer'] })
    if (Number(discount.replace(',', '.')) === 0) {
      assert.deepEqual(result.problems.list, [])
      assert.equal(result.preview.items.find((item) => item.id === 'reduction').total, -2)
      assert.equal(result.preview.unknownAmount, 4.5)
    } else {
      assert.equal(result.preview, null)
      assert.ok(result.problems.itemFields.reduction.discount)
    }
  }
})

test('transaction statuses are displayed in Polish regardless of backend casing', async () => {
  const { transactionStatus } = await vite.ssrLoadModule('/src/mocks/bank.ts')
  assert.equal(transactionStatus('BOOKED'), 'Zaksięgowana')
  assert.equal(transactionStatus('booked'), 'Zaksięgowana')
  assert.equal(transactionStatus('pending'), 'Oczekująca')
})

test('applying the OCR proposal includes discounts, deposits and returns in the expense total exactly once', async () => {
  const { applyOcr } = await vite.ssrLoadModule('/src/views/expenses/OcrPanel.tsx')
  const { computeExpense } = await vite.ssrLoadModule('/src/mocks/expenses.ts')
  const parsed = toAnalysis({
    ...analysis,
    proposal: {
      ...analysis.proposal,
      amount: '16.50',
      items: [
        { name: 'Produkt', categoryId: 'food', unitPrice: '10.00', quantity: '2', discount: '-0.50' },
        { name: 'Opust', categoryId: 'discount', unitPrice: '-3.20', quantity: '1', discount: '0.00' },
        { name: 'Kaucja', categoryId: 'other', unitPrice: '0.50', quantity: '2', discount: '0.00' },
        { name: 'Zwrot kaucji', categoryId: 'other', unitPrice: '-0.80', quantity: '1', discount: '0.00' },
      ],
    },
  })
  const initial = { ...form, shares: [{ userId: 'payer', percentage: '100' }] }
  const applied = applyOcr(initial, parsed, 'overwrite', false).next
  const reapplied = applyOcr(applied, parsed, 'overwrite', false).next
  const result = computeExpense(reapplied, { activePayers: ['payer'], bankAmount: null })
  assert.deepEqual(result.problems.list, [])
  assert.equal(result.preview.unknownAmount, 0)
  assert.equal(result.preview.totalAmount, 16.5)
  assert.deepEqual(result.preview.items.map((item) => item.total), [19.5, -3.2, 1, -0.8])
})

test('the analysis panel shows a review notice without exposing raw worker warnings', async () => {
  const { ExpensesCtx } = await vite.ssrLoadModule('/src/expensesContext.ts')
  const { default: OcrPanel } = await vite.ssrLoadModule('/src/views/expenses/OcrPanel.tsx')
  const parsed = toAnalysis({ ...analysis, warnings: [{ message: 'Page 1 contains synthetic technical details' }] })
  const receipt = { id: 'receipt', previewUrl: null, mimeType: 'image/png', fileName: 'receipt.png', originalAnalysis: parsed, latestAnalysis: parsed }
  const html = renderToString(createElement(ExpensesCtx.Provider, { value: { categories: [], categoriesLoading: false, categoriesError: null } },
    createElement(OcrPanel, { receipt, form, hasBank: false, onApply() {}, onClose() {} })))
  assert.match(html, /Analiza wymaga sprawdzenia/)
  assert.doesNotMatch(html, /Page 1 contains|synthetic technical details/)
})
