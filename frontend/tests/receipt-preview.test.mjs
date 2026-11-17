import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'

let vite, receiptPreview, conversions, downloads
const originalFetch = globalThis.fetch

before(async () => {
  // Isolate the browser-only decoder; exercise download, format detection and cancellation.
  globalThis.receiptPreviewDecoder = {
    isHeic: async (file) => await file.text() === 'synthetic HEIC',
    heicTo: async (options) => {
      conversions.push(options)
      return new Blob(['synthetic JPEG'], { type: 'image/jpeg' })
    },
  }
  vite = await createServer({ logLevel: 'error', ssr: { noExternal: ['heic-to'] }, plugins: [{
    name: 'receipt-preview-decoder', enforce: 'pre',
    resolveId(id) { if (id === 'heic-to/csp') return '\0receipt-preview-decoder' },
    load(id) {
      if (id === '\0receipt-preview-decoder') return 'export const { isHeic, heicTo } = globalThis.receiptPreviewDecoder'
    },
  }] })
  ;({ receiptPreview } = await vite.ssrLoadModule('/src/shared/receiptPreview.ts'))
})

after(async () => {
  await vite?.close()
  globalThis.fetch = originalFetch
  delete globalThis.receiptPreviewDecoder
})

test('HEIC preview uses file contents even without a filename or MIME type', async () => {
  conversions = []; downloads = []
  globalThis.fetch = async (url, options) => {
    downloads.push({ url, ...options })
    return new Response(new Blob(['synthetic HEIC']))
  }
  const controller = new AbortController()
  const preview = await receiptPreview('blob:original-receipt', controller.signal)
  assert.equal(preview.type, 'image/jpeg')
  assert.equal(await preview.text(), 'synthetic JPEG')
  assert.equal(downloads[0].url, 'blob:original-receipt')
  assert.equal(downloads[0].signal, controller.signal)
  assert.equal(conversions[0].type, 'image/jpeg')
  assert.equal(await conversions[0].blob.text(), 'synthetic HEIC')
})

test('a failed download and an unsupported image do not trigger HEIC conversion', async () => {
  conversions = []
  globalThis.fetch = async () => new Response(null, { status: 403 })
  await assert.rejects(receiptPreview('https://storage.example.test/expired', new AbortController().signal))
  globalThis.fetch = async () => new Response('synthetic unsupported file')
  await assert.rejects(receiptPreview('blob:unsupported', new AbortController().signal))
  assert.equal(conversions.length, 0)
})

test('discarding a receipt during download prevents conversion', async () => {
  conversions = []
  const controller = new AbortController()
  globalThis.fetch = async () => {
    controller.abort()
    return new Response('synthetic HEIC')
  }
  await assert.rejects(receiptPreview('blob:discarded', controller.signal), { name: 'AbortError' })
  assert.equal(conversions.length, 0)
})
