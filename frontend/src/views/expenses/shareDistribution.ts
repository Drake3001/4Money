import type { FormShare } from '../../mocks/expenses'

const TOTAL = 10_000
const units = (value: string) => {
  const number = Number(value.trim().replace(',', '.'))
  return Number.isFinite(number) ? Math.round(Math.min(100, Math.max(0, number)) * 100) : 0
}
const valueOf = (value: number) => String(value / 100)
const lockedTotal = (shares: FormShare[], locked: ReadonlySet<string>) =>
  shares.reduce((sum, share) => sum + (locked.has(share.userId) ? units(share.percentage) : 0), 0)

/** Allocate hundredths of a percent, keeping locked values and an exact total. */
export function balanceShares(shares: FormShare[], locked: ReadonlySet<string>, equally = false): FormShare[] {
  const available = TOTAL - lockedTotal(shares, locked)
  const free = shares.filter((share) => !locked.has(share.userId))
  if (available < 0 || !free.length) return shares
  const weights = free.map((share) => equally ? 1 : units(share.percentage))
  const totalWeight = weights.reduce((sum, weight) => sum + weight, 0)
  const exact = weights.map((weight) => available * (totalWeight ? weight / totalWeight : 1 / free.length))
  const allocated = exact.map(Math.floor)
  const remainder = available - allocated.reduce((sum, amount) => sum + amount, 0)
  const order = exact.map((amount, index) => ({ index, fraction: amount - allocated[index] }))
    .sort((a, b) => b.fraction - a.fraction || a.index - b.index)
  for (let i = 0; i < remainder; i++) allocated[order[i].index]++
  const values = new Map(free.map((share, index) => [share.userId, valueOf(allocated[index])]))
  return shares.map((share) => locked.has(share.userId) ? share : { ...share, percentage: values.get(share.userId)! })
}

export function changeShare(shares: FormShare[], locked: ReadonlySet<string>, userId: string, value: string): FormShare[] {
  if (locked.has(userId) || !shares.some((share) => share.userId === userId)) return shares
  const number = Number(value.trim().replace(',', '.'))
  if (!value.trim() || !Number.isFinite(number) || number < 0 || number > 100) {
    return shares.map((share) => share.userId === userId ? { ...share, percentage: value } : share)
  }
  const available = Math.max(0, TOTAL - lockedTotal(shares, locked))
  const others = shares.filter((share) => share.userId !== userId && !locked.has(share.userId))
  const target = others.length ? Math.min(units(value), available) : available
  const next = shares.map((share) => share.userId === userId ? { ...share, percentage: valueOf(target) } : share)
  return balanceShares(next, new Set([...locked, userId]))
}

/** Amounts are an editing aid; the Expense API still stores percentage shares. */
export function changeShareAmount(shares: FormShare[], locked: ReadonlySet<string>, userId: string, amount: string, totalAmount: number): FormShare[] {
  if (!Number.isFinite(totalAmount) || totalAmount <= 0) return shares
  const value = Number(amount.trim().replace(',', '.'))
  if (!amount.trim() || !Number.isFinite(value)) return changeShare(shares, locked, userId, '')
  const percentage = Math.round((value / totalAmount) * TOTAL) / 100
  return changeShare(shares, locked, userId, String(percentage))
}

export function addShare(shares: FormShare[], locked: ReadonlySet<string>, userId: string): FormShare[] {
  if (shares.some((share) => share.userId === userId)) return shares
  const available = TOTAL - lockedTotal(shares, locked)
  const freeCount = shares.filter((share) => !locked.has(share.userId)).length
  if (available <= 0 || (shares.length > 0 && !freeCount)) return shares
  const target = Math.floor(available / (freeCount + 1))
  if (!target) return shares
  return balanceShares([...shares, { userId, percentage: valueOf(target) }], new Set([...locked, userId]))
}

export function removeShare(shares: FormShare[], locked: ReadonlySet<string>, userId: string, payerId?: string): FormShare[] {
  if (userId === payerId) return shares
  const next = shares.filter((share) => share.userId !== userId)
  if (next.length && next.every((share) => locked.has(share.userId)) && lockedTotal(next, locked) !== TOTAL) return shares
  return balanceShares(next, locked)
}
