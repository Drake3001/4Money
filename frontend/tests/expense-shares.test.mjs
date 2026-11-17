import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'

let vite, balanceShares, changeShare, changeShareAmount, addShare, removeShare
const shares = (...values) => values.map((percentage, index) => ({ userId: `user-${index}`, percentage: String(percentage) }))
const total = (values) => values.reduce((sum, share) => sum + Math.round(Number(share.percentage) * 100), 0)

before(async () => {
  vite = await createServer({ logLevel: 'error' })
  ;({ balanceShares, changeShare, changeShareAmount, addShare, removeShare } = await vite.ssrLoadModule('/src/views/expenses/shareDistribution.ts'))
})
after(async () => { await vite?.close() })

test('moving a slider keeps an exact total and adjusts the other unlocked shares proportionally', () => {
  const next = changeShare(shares(50, 30, 20), new Set(), 'user-0', '60')
  assert.deepEqual(next.map(s => s.percentage), ['60', '24', '16'])
  assert.equal(total(next), 10_000)
})

test('locks preserve values and cap edits to the remaining percentage', () => {
  const locked = new Set(['user-0'])
  const initial = shares(40, 30, 30)
  assert.equal(changeShare(initial, locked, 'user-0', '10'), initial)
  const next = changeShare(initial, locked, 'user-1', '90')
  assert.deepEqual(next.map(s => s.percentage), ['40', '60', '0'])
  assert.equal(total(next), 10_000)
  const lastFree = changeShare(shares(40, 60), locked, 'user-1', '10')
  assert.deepEqual(lastFree.map(s => s.percentage), ['40', '60'])
})

test('adding and removing participants redistributes the remainder while preserving locks', () => {
  let next = addShare([], new Set(), 'user-0')
  assert.deepEqual(next, shares(100))
  next = addShare(next, new Set(), 'user-1')
  assert.deepEqual(next, shares(50, 50))
  next = addShare(next, new Set(['user-0']), 'user-2')
  assert.deepEqual(next, shares(50, 25, 25))
  assert.deepEqual(removeShare(next, new Set(['user-0']), 'user-1'), shares(50, 50).map((s, i) => ({ ...s, userId: i ? 'user-2' : s.userId })))
})

test('locked-only allocations cannot be changed indirectly by adding or removing a participant', () => {
  const initial = shares(40, 60)
  assert.equal(addShare(initial, new Set(['user-0', 'user-1']), 'new-user'), initial)
  assert.equal(removeShare(initial, new Set(['user-0']), 'user-1'), initial)
  assert.equal(balanceShares(initial, new Set(['user-0', 'user-1']), true), initial)
})

test('the current payer cannot be removed, and changing payer updates this protection', () => {
  const initial = shares(50, 50)
  assert.equal(removeShare(initial, new Set(), 'user-0', 'user-0'), initial)
  assert.deepEqual(removeShare(initial, new Set(), 'user-1', 'user-0'), shares(100))
  assert.equal(removeShare(initial, new Set(), 'user-1', 'user-1'), initial)
  assert.deepEqual(removeShare(initial, new Set(), 'user-0', 'user-1'), [{ userId: 'user-1', percentage: '100' }])
})

test('equal splits distribute rounding remainders exactly and respect locked percentages', () => {
  const next = balanceShares(shares(1, 1, 1), new Set(), true)
  assert.deepEqual(next, shares(33.34, 33.33, 33.33))
  assert.deepEqual(balanceShares(shares(20, 70, 10), new Set(['user-0']), true), shares(20, 40, 40))
})

test('repeated edits with many participants never lose precision or change a locked share', () => {
  let next = balanceShares(shares(...Array(17).fill(1)), new Set(), true)
  const locked = new Set(['user-0', 'user-3'])
  const originalLocked = next.filter(s => locked.has(s.userId))
  for (let i = 0; i < 300; i++) {
    const userId = `user-${1 + (i % 16)}`
    next = changeShare(next, locked, userId, String((i * 37) % 101))
    assert.equal(total(next), 10_000)
    assert.deepEqual(next.filter(s => locked.has(s.userId)), originalLocked)
    assert.ok(next.every(s => Number(s.percentage) >= 0 && Number(s.percentage) <= 100))
  }
})

test('invalid typed percentages remain visible for field validation without changing other shares', () => {
  for (const value of ['', '-1', '101', 'abc']) {
    const next = changeShare(shares(50, 50), new Set(), 'user-0', value)
    assert.equal(next[0].percentage, value)
    assert.equal(next[1].percentage, '50')
  }
})

test('amount edits convert to percentages and redistribute the remaining expense', () => {
  assert.deepEqual(changeShareAmount(shares(50, 50), new Set(), 'user-0', '75', 250), shares(30, 70))
  assert.deepEqual(changeShareAmount(shares(50, 50), new Set(), 'user-0', '12,50', 100), shares(12.5, 87.5))
})

test('amount edits preserve locks and clamp to the unlocked remainder', () => {
  const initial = shares(40, 30, 30)
  const locked = new Set(['user-0'])
  assert.equal(changeShareAmount(initial, locked, 'user-0', '10', 200), initial)
  assert.deepEqual(changeShareAmount(initial, locked, 'user-1', '90', 200), shares(40, 45, 15))
  assert.deepEqual(changeShareAmount(initial, locked, 'user-1', '150', 200), shares(40, 60, 0))
})

test('amount mode handles fractional expense totals and never changes shares without a usable total', () => {
  const initial = shares(50, 50)
  for (const amount of [0, -1, NaN, Infinity]) assert.equal(changeShareAmount(initial, new Set(), 'user-0', '10', amount), initial)
  const next = changeShareAmount(initial, new Set(), 'user-0', '10', 123.45)
  assert.deepEqual(next, shares(8.1, 91.9))
  assert.equal(total(next), 10_000)
})
