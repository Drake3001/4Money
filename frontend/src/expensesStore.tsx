import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { ExpensesCtx, type ExpensesApi } from './expensesContext'
import { ApiError, run } from './api/client'
import { expensesApi, toAnalysis, type ReceiptAttachment } from './api/expenses'
import { useUser } from './userContext'
import { MAX_RECEIPT_BYTES, RECEIPT_EXT, type Category, type Receipt } from './mocks/expenses'

// Expense module over the real API. Only the category list and the receipts seen in this session are kept here; everything else is fetched per view.
export function ExpensesProvider({ children }: { children: ReactNode }) {
  const { isSignedIn } = useUser()
  const [categories, setCats] = useState<Category[]>([])
  const [categoriesLoading, setCategoriesLoading] = useState(isSignedIn)
  const [categoriesError, setCategoriesError] = useState<number | null>(null)
  const [categoryVersion, setCategoryVersion] = useState(0)
  const reloadCategories = useCallback(() => setCategoryVersion((value) => value + 1), [])
  const [receipts, setReceipts] = useState<Record<string, Receipt>>({})
  const rc = useRef(receipts)
  const drafts = useRef<Record<string, ReceiptAttachment>>({})
  const putReceipt = (r: Receipt) => { rc.current = { ...rc.current, [r.id]: r }; setReceipts(rc.current) }
  const dropReceipt = (id: string) => { const { [id]: _gone, ...rest } = rc.current; rc.current = rest; setReceipts(rest) }
  const releaseDraft = (id: string) => {
    if (drafts.current[id]) {
      const url = rc.current[id]?.previewUrl
      if (url?.startsWith('blob:')) URL.revokeObjectURL(url)
      delete drafts.current[id]
    }
  }
  useEffect(() => () => { Object.keys(drafts.current).forEach(releaseDraft) }, [])

  useEffect(() => {
    if (!isSignedIn) {
      Object.keys(drafts.current).forEach(releaseDraft)
      rc.current = {}; setReceipts({}); setCats([]); setCategoriesLoading(false); setCategoriesError(null); return
    }
    let active = true
    setCategoriesLoading(true); setCategoriesError(null)
    void run(() => expensesApi.categories()).then((r) => {
      if (!active) return
      if (r.ok) setCats(r.data)
      else setCategoriesError(r.code)
      setCategoriesLoading(false)
    })
    return () => { active = false }
  }, [isSignedIn, categoryVersion])

  const api = useMemo<ExpensesApi>(() => ({
    categories,
    categoriesLoading, categoriesError, reloadCategories,
    list: (q) => run(() => expensesApi.list(q)),
    get: (id) => run(() => expensesApi.get(id)),
    preview: (f, expenseId, version) => run(() => expensesApi.preview(f, expenseId, version)),
    create: (f) => run(async () => {
      const result = await expensesApi.create(f, f.receiptId ? drafts.current[f.receiptId] : undefined)
      if (f.receiptId) { releaseDraft(f.receiptId); dropReceipt(f.receiptId) }
      return result
    }),
    update: (id, f, version) => run(async () => {
      const result = await expensesApi.update(id, f, version, f.receiptId ? drafts.current[f.receiptId] : undefined)
      if (f.receiptId && drafts.current[f.receiptId]) { releaseDraft(f.receiptId); dropReceipt(f.receiptId) }
      return result
    }),
    remove: (id, version) => run(() => expensesApi.remove(id, version)),
    availableBankTransactions: (f) => run(() => expensesApi.availableBankTransactions(f)),
    availableBankTransactionsPage: (f) => run(() => expensesApi.availableBankTransactionsPage(f)),
    bankTransaction: (id) => run(() => expensesApi.bankTransaction(id)),
    uploadReceipt: (file) => run(async () => {
      if (file.size === 0) throw new ApiError(400)
      if (file.size > MAX_RECEIPT_BYTES) throw new ApiError(413)
      if (!RECEIPT_EXT.includes(file.name.split('.').pop()?.toLowerCase() ?? '')) throw new ApiError(415)
      const id = `local-${crypto.randomUUID()}`
      drafts.current[id] = { file }
      const r: Receipt = { id, fileName: file.name, mimeType: file.type, size: file.size, previewUrl: URL.createObjectURL(file), createdAt: '', originalAnalysis: null, latestAnalysis: null, isLocal: true }
      putReceipt(r); return r
    }),
    getReceipt: (id) => rc.current[id],
    fetchReceipt: (expenseId) => run(async () => { const r = await expensesApi.receipt(expenseId); putReceipt(r); return r }),
    analyzeReceipt: (id) => run(async () => {
      let draft = drafts.current[id]
      const receipt = rc.current[id]
      if (!draft) {
        if (!receipt?.previewUrl) throw new ApiError(400)
        const response = await fetch(receipt.previewUrl)
        if (!response.ok) throw new ApiError(response.status)
        const blob = await response.blob()
        const type = blob.type.split(';')[0] || receipt.mimeType
        const extension = ({ 'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp', 'application/pdf': 'pdf', 'image/heic': 'heic', 'image/heif': 'heif', 'image/tiff': 'tiff', 'image/bmp': 'bmp' } as Record<string, string>)[type]
        const name = extension ? `paragon.${extension}` : receipt.fileName
        draft = { file: new File([blob], name, { type }) }
      }
      const response = await expensesApi.analyzeReceipt(draft.file)
      // A detached/replaced file must not be restored by a late OCR response.
      if (rc.current[id] !== receipt) throw new ApiError(409)
      drafts.current[id] = draft
      draft.ocrProof = response.ocrProof
      const a = toAnalysis(response)
      const cur = rc.current[id]
      if (cur) putReceipt({ ...cur, originalAnalysis: a, latestAnalysis: a })
      return a
    }),
    deleteReceipt: (id) => run(async () => { releaseDraft(id); dropReceipt(id) }),
  }), [categories, categoriesLoading, categoriesError, reloadCategories, receipts])

  return <ExpensesCtx.Provider value={api}>{children}</ExpensesCtx.Provider>
}
